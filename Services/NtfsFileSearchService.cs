using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace Clipman.Services;

public sealed class NtfsFileSearchService : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    private const uint FsctlQueryUsnJournal = 0x000900f4;
    private const uint FsctlEnumUsnData = 0x000900b3;
    private const uint FsctlReadUsnJournal = 0x000900bb;

    private const int EnumBufferSize = 1 << 20; // 1 MB
    private const int ReadBufferSize = 1 << 20; // 1 MB
    private const int FileAttributeDirectory = 0x10;
    private const int UsnReasonFileDelete = unchecked((int)0x00000200);

    private readonly object _gate = new();
    private readonly Dictionary<string, VolumeState> _volumes = new(StringComparer.OrdinalIgnoreCase);
    private List<FileSearchEntry> _snapshot = [];
    private CancellationTokenSource? _lifetimeCts;
    private Task? _backgroundTask;

    public bool IsRunning => _backgroundTask is { IsCompleted: false };

    public void Start()
    {
        lock (_gate)
        {
            if (_backgroundTask is { IsCompleted: false })
            {
                return;
            }

            _lifetimeCts = new CancellationTokenSource();
            _backgroundTask = Task.Run(() => RunAsync(_lifetimeCts.Token));
        }
    }

    public IReadOnlyList<FileSearchEntry> Search(string? query, int limit = 300)
    {
        if (limit <= 0)
        {
            return [];
        }

        var snapshot = Volatile.Read(ref _snapshot);
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return snapshot.Take(limit).ToList();
        }

        var terms = trimmed
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.ToLowerInvariant())
            .ToArray();
        if (terms.Length == 0)
        {
            return snapshot.Take(limit).ToList();
        }

        var ranked = snapshot
            .Select(entry => new
            {
                Entry = entry,
                Score = ScoreEntry(entry, terms)
            })
            .Where(item => item.Score >= 0)
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Entry.Path, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(item => item.Entry)
            .ToList();

        return ranked;
    }

    public void Dispose()
    {
        Stop();
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? background;
        lock (_gate)
        {
            cts = _lifetimeCts;
            background = _backgroundTask;
            _lifetimeCts = null;
            _backgroundTask = null;
        }

        if (cts is not null)
        {
            cts.Cancel();
        }

        if (background is not null)
        {
            try
            {
                background.GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        cts?.Dispose();

        Volatile.Write(ref _snapshot, []);
        _volumes.Clear();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var drives = Environment.GetLogicalDrives()
            .Where(drive => !string.IsNullOrWhiteSpace(drive))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var driveRoot in drives)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                _ = IndexVolume(driveRoot);
            }
            catch
            {
                // Keep startup resilient when a single volume cannot be indexed.
            }
        }

        RebuildSnapshot();

        while (!cancellationToken.IsCancellationRequested)
        {
            var changed = false;
            foreach (var volume in _volumes.Values.ToArray())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    changed |= ReadJournalChanges(volume);
                }
                catch
                {
                    // Journal reads can fail for external/removable media churn.
                }
            }

            if (changed)
            {
                RebuildSnapshot();
            }
            try
            {
                await Task.Delay(450, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private bool IndexVolume(string driveRoot)
    {
        var volumePath = BuildVolumePath(driveRoot);
        using var handle = CreateFile(
            volumePath,
            GenericRead,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return false;
        }

        if (!TryDeviceIoControl(handle, FsctlQueryUsnJournal, null, out UsnJournalDataV0 journalData))
        {
            return false;
        }

        var state = new VolumeState(driveRoot, journalData.UsnJournalID, journalData.NextUsn);
        EnumerateMftEntries(handle, journalData.NextUsn, state);
        _volumes[driveRoot] = state;
        return true;
    }

    private void EnumerateMftEntries(SafeFileHandle volumeHandle, long highUsn, VolumeState state)
    {
        var enumData = new MftEnumDataV0
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = highUsn
        };

        while (true)
        {
            if (!TryDeviceIoControl(volumeHandle, FsctlEnumUsnData, enumData, EnumBufferSize, out var outputBytes))
            {
                break;
            }

            if (outputBytes.Length <= sizeof(long))
            {
                break;
            }

            enumData.StartFileReferenceNumber = (ulong)BitConverter.ToInt64(outputBytes, 0);
            var offset = sizeof(long);
            while (offset + 60 <= outputBytes.Length)
            {
                if (!TryReadUsnRecordV2(outputBytes, offset, out var record, out var nextOffset))
                {
                    break;
                }

                offset = nextOffset;
                if (record.FileReferenceNumber == 0 || string.IsNullOrWhiteSpace(record.FileName))
                {
                    continue;
                }

                var node = new NtfsNode(
                    record.FileReferenceNumber,
                    record.ParentFileReferenceNumber,
                    record.FileName,
                    (record.FileAttributes & FileAttributeDirectory) != 0);
                state.Nodes[record.FileReferenceNumber] = node;
            }
        }
    }

    private bool ReadJournalChanges(VolumeState state)
    {
        var volumePath = BuildVolumePath(state.DriveRoot);
        using var handle = CreateFile(
            volumePath,
            GenericRead,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return false;
        }

        if (!TryDeviceIoControl(handle, FsctlQueryUsnJournal, null, out UsnJournalDataV0 latestJournal))
        {
            return false;
        }

        if (latestJournal.UsnJournalID != state.UsnJournalId)
        {
            // Journal reset (e.g. churn or volume maintenance). Full re-index that volume.
            return IndexVolume(state.DriveRoot);
        }

        var readData = new ReadUsnJournalDataV0
        {
            StartUsn = state.NextUsn,
            ReasonMask = uint.MaxValue,
            ReturnOnlyOnClose = 0,
            Timeout = 0,
            BytesToWaitFor = 0,
            UsnJournalID = latestJournal.UsnJournalID
        };

        if (!TryDeviceIoControl(handle, FsctlReadUsnJournal, readData, ReadBufferSize, out var outputBytes))
        {
            return false;
        }

        if (outputBytes.Length < sizeof(long))
        {
            return false;
        }

        state.NextUsn = BitConverter.ToInt64(outputBytes, 0);
        var changed = false;
        var offset = sizeof(long);
        while (offset + 60 <= outputBytes.Length)
        {
            if (!TryReadUsnRecordV2(outputBytes, offset, out var record, out var nextOffset))
            {
                break;
            }

            offset = nextOffset;
            if (record.FileReferenceNumber == 0 || string.IsNullOrWhiteSpace(record.FileName))
            {
                continue;
            }

            if ((record.Reason & UsnReasonFileDelete) != 0)
            {
                changed |= state.Nodes.Remove(record.FileReferenceNumber);
                continue;
            }

            var node = new NtfsNode(
                record.FileReferenceNumber,
                record.ParentFileReferenceNumber,
                record.FileName,
                (record.FileAttributes & FileAttributeDirectory) != 0);
            state.Nodes[record.FileReferenceNumber] = node;
            changed = true;
        }

        return changed;
    }

    private void RebuildSnapshot()
    {
        var updated = new List<FileSearchEntry>(capacity: 250_000);
        foreach (var volume in _volumes.Values)
        {
            foreach (var node in volume.Nodes.Values)
            {
                if (node.Name.Length == 0 || node.Name == "." || node.Name == "..")
                {
                    continue;
                }

                var path = BuildPath(volume, node.ReferenceNumber);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                updated.Add(new FileSearchEntry(node.Name, path, volume.DriveRoot, node.IsDirectory));
            }
        }

        updated.Sort(static (left, right) =>
        {
            var nameCompare = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            if (nameCompare != 0)
            {
                return nameCompare;
            }

            return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
        });
        Volatile.Write(ref _snapshot, updated);
    }

    private static string BuildPath(VolumeState volume, ulong referenceNumber)
    {
        if (!volume.Nodes.TryGetValue(referenceNumber, out var node))
        {
            return string.Empty;
        }

        var segments = new List<string>(8);
        var current = node;
        var seen = new HashSet<ulong>();
        while (true)
        {
            if (!seen.Add(current.ReferenceNumber))
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(current.Name) &&
                current.Name != "." &&
                current.Name != ".." &&
                current.Name != "\\")
            {
                segments.Add(current.Name);
            }

            if (current.ParentReferenceNumber == 0 ||
                current.ParentReferenceNumber == current.ReferenceNumber ||
                !volume.Nodes.TryGetValue(current.ParentReferenceNumber, out current))
            {
                break;
            }
        }

        segments.Reverse();
        var root = volume.DriveRoot.EndsWith('\\') ? volume.DriveRoot : $"{volume.DriveRoot}\\";
        if (segments.Count == 0)
        {
            return root;
        }

        return Path.Combine([root, .. segments]);
    }

    private static int ScoreEntry(FileSearchEntry entry, IReadOnlyList<string> terms)
    {
        var lowerName = entry.Name.ToLowerInvariant();
        var lowerPath = entry.Path.ToLowerInvariant();

        var score = 0;
        foreach (var term in terms)
        {
            if (lowerName.StartsWith(term, StringComparison.Ordinal))
            {
                score += 0;
                continue;
            }

            if (lowerName.Contains(term, StringComparison.Ordinal))
            {
                score += 1;
                continue;
            }

            if (lowerPath.Contains(term, StringComparison.Ordinal))
            {
                score += 2;
                continue;
            }

            return -1;
        }

        return score;
    }

    private static bool TryReadUsnRecordV2(byte[] buffer, int offset, out UsnRecord record, out int nextOffset)
    {
        record = default;
        nextOffset = offset;

        if (offset + 60 > buffer.Length)
        {
            return false;
        }

        var recordLength = BitConverter.ToInt32(buffer, offset);
        if (recordLength <= 0 || offset + recordLength > buffer.Length)
        {
            return false;
        }

        nextOffset = offset + recordLength;
        var majorVersion = BitConverter.ToUInt16(buffer, offset + 4);
        if (majorVersion != 2)
        {
            return true; // Skip unsupported versions while continuing the scan.
        }

        var fileNameLength = BitConverter.ToUInt16(buffer, offset + 56);
        var fileNameOffset = BitConverter.ToUInt16(buffer, offset + 58);
        var fileNameBytesStart = offset + fileNameOffset;
        if (fileNameLength <= 0 ||
            fileNameBytesStart < offset ||
            fileNameBytesStart + fileNameLength > offset + recordLength)
        {
            return true;
        }

        var fileName = Encoding.Unicode.GetString(buffer, fileNameBytesStart, fileNameLength);
        record = new UsnRecord(
            BitConverter.ToUInt64(buffer, offset + 8),
            BitConverter.ToUInt64(buffer, offset + 16),
            BitConverter.ToInt32(buffer, offset + 40),
            BitConverter.ToInt32(buffer, offset + 52),
            fileName);
        return true;
    }

    private static string BuildVolumePath(string driveRoot)
    {
        var normalized = driveRoot.TrimEnd('\\');
        return $@"\\.\{normalized}";
    }

    private static bool TryDeviceIoControl<TInput>(
        SafeFileHandle handle,
        uint controlCode,
        TInput input,
        int outputBufferSize,
        out byte[] output)
        where TInput : struct
    {
        var inputBytes = StructureToBytes(input);
        output = new byte[outputBufferSize];
        var result = DeviceIoControl(
            handle,
            controlCode,
            inputBytes,
            inputBytes.Length,
            output,
            output.Length,
            out var bytesReturned,
            IntPtr.Zero);
        if (!result || bytesReturned <= 0)
        {
            output = [];
            return false;
        }

        if (bytesReturned == output.Length)
        {
            return true;
        }

        Array.Resize(ref output, bytesReturned);
        return true;
    }

    private static bool TryDeviceIoControl<TOutput>(
        SafeFileHandle handle,
        uint controlCode,
        byte[]? input,
        out TOutput output)
        where TOutput : struct
    {
        var outputBytes = new byte[Marshal.SizeOf<TOutput>()];
        var inputBytes = input ?? [];
        var result = DeviceIoControl(
            handle,
            controlCode,
            inputBytes,
            inputBytes.Length,
            outputBytes,
            outputBytes.Length,
            out var bytesReturned,
            IntPtr.Zero);
        if (!result || bytesReturned < outputBytes.Length)
        {
            output = default;
            return false;
        }

        output = BytesToStructure<TOutput>(outputBytes);
        return true;
    }

    private static byte[] StructureToBytes<T>(T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buffer = new byte[size];
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, ptr, false);
            Marshal.Copy(ptr, buffer, 0, size);
            return buffer;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static T BytesToStructure<T>(byte[] buffer) where T : struct
    {
        var ptr = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            Marshal.Copy(buffer, 0, ptr, buffer.Length);
            return Marshal.PtrToStructure<T>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct MftEnumDataV0
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReadUsnJournalDataV0
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UsnJournalDataV0
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    private sealed class VolumeState
    {
        public VolumeState(string driveRoot, ulong usnJournalId, long nextUsn)
        {
            DriveRoot = driveRoot;
            UsnJournalId = usnJournalId;
            NextUsn = nextUsn;
        }

        public string DriveRoot { get; }
        public ulong UsnJournalId { get; }
        public long NextUsn { get; set; }
        public Dictionary<ulong, NtfsNode> Nodes { get; } = [];
    }

    private readonly record struct NtfsNode(
        ulong ReferenceNumber,
        ulong ParentReferenceNumber,
        string Name,
        bool IsDirectory);

    private readonly record struct UsnRecord(
        ulong FileReferenceNumber,
        ulong ParentFileReferenceNumber,
        int Reason,
        int FileAttributes,
        string FileName);
}

public sealed record FileSearchEntry(string Name, string Path, string Volume, bool IsDirectory);

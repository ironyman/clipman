using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clipman.Models;
using Microsoft.Isam.Esent.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Clipman.Services;

public sealed class EsentClipboardHistoryService : IClipboardHistoryService, IDisposable
{
    private const string TableName = "Clips";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Instance _instance;
    private readonly string _databasePath;
    private bool _disposed;

    public EsentClipboardHistoryService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clipman",
            "Esent");
        Directory.CreateDirectory(root);

        _databasePath = Path.Combine(root, "history.edb");
        _instance = new Instance("ClipmanHistory")
        {
            Parameters =
            {
                SystemDirectory = root,
                LogFileDirectory = root,
                TempDirectory = root,
                CreatePathIfNotExist = true,
                CircularLog = true
            }
        };

        _instance.Init();
        EnsureDatabase();
    }

    public event EventHandler<ClipboardClip>? ClipAdded;

    public async Task<IReadOnlyList<ClipboardClip>> GetPageAsync(
        int skip,
        int take,
        string? query = null,
        ClipKind? kind = null,
        bool pinnedOnly = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return ReadPage(skip, take, query, kind, pinnedOnly);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var session = new Session(_instance);
            var dbid = OpenDatabase(session);
            using var table = new Table(session, dbid, TableName, OpenTableGrbit.ReadOnly);
            return CountRows(session, table);
        }
        catch
        {
            return 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CaptureCurrentClipboardAsync(CancellationToken cancellationToken = default)
    {
        var clip = await ClipboardClipFactory.FromClipboardAsync(cancellationToken);
        if (clip is null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Exists(clip.Id))
            {
                return;
            }

            WriteClip(clip);
        }
        finally
        {
            _gate.Release();
        }

        ClipAdded?.Invoke(this, clip);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _instance.Dispose();
        _gate.Dispose();
    }

    private void EnsureDatabase()
    {
        using var session = new Session(_instance);
        JET_DBID dbid;

        if (File.Exists(_databasePath))
        {
            Api.JetAttachDatabase(session, _databasePath, AttachDatabaseGrbit.None);
            Api.JetOpenDatabase(session, _databasePath, null, out dbid, OpenDatabaseGrbit.None);
        }
        else
        {
            Api.JetCreateDatabase(session, _databasePath, null, out dbid, CreateDatabaseGrbit.None);
        }

        try
        {
            Api.JetCreateTable(session, dbid, TableName, 16, 100, out var table);
            try
            {
                AddColumn(session, table, "Id", JET_coltyp.LongText);
                AddColumn(session, table, "Kind", JET_coltyp.LongText);
                AddColumn(session, table, "Title", JET_coltyp.LongText);
                AddColumn(session, table, "Preview", JET_coltyp.LongText);
                AddColumn(session, table, "ContentText", JET_coltyp.LongText);
                AddColumn(session, table, "ContentBytes", JET_coltyp.LongBinary);
                AddColumn(session, table, "ReferencePath", JET_coltyp.LongText);
                AddColumn(session, table, "FormatsJson", JET_coltyp.LongText);
                AddColumn(session, table, "SourceApp", JET_coltyp.LongText);
                AddColumn(session, table, "FormatLabel", JET_coltyp.LongText);
                AddColumn(session, table, "CopiedAt", JET_coltyp.Currency);
                AddColumn(session, table, "IsPinned", JET_coltyp.Bit);
                AddColumn(session, table, "UseCount", JET_coltyp.Long);

                Api.JetCreateIndex(session, table, "primary", CreateIndexGrbit.IndexPrimary, "+Id\0\0", 5, 100);
                Api.JetCreateIndex(session, table, "copiedAt", CreateIndexGrbit.None, "-CopiedAt\0\0", 11, 100);
            }
            finally
            {
                Api.JetCloseTable(session, table);
            }
        }
        catch (EsentTableDuplicateException)
        {
        }
    }

    private static void AddColumn(Session session, JET_TABLEID table, string name, JET_coltyp type)
    {
        var definition = new JET_COLUMNDEF
        {
            coltyp = type,
            cp = JET_CP.Unicode
        };
        Api.JetAddColumn(session, table, name, definition, null, 0, out _);
    }

    private JET_DBID OpenDatabase(Session session)
    {
        Api.JetOpenDatabase(session, _databasePath, null, out var dbid, OpenDatabaseGrbit.None);
        return dbid;
    }

    private IReadOnlyList<ClipboardClip> ReadPage(int skip, int take, string? query, ClipKind? kind, bool pinnedOnly)
    {
        using var session = new Session(_instance);
        var dbid = OpenDatabase(session);
        using var table = new Table(session, dbid, TableName, OpenTableGrbit.ReadOnly);
        var columns = Columns(session, table);
        var clips = new List<ClipboardClip>(take);
        var skipped = 0;
        var normalizedQuery = query?.Trim();

        Api.JetSetCurrentIndex(session, table, "copiedAt");
        if (!Api.TryMoveFirst(session, table))
        {
            return clips;
        }

        do
        {
            var clip = ReadClip(session, table, columns);
            if (Matches(clip, normalizedQuery, kind, pinnedOnly))
            {
                if (skipped++ >= skip)
                {
                    clips.Add(clip);
                    if (clips.Count >= take)
                    {
                        break;
                    }
                }
            }
        }
        while (Api.TryMoveNext(session, table));

        return clips;
    }

    private static bool Matches(ClipboardClip clip, string? query, ClipKind? kind, bool pinnedOnly)
    {
        return (!pinnedOnly || clip.IsPinned) &&
               (kind is null || clip.Kind == kind) &&
               (string.IsNullOrWhiteSpace(query) ||
                clip.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                clip.Preview.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (clip.ReferencePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (clip.ContentText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private bool Exists(string id)
    {
        using var session = new Session(_instance);
        var dbid = OpenDatabase(session);
        using var table = new Table(session, dbid, TableName, OpenTableGrbit.ReadOnly);

        Api.JetSetCurrentIndex(session, table, "primary");
        Api.MakeKey(session, table, id, Encoding.Unicode, MakeKeyGrbit.NewKey);
        return Api.TrySeek(session, table, SeekGrbit.SeekEQ);
    }

    private void WriteClip(ClipboardClip clip)
    {
        using var session = new Session(_instance);
        var dbid = OpenDatabase(session);
        using var table = new Table(session, dbid, TableName, OpenTableGrbit.None);
        var columns = Columns(session, table);

        using var transaction = new Transaction(session);
        using var update = new Update(session, table, JET_prep.Insert);
        SetText(session, table, columns["Id"], clip.Id);
        SetText(session, table, columns["Kind"], clip.Kind.ToString());
        SetText(session, table, columns["Title"], clip.Title);
        SetText(session, table, columns["Preview"], clip.Preview);
        SetText(session, table, columns["ContentText"], clip.ContentText);
        SetBytes(session, table, columns["ContentBytes"], clip.ContentBytes);
        SetText(session, table, columns["ReferencePath"], clip.ReferencePath);
        SetText(session, table, columns["FormatsJson"], clip.FormatsJson);
        SetText(session, table, columns["SourceApp"], clip.SourceApp);
        SetText(session, table, columns["FormatLabel"], clip.FormatLabel);
        Api.SetColumn(session, table, columns["CopiedAt"], clip.CopiedAt.UtcTicks);
        Api.SetColumn(session, table, columns["IsPinned"], clip.IsPinned);
        Api.SetColumn(session, table, columns["UseCount"], clip.UseCount);
        update.Save();
        transaction.Commit(CommitTransactionGrbit.LazyFlush);
    }

    private static IDictionary<string, JET_COLUMNID> Columns(Session session, JET_TABLEID table) =>
        Api.GetColumnDictionary(session, table);

    private static ClipboardClip ReadClip(Session session, JET_TABLEID table, IDictionary<string, JET_COLUMNID> columns)
    {
        var kindText = GetText(session, table, columns["Kind"]) ?? nameof(ClipKind.Other);
        var copiedAtTicks = Api.RetrieveColumnAsInt64(session, table, columns["CopiedAt"]) ?? DateTimeOffset.Now.UtcTicks;

        return new ClipboardClip
        {
            Id = GetText(session, table, columns["Id"]) ?? Guid.NewGuid().ToString("N"),
            Kind = Enum.TryParse<ClipKind>(kindText, out var kind) ? kind : ClipKind.Other,
            Title = GetText(session, table, columns["Title"]) ?? "Clipboard item",
            Preview = GetText(session, table, columns["Preview"]) ?? string.Empty,
            ContentText = GetText(session, table, columns["ContentText"]),
            ContentBytes = Api.RetrieveColumn(session, table, columns["ContentBytes"]),
            ReferencePath = GetText(session, table, columns["ReferencePath"]),
            FormatsJson = GetText(session, table, columns["FormatsJson"]),
            SourceApp = GetText(session, table, columns["SourceApp"]),
            FormatLabel = GetText(session, table, columns["FormatLabel"]),
            CopiedAt = new DateTimeOffset(copiedAtTicks, TimeSpan.Zero).ToLocalTime(),
            IsPinned = Api.RetrieveColumnAsBoolean(session, table, columns["IsPinned"]) ?? false,
            UseCount = Api.RetrieveColumnAsInt32(session, table, columns["UseCount"]) ?? 0
        };
    }

    private static void SetText(Session session, JET_TABLEID table, JET_COLUMNID column, string? value)
    {
        if (value is not null)
        {
            Api.SetColumn(session, table, column, value, Encoding.Unicode);
        }
    }

    private static string? GetText(Session session, JET_TABLEID table, JET_COLUMNID column) =>
        Api.RetrieveColumnAsString(session, table, column, Encoding.Unicode);

    private static void SetBytes(Session session, JET_TABLEID table, JET_COLUMNID column, byte[]? value)
    {
        if (value is not null)
        {
            Api.SetColumn(session, table, column, value);
        }
    }

    private static int CountRows(Session session, JET_TABLEID table)
    {
        var count = 0;
        if (!Api.TryMoveFirst(session, table))
        {
            return count;
        }

        do
        {
            count++;
        }
        while (Api.TryMoveNext(session, table));

        return count;
    }

    private static class ClipboardClipFactory
    {
        public static async Task<ClipboardClip?> FromClipboardAsync(CancellationToken cancellationToken)
        {
            DataPackageView view;
            try
            {
                view = Clipboard.GetContent();
            }
            catch
            {
                return null;
            }

            var formats = view.AvailableFormats.ToArray();
            if (formats.Length == 0)
            {
                return null;
            }

            var copiedAt = DateTimeOffset.Now;
            var formatsJson = JsonSerializer.Serialize(formats);
            var fileClip = await TryCreateStorageItemClipAsync(view, formatsJson, copiedAt);
            if (fileClip is not null)
            {
                return fileClip;
            }

            if (view.Contains(StandardDataFormats.Bitmap))
            {
                var imageBytes = await ReadBitmapAsync(view);
                if (imageBytes is not null)
                {
                    return BuildClip(ClipKind.Image, "Clipboard image", "Bitmap image captured from clipboard", "Bitmap image", null, imageBytes, null, formatsJson, copiedAt);
                }
            }

            if (view.Contains(StandardDataFormats.Text))
            {
                var text = await view.GetTextAsync().AsTask(cancellationToken);
                var kind = DetectTextKind(text);
                return BuildClip(kind, TitleFromText(text, kind), PreviewText(text), LabelForText(kind, text), text, null, null, formatsJson, copiedAt);
            }

            if (view.Contains(StandardDataFormats.Html))
            {
                var html = await view.GetHtmlFormatAsync().AsTask(cancellationToken);
                return BuildClip(ClipKind.Html, "HTML fragment", PreviewText(HtmlFormatHelper.GetStaticFragment(html)), "HTML", html, null, null, formatsJson, copiedAt);
            }

            return BuildClip(ClipKind.Other, "Clipboard data", string.Join(", ", formats), "Reference formats", null, null, null, formatsJson, copiedAt);
        }

        private static async Task<ClipboardClip?> TryCreateStorageItemClipAsync(DataPackageView view, string formatsJson, DateTimeOffset copiedAt)
        {
            if (!view.Contains(StandardDataFormats.StorageItems))
            {
                return null;
            }

            var items = await view.GetStorageItemsAsync();
            var paths = items
                .Select(item => item is StorageFile file ? file.Path : item.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();

            if (paths.Length == 0)
            {
                return null;
            }

            var firstPath = paths[0];
            var kind = IsVideo(firstPath) ? ClipKind.Video : ClipKind.File;
            var title = paths.Length == 1 ? Path.GetFileName(firstPath) : $"{paths.Length} files";
            return BuildClip(kind, title, string.Join(Environment.NewLine, paths), paths.Length == 1 ? "File reference" : "File references", null, null, string.Join(Environment.NewLine, paths), formatsJson, copiedAt);
        }

        private static async Task<byte[]?> ReadBitmapAsync(DataPackageView view)
        {
            var reference = await view.GetBitmapAsync();
            await using var stream = (await reference.OpenReadAsync()).AsStreamForRead();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return memory.ToArray();
        }

        private static ClipboardClip BuildClip(
            ClipKind kind,
            string title,
            string preview,
            string formatLabel,
            string? contentText,
            byte[]? contentBytes,
            string? referencePath,
            string formatsJson,
            DateTimeOffset copiedAt)
        {
            var hashInput = $"{kind}|{contentText}|{referencePath}|{preview}|{Convert.ToBase64String(SHA256.HashData(contentBytes ?? []))}";
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
            return new ClipboardClip
            {
                Id = id,
                Kind = kind,
                Title = title,
                Preview = preview,
                ContentText = contentText,
                ContentBytes = contentBytes,
                ReferencePath = referencePath,
                FormatsJson = formatsJson,
                FormatLabel = formatLabel,
                CopiedAt = copiedAt
            };
        }

        private static ClipKind DetectTextKind(string text)
        {
            if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out _))
            {
                return ClipKind.Url;
            }

            var trimmed = text.TrimStart();
            if (trimmed.StartsWith("using ", StringComparison.Ordinal) ||
                trimmed.StartsWith("const ", StringComparison.Ordinal) ||
                trimmed.StartsWith("function ", StringComparison.Ordinal) ||
                trimmed.Contains("=>", StringComparison.Ordinal) ||
                trimmed.Contains("{", StringComparison.Ordinal) && trimmed.Contains(";", StringComparison.Ordinal))
            {
                return ClipKind.Code;
            }

            return ClipKind.Text;
        }

        private static string TitleFromText(string text, ClipKind kind)
        {
            var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return kind == ClipKind.Url ? "URL" : "Text";
            }

            return firstLine.Length <= 80 ? firstLine : $"{firstLine[..77]}...";
        }

        private static string PreviewText(string text)
        {
            var compact = string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));
            return compact.Length <= 240 ? compact : $"{compact[..237]}...";
        }

        private static string LabelForText(ClipKind kind, string text) =>
            kind switch
            {
                ClipKind.Url => "URL",
                ClipKind.Code => $"Code - {text.Split('\n').Length} lines",
                _ => "Plain text"
            };

        private static bool IsVideo(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension is ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" or ".wmv";
        }
    }
}

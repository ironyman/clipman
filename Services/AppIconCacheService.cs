using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Clipman.Services;

public sealed class AppIconCacheService
{
    private readonly string _iconDirectory;
    private const uint WmGetIcon = 0x007F;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int IconSmall2 = 2;
    private const int GclpHicon = -14;
    private const int GclpHiconsm = -34;

    public AppIconCacheService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clipman");
        _iconDirectory = Path.Combine(root, "AppIcons");
        Directory.CreateDirectory(_iconDirectory);
    }

    public string GetIconPath(string iconKey)
    {
        var pngPath = Path.Combine(_iconDirectory, $"{iconKey}.png");
        if (File.Exists(pngPath))
        {
            return pngPath;
        }

        var icoPath = Path.Combine(_iconDirectory, $"{iconKey}.ico");
        return File.Exists(icoPath) ? icoPath : pngPath;
    }

    public async Task<string?> TryCacheIconByNameAsync(string? appName, string? processName, string? executablePath, IntPtr windowHandle)
    {
        var keySource = !string.IsNullOrWhiteSpace(processName) ? processName : appName;
        var key = NormalizeIconKey(keySource);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var destinationPngPath = Path.Combine(_iconDirectory, $"{key}.png");
        var destinationIcoPath = Path.Combine(_iconDirectory, $"{key}.ico");
        if (File.Exists(destinationPngPath) || File.Exists(destinationIcoPath))
        {
            return key;
        }

        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            try
            {
                var sourceFile = await StorageFile.GetFileFromPathAsync(executablePath);
                using var thumbnail = await sourceFile.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    48,
                    ThumbnailOptions.UseCurrentScale);
                if (thumbnail is not null)
                {
                    await using var sourceStream = thumbnail.AsStreamForRead();
                    await using var destinationStream = File.Create(destinationPngPath);
                    await sourceStream.CopyToAsync(destinationStream);
                    await destinationStream.FlushAsync();
                    if (File.Exists(destinationPngPath))
                    {
                        return key;
                    }
                }
            }
            catch
            {
            }
        }

        if (windowHandle != IntPtr.Zero && TryCacheIconFromWindow(windowHandle, destinationIcoPath))
        {
            return key;
        }

        return null;
    }

    public static string? NormalizeIconKey(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var chars = input
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var normalized = new string(chars).Trim('_');
        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        return normalized.Length == 0 ? null : normalized;
    }

    private static bool TryCacheIconFromWindow(IntPtr windowHandle, string destinationIcoPath)
    {
        try
        {
            var iconHandle = GetWindowIconHandle(windowHandle);
            if (iconHandle == IntPtr.Zero)
            {
                return false;
            }

            var copiedIconHandle = CopyIcon(iconHandle);
            if (copiedIconHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                using var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(copiedIconHandle).Clone();
                using var destinationStream = File.Create(destinationIcoPath);
                icon.Save(destinationStream);
                return File.Exists(destinationIcoPath);
            }
            finally
            {
                _ = DestroyIcon(copiedIconHandle);
            }
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr GetWindowIconHandle(IntPtr windowHandle)
    {
        var iconHandle = SendMessage(windowHandle, WmGetIcon, new IntPtr(IconSmall2), IntPtr.Zero);
        if (iconHandle == IntPtr.Zero)
        {
            iconHandle = SendMessage(windowHandle, WmGetIcon, new IntPtr(IconSmall), IntPtr.Zero);
        }

        if (iconHandle == IntPtr.Zero)
        {
            iconHandle = SendMessage(windowHandle, WmGetIcon, new IntPtr(IconBig), IntPtr.Zero);
        }

        if (iconHandle == IntPtr.Zero)
        {
            iconHandle = GetClassLongPtr(windowHandle, GclpHiconsm);
        }

        if (iconHandle == IntPtr.Zero)
        {
            iconHandle = GetClassLongPtr(windowHandle, GclpHicon);
        }

        return iconHandle;
    }

    private static IntPtr GetClassLongPtr(IntPtr hWnd, int index) =>
        IntPtr.Size == 8
            ? GetClassLongPtr64(hWnd, index)
            : new IntPtr(unchecked((int)GetClassLong32(hWnd, index)));

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassLong")]
    private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
    private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

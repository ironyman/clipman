using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Clipman.Services;

public sealed class AppIconCacheService
{
    private readonly string _iconDirectory;

    public AppIconCacheService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clipman");
        _iconDirectory = Path.Combine(root, "AppIcons");
        Directory.CreateDirectory(_iconDirectory);
    }

    public string GetIconPath(string iconKey) => Path.Combine(_iconDirectory, $"{iconKey}.png");

    public async Task<string?> TryCacheIconByNameAsync(string? appName, string? processName, string? executablePath)
    {
        var keySource = !string.IsNullOrWhiteSpace(processName) ? processName : appName;
        var key = NormalizeIconKey(keySource);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var destinationPath = GetIconPath(key);
        if (File.Exists(destinationPath))
        {
            return key;
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            var sourceFile = await StorageFile.GetFileFromPathAsync(executablePath);
            using var thumbnail = await sourceFile.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                48,
                ThumbnailOptions.UseCurrentScale);
            if (thumbnail is null)
            {
                return null;
            }

            await using var sourceStream = thumbnail.AsStreamForRead();
            await using var destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream);
            await destinationStream.FlushAsync();
            return File.Exists(destinationPath) ? key : null;
        }
        catch
        {
            return null;
        }
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
}

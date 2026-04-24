namespace Clipman.Models;

public sealed class ClipboardClip
{
    public required string Id { get; init; }
    public required ClipKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Preview { get; init; }
    public string? ContentText { get; init; }
    public byte[]? ContentBytes { get; init; }
    public string? ReferencePath { get; init; }
    public string? FormatsJson { get; init; }
    public string? SourceApp { get; init; }
    public string? AppIconKey { get; init; }
    public string? SourceWindowTitle { get; init; }
    public string? BrowserTabTitle { get; init; }
    public string? SourceUrl { get; init; }
    public string? SourceDomain { get; init; }
    public string? Tags { get; init; }
    public string? FormatLabel { get; init; }
    public DateTimeOffset CopiedAt { get; init; }
    public bool IsPinned { get; init; }
    public int UseCount { get; init; }
    public string? AccentHex { get; init; }

    public string RelativeTime
    {
        get
        {
            var elapsed = DateTimeOffset.Now - CopiedAt;
            if (elapsed.TotalMinutes < 1) return "Just now";
            if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes} min";
            if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours} hr";
            return $"{(int)elapsed.TotalDays} d";
        }
    }

    public string DisplayContent => !string.IsNullOrWhiteSpace(ContentText) ? ContentText : Preview;

    public string Metadata =>
        string.Join(" - ", new[] { FormatLabel, SourceApp, SourceDomain, RelativeTime }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

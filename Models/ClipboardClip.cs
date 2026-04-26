namespace Clipman.Models;

public sealed class ClipboardClip
{
    private const int MaxListTitleChars = 180;
    private const int MaxListPreviewChars = 1200;
    private const int MaxSelectedPreviewChars = 12000;
    private string? _displayTitle;
    private string? _displayPreview;
    private string? _displayContent;

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

    public string DisplayTitle => _displayTitle ??= SanitizeAndClampForDisplay(Title, MaxListTitleChars, collapseLines: true);

    public string DisplayPreview => _displayPreview ??= SanitizeAndClampForDisplay(Preview, MaxListPreviewChars, collapseLines: true);

    public string DisplayContent => _displayContent ??= SanitizeAndClampForDisplay(
        !string.IsNullOrWhiteSpace(ContentText) ? ContentText : Preview,
        MaxSelectedPreviewChars,
        collapseLines: false);

    public string Metadata =>
        string.Join(" - ", new[] { FormatLabel, SourceApp, SourceDomain, RelativeTime }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string SanitizeForDisplay(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // XAML text controls can render poorly with embedded control characters.
        Span<char> buffer = text.Length <= 4096 ? stackalloc char[text.Length] : new char[text.Length];
        var written = 0;

        foreach (var ch in text)
        {
            if (ch == '\r' || ch == '\n' || ch == '\t' || !char.IsControl(ch))
            {
                buffer[written++] = ch;
            }
        }

        return written == text.Length ? text : new string(buffer[..written]);
    }

    private static string SanitizeAndClampForDisplay(string? text, int maxChars, bool collapseLines)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sanitized = SanitizeForDisplay(text);
        if (collapseLines)
        {
            sanitized = sanitized
                .Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' ');
        }

        if (sanitized.Length <= maxChars)
        {
            return sanitized;
        }

        return $"{sanitized[..Math.Max(1, maxChars - 3)]}...";
    }
}

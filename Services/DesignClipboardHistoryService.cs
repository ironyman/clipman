using Clipman.Models;

namespace Clipman.Services;

public sealed class DesignClipboardHistoryService : IClipboardHistoryService
{
    public event EventHandler<ClipboardClip>? ClipAdded;

    public Task<IReadOnlyList<ClipboardClip>> GetPageAsync(
        int skip,
        int take,
        string? query = null,
        ClipKind? kind = null,
        bool pinnedOnly = false,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ClipboardClip> clips =
        [
            new()
            {
                Id = "clip-001",
                Kind = ClipKind.Text,
                Title = "Meeting notes - Q4 planning",
                Preview = "Action items: finalize budget, review launch metrics, and send the revised timeline to design.",
                SourceApp = "Teams",
                FormatLabel = "Plain text",
                CopiedAt = DateTimeOffset.Now.AddSeconds(-20),
                IsPinned = true,
                UseCount = 18,
                AccentHex = "#7C3AED"
            },
            new()
            {
                Id = "clip-002",
                Kind = ClipKind.Code,
                Title = "Submit handler",
                Preview = "const handleSubmit = async (data) => {\n  await saveClipboardRule(data);\n};",
                SourceApp = "Visual Studio Code",
                FormatLabel = "JavaScript - 3 lines",
                CopiedAt = DateTimeOffset.Now.AddMinutes(-3),
                UseCount = 7,
                AccentHex = "#0F766E"
            },
            new()
            {
                Id = "clip-003",
                Kind = ClipKind.Url,
                Title = "API documentation",
                Preview = "https://docs.example.com/api/v2/clipboard/history",
                SourceApp = "Edge",
                FormatLabel = "URL",
                CopiedAt = DateTimeOffset.Now.AddMinutes(-8),
                UseCount = 3,
                AccentHex = "#2563EB"
            },
            new()
            {
                Id = "clip-004",
                Kind = ClipKind.Image,
                Title = "Screenshot - dashboard.png",
                Preview = "Image thumbnail preview - 1920 x 1080",
                SourceApp = "Snipping Tool",
                FormatLabel = "PNG image",
                CopiedAt = DateTimeOffset.Now.AddMinutes(-18),
                IsPinned = true,
                UseCount = 11,
                AccentHex = "#DB2777"
            },
            new()
            {
                Id = "clip-005",
                Kind = ClipKind.Html,
                Title = "Revenue table",
                Preview = "HTML table with 24 rows and formatting retained for rich paste targets.",
                SourceApp = "Excel",
                FormatLabel = "HTML",
                CopiedAt = DateTimeOffset.Now.AddHours(-1),
                UseCount = 5,
                AccentHex = "#D97706"
            },
            new()
            {
                Id = "clip-006",
                Kind = ClipKind.File,
                Title = "Project brief.pdf",
                Preview = "C:\\Users\\admin\\Documents\\Project brief.pdf",
                SourceApp = "File Explorer",
                FormatLabel = "File path",
                CopiedAt = DateTimeOffset.Now.AddHours(-4),
                UseCount = 2,
                AccentHex = "#4B5563"
            }
        ];

        var filtered = clips.Where(clip =>
            (!pinnedOnly || clip.IsPinned) &&
            (kind is null || clip.Kind == kind) &&
            (string.IsNullOrWhiteSpace(query) ||
             clip.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             clip.Preview.Contains(query, StringComparison.OrdinalIgnoreCase)));

        return Task.FromResult<IReadOnlyList<ClipboardClip>>(filtered.Skip(skip).Take(take).ToList());
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(6);

    public Task CaptureCurrentClipboardAsync(CancellationToken cancellationToken = default)
    {
        ClipAdded?.Invoke(this, new ClipboardClip
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = ClipKind.Text,
            Title = "Current clipboard",
            Preview = "Captured from the clipboard listener.",
            FormatLabel = "Design capture",
            CopiedAt = DateTimeOffset.Now
        });

        return Task.CompletedTask;
    }
}

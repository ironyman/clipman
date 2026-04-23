using Clipman.Models;

namespace Clipman.Services;

public interface IClipboardHistoryService
{
    event EventHandler<ClipboardClip>? ClipAdded;

    Task<IReadOnlyList<ClipboardClip>> GetPageAsync(
        int skip,
        int take,
        string? query = null,
        ClipKind? kind = null,
        bool pinnedOnly = false,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task CaptureCurrentClipboardAsync(CancellationToken cancellationToken = default);
}

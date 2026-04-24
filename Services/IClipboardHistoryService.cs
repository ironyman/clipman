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

    Task CaptureAndStoreAsync(CancellationToken cancellationToken = default);

    Task SetPinnedAsync(string id, bool isPinned, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task UpdateTextAsync(string id, string title, string preview, string contentText, CancellationToken cancellationToken = default);
}

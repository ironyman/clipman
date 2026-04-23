using Clipman.Models;

namespace Clipman.Services;

public interface IClipboardClipRepository
{
    Task<IReadOnlyList<ClipboardClip>> GetPageAsync(
        int skip,
        int take,
        string? query = null,
        ClipKind? kind = null,
        bool pinnedOnly = false,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);

    Task AddAsync(ClipboardClip clip, CancellationToken cancellationToken = default);

    Task SetPinnedAsync(string id, bool isPinned, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

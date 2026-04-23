using Clipman.Models;

namespace Clipman.Services;

public interface IClipboardHistoryService
{
    Task<IReadOnlyList<ClipboardClip>> GetRecentAsync(CancellationToken cancellationToken = default);
}

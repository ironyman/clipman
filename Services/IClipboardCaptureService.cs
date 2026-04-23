using Clipman.Models;

namespace Clipman.Services;

public interface IClipboardCaptureService
{
    Task<ClipboardClip?> CaptureAsync(CancellationToken cancellationToken = default);
}

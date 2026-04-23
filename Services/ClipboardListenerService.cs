using Windows.ApplicationModel.DataTransfer;

namespace Clipman.Services;

public sealed class ClipboardListenerService : IDisposable
{
    private readonly IClipboardHistoryService _historyService;
    private bool _disposed;

    public ClipboardListenerService(IClipboardHistoryService historyService)
    {
        _historyService = historyService;
    }

    public void Start()
    {
        Clipboard.ContentChanged += Clipboard_ContentChanged;
    }

    public void Stop()
    {
        Clipboard.ContentChanged -= Clipboard_ContentChanged;
    }

    public async Task CaptureNowAsync(CancellationToken cancellationToken = default)
    {
        await _historyService.CaptureAndStoreAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private async void Clipboard_ContentChanged(object? sender, object e)
    {
        await _historyService.CaptureAndStoreAsync();
    }
}

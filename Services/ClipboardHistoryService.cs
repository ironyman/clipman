using Clipman.Models;

namespace Clipman.Services;

public sealed class ClipboardHistoryService : IClipboardHistoryService
{
    private readonly IClipboardClipRepository _repository;
    private readonly IClipboardCaptureService _captureService;

    public ClipboardHistoryService(IClipboardClipRepository repository, IClipboardCaptureService captureService)
    {
        _repository = repository;
        _captureService = captureService;
    }

    public event EventHandler<ClipboardClip>? ClipAdded;

    public Task<IReadOnlyList<ClipboardClip>> GetPageAsync(
        int skip,
        int take,
        string? query = null,
        ClipKind? kind = null,
        bool pinnedOnly = false,
        CancellationToken cancellationToken = default) =>
        _repository.GetPageAsync(skip, take, query, kind, pinnedOnly, cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _repository.CountAsync(cancellationToken);

    public async Task CaptureAndStoreAsync(CancellationToken cancellationToken = default)
    {
        var clip = await _captureService.CaptureAsync(cancellationToken);
        if (clip is null)
        {
            return;
        }

        if (await _repository.ExistsAsync(clip.Id, cancellationToken))
        {
            return;
        }

        await _repository.AddAsync(clip, cancellationToken);
        ClipAdded?.Invoke(this, clip);
    }

    public Task SetPinnedAsync(string id, bool isPinned, CancellationToken cancellationToken = default) =>
        _repository.SetPinnedAsync(id, isPinned, cancellationToken);

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        _repository.DeleteAsync(id, cancellationToken);

    public Task UpdateTextAsync(string id, string title, string preview, string contentText, CancellationToken cancellationToken = default) =>
        _repository.UpdateTextAsync(id, title, preview, contentText, cancellationToken);

    public Task UpdateTagsAsync(string id, string? tags, CancellationToken cancellationToken = default) =>
        _repository.UpdateTagsAsync(id, tags, cancellationToken);
}

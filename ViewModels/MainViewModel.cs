using System.Collections.ObjectModel;
using System.Windows.Input;
using Clipman.Models;
using Clipman.Services;

namespace Clipman.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int PageSize = 50;
    private readonly IClipboardHistoryService _clipboardHistoryService;
    private ClipboardClip? _selectedClip;
    private string _searchQuery = string.Empty;
    private ClipKind? _selectedKind;
    private bool _showPinnedOnly;
    private int _loadedCount;
    private int _savedCount;
    private int _pinnedCount;
    private bool _isLoading;
    private bool _hasMore = true;
    private CancellationTokenSource? _searchDebounceCts;
    private bool _suppressRealtimeClipInsert;

    public MainViewModel(IClipboardHistoryService clipboardHistoryService)
    {
        _clipboardHistoryService = clipboardHistoryService;
        _clipboardHistoryService.ClipAdded += ClipboardHistoryService_ClipAdded;
        SelectKindCommand = new RelayCommand(parameter =>
        {
            SelectedKind = parameter is ClipKind kind ? kind : null;
        });
    }

    public ObservableCollection<ClipboardClip> VisibleClips { get; } = [];

    public IReadOnlyList<ClipKind> Kinds { get; } =
    [
        ClipKind.Text,
        ClipKind.Code,
        ClipKind.Url,
        ClipKind.Image,
        ClipKind.Video,
        ClipKind.Html,
        ClipKind.File,
        ClipKind.Other
    ];

    public ICommand SelectKindCommand { get; }

    public ClipboardClip? SelectedClip
    {
        get => _selectedClip;
        set => SetProperty(ref _selectedClip, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                _ = DebounceSearchRefreshAsync();
            }
        }
    }

    public ClipKind? SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (SetProperty(ref _selectedKind, value))
            {
                OnPropertyChanged(nameof(FilterLabel));
                _ = RefreshAsync();
            }
        }
    }

    public bool ShowPinnedOnly
    {
        get => _showPinnedOnly;
        set
        {
            if (SetProperty(ref _showPinnedOnly, value))
            {
                OnPropertyChanged(nameof(FilterLabel));
                _ = RefreshAsync();
            }
        }
    }

    public string FilterLabel => ShowPinnedOnly ? "Pinned clips" : SelectedKind?.ToString() ?? string.Empty;

    public int SavedCount
    {
        get => _savedCount;
        private set => SetProperty(ref _savedCount, value);
    }

    public int PinnedCount
    {
        get => _pinnedCount;
        private set => SetProperty(ref _pinnedCount, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasMore
    {
        get => _hasMore;
        private set => SetProperty(ref _hasMore, value);
    }

    public async Task LoadAsync()
    {
        SavedCount = await _clipboardHistoryService.CountAsync();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            _loadedCount = 0;
            HasMore = true;
            VisibleClips.Clear();
            SelectedClip = null;
            await LoadPageAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadMoreAsync()
    {
        if (IsLoading)
        {
            return;
        }

        if (!HasMore)
        {
            return;
        }

        IsLoading = true;
        try
        {
            await LoadPageAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SetLiveInsertSuppressed(bool suppressed)
    {
        _suppressRealtimeClipInsert = suppressed;
    }

    private async Task LoadPageAsync()
    {
        var sourcePage = await _clipboardHistoryService.GetPageAsync(
            _loadedCount,
            PageSize,
            SearchQuery,
            SelectedKind,
            ShowPinnedOnly);
        var isSearching = !string.IsNullOrWhiteSpace(SearchQuery);
        var page = sourcePage.ToList();

        foreach (var clip in page)
        {
            VisibleClips.Add(clip);
        }

        _loadedCount += sourcePage.Count;
        HasMore = sourcePage.Count >= PageSize;
        ReorderVisibleClips();
        if (_loadedCount <= sourcePage.Count || isSearching)
        {
            SelectedClip = VisibleClips.FirstOrDefault();
        }
        else
        {
            SelectedClip ??= VisibleClips.FirstOrDefault();
        }
        PinnedCount = VisibleClips.Count(clip => clip.IsPinned);
    }

    private async Task DebounceSearchRefreshAsync()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;
        try
        {
            await Task.Delay(260, token);
            await RefreshAsync();
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void ClipboardHistoryService_ClipAdded(object? sender, ClipboardClip clip)
    {
        SavedCount++;

        if (_suppressRealtimeClipInsert)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        if ((!ShowPinnedOnly || clip.IsPinned) && (SelectedKind is null || clip.Kind == SelectedKind))
        {
            VisibleClips.Insert(0, clip);
            _loadedCount++;
            ReorderVisibleClips();
            SelectedClip = VisibleClips.FirstOrDefault(item => item.Id == clip.Id) ?? clip;
            PinnedCount = VisibleClips.Count(item => item.IsPinned);
        }
    }

    private void ReorderVisibleClips()
    {
        var selectedId = SelectedClip?.Id;
        var ordered = VisibleClips
            .OrderByDescending(clip => clip.IsPinned)
            .ThenByDescending(clip => clip.CopiedAt)
            .ToList();

        VisibleClips.Clear();
        foreach (var clip in ordered)
        {
            VisibleClips.Add(clip);
        }

        if (selectedId is not null)
        {
            SelectedClip = VisibleClips.FirstOrDefault(clip => clip.Id == selectedId) ?? VisibleClips.FirstOrDefault();
        }
    }
}

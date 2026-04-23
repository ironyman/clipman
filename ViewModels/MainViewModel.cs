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

    public MainViewModel(IClipboardHistoryService clipboardHistoryService)
    {
        _clipboardHistoryService = clipboardHistoryService;
        _clipboardHistoryService.ClipAdded += ClipboardHistoryService_ClipAdded;
        SelectKindCommand = new RelayCommand(parameter =>
        {
            SelectedKind = parameter is ClipKind kind ? kind : null;
        });
        LoadMoreCommand = new RelayCommand(async _ => await LoadMoreAsync(), _ => !IsLoading);
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

    public ICommand LoadMoreCommand { get; }

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
                _ = RefreshAsync();
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

    public string FilterLabel => ShowPinnedOnly ? "Pinned clips" : SelectedKind?.ToString() ?? "All clips";

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
        private set
        {
            if (SetProperty(ref _isLoading, value) && LoadMoreCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
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
            VisibleClips.Clear();
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

    private async Task LoadPageAsync()
    {
        var page = await _clipboardHistoryService.GetPageAsync(
            _loadedCount,
            PageSize,
            SearchQuery,
            SelectedKind,
            ShowPinnedOnly);

        foreach (var clip in page)
        {
            VisibleClips.Add(clip);
        }

        _loadedCount += page.Count;
        SelectedClip ??= VisibleClips.FirstOrDefault();
        PinnedCount = VisibleClips.Count(clip => clip.IsPinned);
    }

    private void ClipboardHistoryService_ClipAdded(object? sender, ClipboardClip clip)
    {
        SavedCount++;
        if ((!ShowPinnedOnly || clip.IsPinned) && (SelectedKind is null || clip.Kind == SelectedKind))
        {
            VisibleClips.Insert(0, clip);
            SelectedClip = clip;
            _loadedCount++;
        }
    }
}

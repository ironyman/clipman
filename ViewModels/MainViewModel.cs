using System.Collections.ObjectModel;
using System.Windows.Input;
using Clipman.Models;
using Clipman.Services;

namespace Clipman.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IClipboardHistoryService _clipboardHistoryService;
    private readonly List<ClipboardClip> _allClips = [];
    private ClipboardClip? _selectedClip;
    private string _searchQuery = string.Empty;
    private ClipKind? _selectedKind;
    private bool _showPinnedOnly;

    public MainViewModel(IClipboardHistoryService clipboardHistoryService)
    {
        _clipboardHistoryService = clipboardHistoryService;
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
        ClipKind.Html,
        ClipKind.File
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
                ApplyFilters();
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
                ApplyFilters();
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
                ApplyFilters();
            }
        }
    }

    public string FilterLabel => ShowPinnedOnly ? "Pinned clips" : SelectedKind?.ToString() ?? "All clips";

    public int SavedCount => _allClips.Count;

    public int PinnedCount => _allClips.Count(clip => clip.IsPinned);

    public async Task LoadAsync()
    {
        var clips = await _clipboardHistoryService.GetRecentAsync();
        _allClips.Clear();
        _allClips.AddRange(clips.OrderByDescending(clip => clip.IsPinned).ThenByDescending(clip => clip.CopiedAt));

        OnPropertyChanged(nameof(SavedCount));
        OnPropertyChanged(nameof(PinnedCount));
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var query = SearchQuery.Trim();
        var filtered = _allClips.Where(clip =>
            (!ShowPinnedOnly || clip.IsPinned) &&
            (SelectedKind is null || clip.Kind == SelectedKind) &&
            (query.Length == 0 ||
             clip.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             clip.Preview.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             (clip.SourceApp?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)));

        VisibleClips.Clear();
        foreach (var clip in filtered)
        {
            VisibleClips.Add(clip);
        }

        SelectedClip = VisibleClips.FirstOrDefault();
    }
}

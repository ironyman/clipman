using Clipman.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Clipman;

public sealed partial class SearchPopupWindow : Window
{
    public SearchPopupWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        Root.DataContext = viewModel;
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(620, 560));
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        Activated += SearchPopupWindow_Activated;
    }

    private void SearchPopupWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        SearchBox.Focus(FocusState.Programmatic);
    }
}

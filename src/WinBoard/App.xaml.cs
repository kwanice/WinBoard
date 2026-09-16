using Microsoft.UI.Xaml;
using WinBoard.UI;

namespace WinBoard;

/// <summary>
/// WinBoard entry point. The keyboard window is shown without activation so
/// the currently focused app keeps input focus for SendInput.
/// </summary>
public partial class App : Application
{
    private KeyboardWindow? _window;

    public App()
    {
        InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new KeyboardWindow();
        _window.ShowWithoutActivating();
    }
}

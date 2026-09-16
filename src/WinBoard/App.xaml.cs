using Microsoft.UI.Xaml;
using WinBoard.Input;
using WinBoard.Services;
using WinBoard.UI;

namespace WinBoard;

/// <summary>
/// WinBoard entry point. The keyboard window is shown without activation so
/// the currently focused app keeps input focus for SendInput. A Win32 tray
/// icon (Shell_NotifyIcon) stays in the notification area for show/hide/quit.
/// </summary>
public partial class App : Application
{
    private KeyboardWindow? _window;
    private TrayIcon? _tray;

    public App()
    {
        KeyboardSettings settings = SettingsService.Shared.Current;
        RequestedTheme = settings.Theme == "Light" ? ApplicationTheme.Light : ApplicationTheme.Dark;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new KeyboardWindow();
        _window.Closed += OnKeyboardClosed;
        _tray = new TrayIcon(_window.DispatcherQueue);
        _tray.ToggleRequested += () => _window?.ToggleFromTray();
        _tray.SettingsRequested += () => _window?.OpenSettingsFromTray();
        _tray.QuitRequested += () => _window?.RequestQuit();
        _window.ShowWithoutActivating();
    }

    private void OnKeyboardClosed(object sender, WindowEventArgs args)
    {
        _tray?.Dispose();
        _tray = null;
    }
}

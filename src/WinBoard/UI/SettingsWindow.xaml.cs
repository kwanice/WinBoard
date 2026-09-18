using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinBoard.Input;
using WinBoard.Layouts;
using WinBoard.Services;

namespace WinBoard.UI;

/// <summary>
/// Standalone settings window (may take focus). The keyboard stays visible
/// beside it and applies changes live via <see cref="SettingsService.Changed"/>.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private const int WidthDip = 520;
    private const int HeightDip = 780;

    private static SettingsWindow? _open;

    private readonly SettingsService _settings;
    private bool _suppress = true;
    private bool _ready;
    private Window? _beside;

    private SettingsWindow(SettingsService settings, Window? beside)
    {
        _settings = settings;
        _beside = beside;
        InitializeComponent();
        Title = "Réglages — WinBoard";
        VersionText.Text = AppInfo.DisplayVersion;
        SystemBackdrop = new DesktopAcrylicBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: true);
        }

        RootGrid.RequestedTheme = _settings.Current.Theme == "Light" ? ElementTheme.Light : ElementTheme.Dark;
        _settings.Changed += OnSettingsChanged;
        Closed += OnClosed;
        RootGrid.Loaded += OnRootLoaded;
        LoadIntoUi();
        ResizeClient();
    }

    public static void Show(SettingsService settings, Window? beside)
    {
        if (_open is not null)
        {
            _open.LoadIntoUi();
            _open.AppWindow.Show(activateWindow: true);
            _open.Activate();
            if (beside is KeyboardWindow keyboard)
            {
                keyboard.KeepTopmost();
            }

            return;
        }

        _open = new SettingsWindow(settings, beside);
        _open.PlaceBeside(beside);
        _open.Activate();
        if (beside is KeyboardWindow keyboard)
        {
            keyboard.KeepTopmost();
        }
    }

    private KeyboardSettings Settings => _settings.Current;

    private void ResizeClient()
    {
        nint hwnd = NoActivateWindow.GetHwnd(this);
        uint dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        int w = (int)Math.Round(WidthDip * dpi / 96.0);
        int h = (int)Math.Round(HeightDip * dpi / 96.0);
        PointInt32 pos = AppWindow.Position;
        AppWindow.MoveAndResize(new RectInt32(pos.X, pos.Y, w, h));
    }

    private void PlaceBeside(Window? beside)
    {
        DisplayArea display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 work = display.WorkArea;
        int w = AppWindow.Size.Width;
        int h = AppWindow.Size.Height;
        int x = work.X + 48;
        int y = work.Y + 48;

        if (beside is not null)
        {
            PointInt32 kp = beside.AppWindow.Position;
            SizeInt32 ks = beside.AppWindow.Size;
            int right = kp.X + ks.Width + 16;
            int left = kp.X - w - 16;
            x = right + w <= work.X + work.Width ? right : Math.Max(work.X + 16, left);
            y = Math.Clamp(kp.Y, work.Y + 16, Math.Max(work.Y + 16, work.Y + work.Height - h - 16));
        }

        AppWindow.Move(new PointInt32(x, y));
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        LoadIntoUi();
        _ready = true;
        // Init ValueChanged/Toggled can be queued until after Loaded. Keep
        // suppress until those have flushed so XAML defaults cannot clobber
        // the file we just loaded.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _suppress = false);
    }

    private void LoadIntoUi()
    {
        _suppress = true;
        RootGrid.RequestedTheme = Settings.Theme == "Light" ? ElementTheme.Light : ElementTheme.Dark;
        LayoutChoice.SelectedIndex = Settings.LayoutId == LayoutCatalog.QwertyId ? 1 : 0;
        ThemeChoice.SelectedIndex = Settings.Theme == "Light" ? 1 : 0;
        SwipeToggle.IsOn = Settings.SwipeEnabled;
        SwipeTrailToggle.IsOn = Settings.ShowSwipeTrail;
        NumberRowToggle.IsOn = Settings.ShowNumberRow;
        SecondaryGlyphToggle.IsOn = Settings.ShowSecondaryGlyphs;
        LongPressToggle.IsOn = Settings.LongPressEnabled;
        KeyRepeatToggle.IsOn = Settings.KeyRepeatEnabled;
        RepeatDelaySlider.Value = Settings.KeyRepeatInitialDelayMs;
        RepeatIntervalSlider.Value = Settings.KeyRepeatIntervalMs;
        OpacitySlider.Value = Settings.Opacity;
        SizeSlider.Value = Settings.SizeScale;
        FontSlider.Value = Settings.LetterFontScale;
        OutlinesToggle.IsOn = Settings.ShowKeyOutlines;
        _suppress = !_ready;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        RootGrid.RequestedTheme = Settings.Theme == "Light" ? ElementTheme.Light : ElementTheme.Dark;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnQuitClicked(object sender, RoutedEventArgs e) => Application.Current.Exit();

    private static void OnClosed(object sender, WindowEventArgs args)
    {
        if (sender is SettingsWindow window)
        {
            window._settings.Changed -= window.OnSettingsChanged;
            if (window._beside is KeyboardWindow keyboard)
            {
                keyboard.RestoreAfterSettings();
            }
        }

        _open = null;
    }

    private bool ShouldWrite() => !_suppress && _ready;

    private void OnLayoutChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ShouldWrite() || LayoutChoice.SelectedIndex < 0)
        {
            return;
        }

        string id = LayoutChoice.SelectedIndex == 1 ? LayoutCatalog.QwertyId : LayoutCatalog.AzertyId;
        if (Settings.LayoutId != id)
        {
            _settings.Update(s => s.LayoutId = id);
        }
    }

    private void OnThemeChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ShouldWrite() || ThemeChoice.SelectedIndex < 0)
        {
            return;
        }

        string theme = ThemeChoice.SelectedIndex == 1 ? "Light" : "Dark";
        if (Settings.Theme != theme)
        {
            _settings.Update(s => s.Theme = theme);
        }
    }

    private void OnSwipeToggled(object sender, RoutedEventArgs e)
    {
        if (ShouldWrite() && Settings.SwipeEnabled != SwipeToggle.IsOn)
        {
            _settings.Update(s => s.SwipeEnabled = SwipeToggle.IsOn);
        }
    }

    private void OnSwipeTrailToggled(object sender, RoutedEventArgs e)
    {
        if (ShouldWrite() && Settings.ShowSwipeTrail != SwipeTrailToggle.IsOn)
        {
            _settings.Update(s => s.ShowSwipeTrail = SwipeTrailToggle.IsOn);
        }
    }

    private void OnDiagnosticClicked(object sender, RoutedEventArgs e)
    {
        if (_beside is KeyboardWindow keyboard)
        {
            keyboard.OpenSwipeDiagnostic();
        }
    }

    private void OnNumberRowToggled(object sender, RoutedEventArgs e)
    {
        if (ShouldWrite() && Settings.ShowNumberRow != NumberRowToggle.IsOn)
        {
            _settings.Update(s => s.ShowNumberRow = NumberRowToggle.IsOn);
        }
    }

    private void OnSecondaryGlyphToggled(object sender, RoutedEventArgs e)
    {
        if (ShouldWrite() && Settings.ShowSecondaryGlyphs != SecondaryGlyphToggle.IsOn)
        {
            _settings.Update(s => s.ShowSecondaryGlyphs = SecondaryGlyphToggle.IsOn);
        }
    }

    private void OnLongPressToggled(object sender, RoutedEventArgs e)
    {
        if (ShouldWrite() && Settings.LongPressEnabled != LongPressToggle.IsOn)
        {
            _settings.Update(s => s.LongPressEnabled = LongPressToggle.IsOn);
        }
    }

    private void OnKeyRepeatToggled(object sender, RoutedEventArgs e)
    {
        if (ShouldWrite() && Settings.KeyRepeatEnabled != KeyRepeatToggle.IsOn)
        {
            _settings.Update(s => s.KeyRepeatEnabled = KeyRepeatToggle.IsOn);
        }
    }

    private void OnRepeatDelayChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ShouldWrite() && Settings.KeyRepeatInitialDelayMs != (int)e.NewValue)
        {
            _settings.Update(s => s.KeyRepeatInitialDelayMs = (int)e.NewValue);
        }
    }

    private void OnRepeatIntervalChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ShouldWrite() && Settings.KeyRepeatIntervalMs != (int)e.NewValue)
        {
            _settings.Update(s => s.KeyRepeatIntervalMs = (int)e.NewValue);
        }
    }

    private void OnOpacityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ShouldWrite() && Math.Abs(Settings.Opacity - e.NewValue) > 0.0005)
        {
            _settings.Update(s => s.Opacity = e.NewValue);
        }
    }

    private void OnSizeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ShouldWrite() && Math.Abs(Settings.SizeScale - e.NewValue) > 0.0005)
        {
            _settings.Update(s => s.SizeScale = e.NewValue);
        }
    }

    private void OnFontChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ShouldWrite() && Math.Abs(Settings.LetterFontScale - e.NewValue) > 0.0005)
        {
            _settings.Update(s => s.LetterFontScale = e.NewValue);
        }
    }

    private void OnOutlinesToggled(object sender, RoutedEventArgs e)
    {
        if (ShouldWrite() && Settings.ShowKeyOutlines != OutlinesToggle.IsOn)
        {
            _settings.Update(s => s.ShowKeyOutlines = OutlinesToggle.IsOn);
        }
    }
}

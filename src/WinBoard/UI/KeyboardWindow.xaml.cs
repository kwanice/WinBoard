using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using WinBoard.Input;
using WinBoard.Layouts;
using WinBoard.Services;

namespace WinBoard.UI;

public sealed partial class KeyboardWindow : Window
{
    private const double BaseWidthDip = 720;
    private const double BaseKeyHeight = 46;
    private const double TopBarHeight = 34;

    private readonly SettingsService _settingsService = new();
    private readonly LayoutService _layout = new();

    private readonly DispatcherQueueTimer _pressTimer;
    private readonly DispatcherQueueTimer _repeatTimer;

    // Starts true so slider/radio coercion during InitializeComponent does not
    // write back to settings before the UI is populated.
    private bool _suppressSettingsEvents = true;

    // Active press state.
    private Border? _activeBorder;
    private KeyDefinition? _activeKey;
    private uint _activePointerId;
    private PressMode _pressMode;
    private bool _repeatFired;
    private bool _popupShown;
    private bool _spaceHandled;
    private bool _wordDeleted;
    private double _pressStartX;
    private double _lastWordX;

    // Long-press popup state.
    private readonly List<Border> _popupItems = new();
    private IReadOnlyList<string> _popupChars = [];
    private double _popupItemWidth;
    private int _popupIndex;

    // Window drag state.
    private bool _dragging;
    private POINT _dragCursorStart;
    private PointInt32 _dragWindowStart;

    // Cached brushes (rebuilt when the theme changes).
    private Brush _letterBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    private Brush _funcBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
    private Brush _pressedBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
    private Brush _accentBrush = new SolidColorBrush(Colors.SlateBlue);

    private enum PressMode
    {
        None,
        Repeat,
        Popup,
        SpaceLanguage,
    }

    public KeyboardWindow()
    {
        InitializeComponent();

        Title = "WinBoard";
        SystemBackdrop = new DesktopAcrylicBackdrop();
        VersionText.Text = AppInfo.DisplayVersion;

        _pressTimer = DispatcherQueue.CreateTimer();
        _pressTimer.IsRepeating = false;
        _pressTimer.Tick += OnPressTimerTick;

        _repeatTimer = DispatcherQueue.CreateTimer();
        _repeatTimer.IsRepeating = true;
        _repeatTimer.Tick += OnRepeatTimerTick;

        _layout.SetAlphabetic(_settingsService.Current.LayoutId);
        _layout.Changed += (_, _) => RenderKeyboard();
        _settingsService.Changed += (_, _) => OnSettingsChanged();
        Closed += OnClosed;

        ConfigurePresenter();
        NoActivateWindow.Apply(NoActivateWindow.GetHwnd(this));

        ApplyAppearance();
        LoadSettingsIntoUi();
        RenderKeyboard();
        RelayoutWindow();
    }

    /// <summary>Shows the overlay without activation so the target app keeps focus.</summary>
    public void ShowWithoutActivating()
    {
        AppWindow.Show(activateWindow: false);
        NoActivateWindow.Apply(NoActivateWindow.GetHwnd(this));
    }

    private KeyboardSettings Settings => _settingsService.Current;

    private double Scale => Settings.SizeScale;

    // --- Window setup ------------------------------------------------------

    private void ConfigurePresenter()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
    }

    private void ApplyAppearance()
    {
        ElementTheme theme = Settings.Theme == "Light" ? ElementTheme.Light : ElementTheme.Dark;
        RootGrid.RequestedTheme = theme;
        KeyboardRoot.Opacity = Settings.Opacity;

        _letterBrush = ResourceBrush("ControlFillColorDefaultBrush", Color.FromArgb(46, 255, 255, 255));
        _funcBrush = ResourceBrush("ControlFillColorSecondaryBrush", Color.FromArgb(22, 255, 255, 255));
        _pressedBrush = ResourceBrush("ControlFillColorTertiaryBrush", Color.FromArgb(85, 255, 255, 255));
        _accentBrush = ResourceBrush("AccentFillColorDefaultBrush", Colors.SlateBlue);
    }

    private static Brush ResourceBrush(string key, Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }

    private void RelayoutWindow()
    {
        nint hwnd = NoActivateWindow.GetHwnd(this);

        int rowCount = _layout.Current.Rows.Count + (ShouldShowNumberRow() ? 1 : 0);
        double keyHeight = BaseKeyHeight * Scale;
        double keysHeight = (rowCount * keyHeight) + ((rowCount - 1) * 6);
        double contentHeight = TopBarHeight + 6 + keysHeight + 18 + 2;
        double contentWidth = BaseWidthDip * Scale;

        int width = DipToPixels(hwnd, contentWidth);
        int height = DipToPixels(hwnd, contentHeight);

        DisplayArea display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 work = display.WorkArea;
        int x = work.X + Math.Max(0, (work.Width - width) / 2);
        int y = work.Y + Math.Max(0, work.Height - height - DipToPixels(hwnd, 16));

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private static int DipToPixels(nint hwnd, double dip)
    {
        uint dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return (int)Math.Round(dip * dpi / 96.0);
    }

    private bool ShouldShowNumberRow() =>
        Settings.ShowNumberRow && _layout.Current.NumberRow.Count > 0;

    // --- Keyboard rendering ------------------------------------------------

    private void RenderKeyboard()
    {
        KeysHost.Children.Clear();

        double keyHeight = BaseKeyHeight * Scale;
        double primaryFont = 19 * Scale;
        double secondaryFont = 10.5 * Scale;

        if (ShouldShowNumberRow())
        {
            KeysHost.Children.Add(BuildRow(_layout.Current.NumberRow, keyHeight, primaryFont, secondaryFont));
        }

        foreach (IReadOnlyList<KeyDefinition> row in _layout.Current.Rows)
        {
            KeysHost.Children.Add(BuildRow(row, keyHeight, primaryFont, secondaryFont));
        }
    }

    private Grid BuildRow(IReadOnlyList<KeyDefinition> keys, double keyHeight, double primaryFont, double secondaryFont)
    {
        var grid = new Grid { Height = keyHeight };

        for (int i = 0; i < keys.Count; i++)
        {
            KeyDefinition key = keys[i];
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(key.WidthUnits, GridUnitType.Star),
            });

            Border border = BuildKey(key, primaryFont, secondaryFont);
            Grid.SetColumn(border, i);
            grid.Children.Add(border);
        }

        return grid;
    }

    private Border BuildKey(KeyDefinition key, double primaryFont, double secondaryFont)
    {
        Brush baseBrush = BaseBrushFor(key);

        var border = new Border
        {
            Background = baseBrush,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = BuildKeyContent(key, primaryFont, secondaryFont),
            Tag = new KeyContext(key, baseBrush),
        };

        border.PointerPressed += OnKeyPointerPressed;
        border.PointerReleased += OnKeyPointerReleased;
        border.PointerCanceled += OnKeyPointerCanceled;
        border.PointerCaptureLost += OnKeyPointerCaptureLost;
        border.PointerMoved += OnKeyPointerMoved;
        // TODO(swipe): to add glide typing, sample PointerMoved positions across
        // letter keys here and feed them to a word decoder.
        return border;
    }

    private Brush BaseBrushFor(KeyDefinition key)
    {
        if (key.Kind == KeyKind.Shift && _layout.Shift != ShiftState.Off)
        {
            return _accentBrush;
        }

        return key.IsFunctionKey ? _funcBrush : _letterBrush;
    }

    private UIElement BuildKeyContent(KeyDefinition key, double primaryFont, double secondaryFont)
    {
        var grid = new Grid();

        var primary = new TextBlock
        {
            Text = PrimaryLabel(key),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = key.Kind is KeyKind.Symbols or KeyKind.Space ? primaryFont * 0.62 : primaryFont,
        };
        grid.Children.Add(primary);

        if (Settings.ShowSecondaryGlyphs && key.Kind == KeyKind.Character && key.SecondaryGlyph is not null)
        {
            grid.Children.Add(new TextBlock
            {
                Text = key.SecondaryGlyph,
                FontSize = secondaryFont,
                Opacity = 0.55,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 5, 0),
            });
        }

        return grid;
    }

    private string PrimaryLabel(KeyDefinition key)
    {
        switch (key.Kind)
        {
            case KeyKind.Space:
                return "FR • EN";
            case KeyKind.Shift:
                return _layout.Shift == ShiftState.CapsLock ? "⇪" : "⇧";
            case KeyKind.Character when key.Character is char c && char.IsLetter(c):
                return (_layout.IsUpper ? char.ToUpperInvariant(c) : c).ToString();
            default:
                return key.DisplayLabel;
        }
    }

    // --- Press handling ----------------------------------------------------

    private void OnKeyPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_activeBorder is not null)
        {
            ResetPress(commit: false);
        }

        var border = (Border)sender;
        var context = (KeyContext)border.Tag;

        _activeBorder = border;
        _activeKey = context.Key;
        _activePointerId = e.Pointer.PointerId;
        _repeatFired = false;
        _popupShown = false;
        _spaceHandled = false;
        _wordDeleted = false;

        Point p = e.GetCurrentPoint(RootGrid).Position;
        _pressStartX = p.X;
        _lastWordX = p.X;

        border.CapturePointer(e.Pointer);
        border.Background = _pressedBrush;

        StartPressTimer(context.Key);
        e.Handled = true;
    }

    private void StartPressTimer(KeyDefinition key)
    {
        double delay;
        if (key.Kind == KeyKind.Space)
        {
            _pressMode = PressMode.SpaceLanguage;
            delay = Settings.LongPressDelayMs;
        }
        else if (key.Kind == KeyKind.Character && key.HasLongPress && Settings.LongPressEnabled)
        {
            _pressMode = PressMode.Popup;
            delay = Settings.LongPressDelayMs;
        }
        else if (key.Repeatable && Settings.KeyRepeatEnabled)
        {
            _pressMode = PressMode.Repeat;
            delay = Settings.KeyRepeatInitialDelayMs;
        }
        else
        {
            _pressMode = PressMode.None;
            return;
        }

        _pressTimer.Interval = TimeSpan.FromMilliseconds(delay);
        _pressTimer.Start();
    }

    private void OnPressTimerTick(DispatcherQueueTimer sender, object args)
    {
        _pressTimer.Stop();
        if (_activeKey is null)
        {
            return;
        }

        switch (_pressMode)
        {
            case PressMode.Popup:
                ShowLongPressPopup(_activeBorder!, _activeKey);
                _popupShown = true;
                break;
            case PressMode.Repeat:
                _repeatFired = true;
                InjectForKey(_activeKey);
                _repeatTimer.Interval = TimeSpan.FromMilliseconds(Settings.KeyRepeatIntervalMs);
                _repeatTimer.Start();
                break;
            case PressMode.SpaceLanguage:
                _spaceHandled = true;
                _layout.ToggleLanguage();
                _settingsService.Update(s => s.LayoutId = _layout.Current.Id);
                break;
        }
    }

    private void OnRepeatTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_activeKey is not null)
        {
            InjectForKey(_activeKey);
        }
    }

    private void OnKeyPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_activeBorder is null || e.Pointer.PointerId != _activePointerId || _activeKey is null)
        {
            return;
        }

        double x = e.GetCurrentPoint(RootGrid).Position.X;

        if (_popupShown)
        {
            UpdatePopupHighlight(x);
            return;
        }

        if (_activeKey.Kind == KeyKind.Backspace)
        {
            double threshold = 45 * Scale;
            if (x - _lastWordX <= -threshold)
            {
                _pressTimer.Stop();
                _repeatTimer.Stop();
                KeyboardInjector.InjectDeleteWord();
                _wordDeleted = true;
                _lastWordX = x;
            }
        }
    }

    private void OnKeyPointerReleased(object sender, PointerRoutedEventArgs e) => ResetPress(commit: true);

    private void OnKeyPointerCanceled(object sender, PointerRoutedEventArgs e) => ResetPress(commit: false);

    private void OnKeyPointerCaptureLost(object sender, PointerRoutedEventArgs e) => ResetPress(commit: false);

    private void ResetPress(bool commit)
    {
        Border? border = _activeBorder;
        KeyDefinition? key = _activeKey;
        if (border is null || key is null)
        {
            return;
        }

        _activeBorder = null;
        _activeKey = null;
        _pressTimer.Stop();
        _repeatTimer.Stop();

        if (border.Tag is KeyContext context)
        {
            border.Background = context.BaseBrush;
        }

        if (_popupShown)
        {
            if (commit)
            {
                CommitPopupSelection();
            }

            CloseLongPressPopup();
        }
        else if (commit && !_wordDeleted && !_repeatFired && !_spaceHandled)
        {
            PerformTap(key);
        }

        _pressMode = PressMode.None;
    }

    private void PerformTap(KeyDefinition key)
    {
        switch (key.Kind)
        {
            case KeyKind.Shift:
                _layout.CycleShift();
                break;
            case KeyKind.Backspace:
                KeyboardInjector.InjectBackspace();
                break;
            case KeyKind.Enter:
                KeyboardInjector.InjectEnter();
                break;
            case KeyKind.Symbols:
                _layout.ToggleSymbols();
                RelayoutWindow();
                break;
            case KeyKind.Emoji:
                KeyboardInjector.InjectText("🙂");
                break;
            case KeyKind.Space:
                KeyboardInjector.InjectCharacter(' ');
                break;
            case KeyKind.Character:
                InjectCharacterKey(key);
                break;
        }
    }

    private void InjectForKey(KeyDefinition key)
    {
        switch (key.Kind)
        {
            case KeyKind.Backspace:
                KeyboardInjector.InjectBackspace();
                break;
            case KeyKind.Space:
                KeyboardInjector.InjectCharacter(' ');
                break;
            case KeyKind.Character:
                InjectCharacterKey(key);
                break;
        }
    }

    private void InjectCharacterKey(KeyDefinition key)
    {
        if (key.Character is not char c)
        {
            return;
        }

        if (_layout.IsUpper && char.IsLetter(c))
        {
            c = char.ToUpperInvariant(c);
        }

        KeyboardInjector.InjectCharacter(c);
        _layout.ConsumeShift();
    }

    // --- Long-press popup --------------------------------------------------

    private void ShowLongPressPopup(Border keyBorder, KeyDefinition key)
    {
        _popupChars = key.LongPress ?? [];
        if (_popupChars.Count == 0)
        {
            return;
        }

        LongPressItems.Children.Clear();
        _popupItems.Clear();

        _popupItemWidth = 40 * Scale;
        double itemHeight = 46 * Scale;

        foreach (string glyph in _popupChars)
        {
            var item = new Border
            {
                Width = _popupItemWidth,
                Height = itemHeight,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Colors.Transparent),
                Child = new TextBlock
                {
                    Text = glyph,
                    FontSize = 20 * Scale,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            _popupItems.Add(item);
            LongPressItems.Children.Add(item);
        }

        double popupWidth = (_popupItemWidth * _popupChars.Count) + 8;
        double popupHeight = itemHeight + 8;

        GeneralTransform transform = keyBorder.TransformToVisual(RootGrid);
        Point topLeft = transform.TransformPoint(new Point(0, 0));

        double left = topLeft.X + (keyBorder.ActualWidth / 2) - (popupWidth / 2);
        left = Math.Clamp(left, 4, Math.Max(4, RootGrid.ActualWidth - popupWidth - 4));

        double top = topLeft.Y - popupHeight - 4;
        if (top < 0)
        {
            top = topLeft.Y + keyBorder.ActualHeight + 4;
        }

        LongPressPopup.HorizontalOffset = left;
        LongPressPopup.VerticalOffset = top;
        LongPressPopup.IsOpen = true;

        UpdatePopupHighlight(_pressStartX);
    }

    private void UpdatePopupHighlight(double pointerX)
    {
        if (_popupItems.Count == 0)
        {
            return;
        }

        double left = LongPressPopup.HorizontalOffset + 4;
        int index = (int)Math.Floor((pointerX - left) / _popupItemWidth);
        index = Math.Clamp(index, 0, _popupItems.Count - 1);
        _popupIndex = index;

        for (int i = 0; i < _popupItems.Count; i++)
        {
            _popupItems[i].Background = i == index
                ? _accentBrush
                : new SolidColorBrush(Colors.Transparent);
        }
    }

    private void CommitPopupSelection()
    {
        if (_popupIndex < 0 || _popupIndex >= _popupChars.Count)
        {
            return;
        }

        string glyph = _popupChars[_popupIndex];
        if (_layout.IsUpper && glyph.Length == 1 && char.IsLetter(glyph[0]))
        {
            glyph = glyph.ToUpperInvariant();
        }

        KeyboardInjector.InjectText(glyph);
        _layout.ConsumeShift();
    }

    private void CloseLongPressPopup()
    {
        LongPressPopup.IsOpen = false;
        LongPressItems.Children.Clear();
        _popupItems.Clear();
        _popupChars = [];
    }

    // --- Settings UI -------------------------------------------------------

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        LoadSettingsIntoUi();
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private void OnCloseSettingsClicked(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void LoadSettingsIntoUi()
    {
        _suppressSettingsEvents = true;

        LayoutChoice.SelectedIndex = Settings.LayoutId == LayoutCatalog.QwertyId ? 1 : 0;
        ThemeChoice.SelectedIndex = Settings.Theme == "Light" ? 1 : 0;
        NumberRowToggle.IsOn = Settings.ShowNumberRow;
        SecondaryGlyphToggle.IsOn = Settings.ShowSecondaryGlyphs;
        LongPressToggle.IsOn = Settings.LongPressEnabled;
        KeyRepeatToggle.IsOn = Settings.KeyRepeatEnabled;
        RepeatDelaySlider.Value = Settings.KeyRepeatInitialDelayMs;
        RepeatIntervalSlider.Value = Settings.KeyRepeatIntervalMs;
        OpacitySlider.Value = Settings.Opacity;
        SizeSlider.Value = Settings.SizeScale;

        _suppressSettingsEvents = false;
    }

    private void OnSettingsChanged()
    {
        ApplyAppearance();
        RenderKeyboard();
        RelayoutWindow();
    }

    private void OnLayoutChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsEvents || LayoutChoice.SelectedIndex < 0)
        {
            return;
        }

        string id = LayoutChoice.SelectedIndex == 1 ? LayoutCatalog.QwertyId : LayoutCatalog.AzertyId;
        _settingsService.Update(s => s.LayoutId = id);
        _layout.SetAlphabetic(id);
    }

    private void OnThemeChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsEvents || ThemeChoice.SelectedIndex < 0)
        {
            return;
        }

        string theme = ThemeChoice.SelectedIndex == 1 ? "Light" : "Dark";
        _settingsService.Update(s => s.Theme = theme);
    }

    private void OnNumberRowToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.ShowNumberRow = NumberRowToggle.IsOn);
    }

    private void OnSecondaryGlyphToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.ShowSecondaryGlyphs = SecondaryGlyphToggle.IsOn);
    }

    private void OnLongPressToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.LongPressEnabled = LongPressToggle.IsOn);
    }

    private void OnKeyRepeatToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.KeyRepeatEnabled = KeyRepeatToggle.IsOn);
    }

    private void OnRepeatDelayChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.KeyRepeatInitialDelayMs = (int)e.NewValue);
    }

    private void OnRepeatIntervalChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.KeyRepeatIntervalMs = (int)e.NewValue);
    }

    private void OnOpacityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.Opacity = e.NewValue);
    }

    private void OnSizeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.SizeScale = e.NewValue);
    }

    // --- Top bar / window chrome ------------------------------------------

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnDragHandlePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = true;
        ((UIElement)sender).CapturePointer(e.Pointer);
        NativeMethods.GetCursorPos(out _dragCursorStart);
        _dragWindowStart = AppWindow.Position;
    }

    private void OnDragHandlePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        NativeMethods.GetCursorPos(out POINT now);
        AppWindow.Move(new PointInt32(
            _dragWindowStart.X + (now.X - _dragCursorStart.X),
            _dragWindowStart.Y + (now.Y - _dragCursorStart.Y)));
    }

    private void OnDragHandlePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        if (sender is UIElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
        }
    }

    private static void OnClosed(object sender, WindowEventArgs args)
    {
        Application.Current.Exit();
    }

    private sealed record KeyContext(KeyDefinition Key, Brush BaseBrush);
}

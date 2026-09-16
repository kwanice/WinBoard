using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using WinBoard.Core;
using WinBoard.Input;
using WinBoard.Layouts;
using WinBoard.Services;

namespace WinBoard.UI;

public sealed partial class KeyboardWindow : Window
{
    private const double BaseWidthDip = 720;
    private const double BaseKeyHeight = 46;
    private const double TopBarHeight = 48;
    private const double BaseSuggestionHeight = 34;
    private const double CaretPixelsPerStep = 18;

    private readonly SettingsService _settingsService = new();
    private readonly LayoutService _layout = new();
    private readonly WordListService _wordLists = new();
    private readonly IClipboardClipSource _clips = new FileClipboardClipSource();
    private string _emojiTab = EmojiCatalog.RecentsId;
    private string _emojiQuery = string.Empty;

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
    private bool _caretMode;
    private double _lastCaretX;
    private double _caretAccum;
    private DateTime _spacePressedAt;
    private double _pressStartX;
    private double _lastWordX;

    // Long-press popup state.
    private readonly List<Border> _popupItems = new();
    private IReadOnlyList<string> _popupChars = [];
    private double _popupItemWidth;
    private int _popupIndex;

    // Swipe typing state.
    private readonly List<(char Letter, Border Border)> _letterKeyBorders = new();
    private readonly Dictionary<char, Point> _letterCenters = new();
    private readonly List<LetterBox> _letterBoxes = new();
    private readonly List<Point> _swipePoints = new();
    private readonly List<char> _swipeChars = new();
    private double _letterKeySize = BaseKeyHeight;
    private bool _swiping;
    private Point _swipeStartPoint;
    private KeyDefinition? _swipeStartKey;
    private Polyline? _swipeTrail;
    private int _lastSwipeWordLength;
    private uint _dragPointerId;

    private readonly record struct LetterBox(char Letter, Rect Bounds, Point Center);

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
        SpaceHold,
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
        ApplyTransparency();
    }

    public void HideToTray()
    {
        AppWindow.Hide();
    }

    public void ShowFromTray()
    {
        ShowWithoutActivating();
    }

    public void ToggleFromTray()
    {
        if (AppWindow.IsVisible)
        {
            HideToTray();
        }
        else
        {
            ShowFromTray();
        }
    }

    public void OpenSettingsFromTray()
    {
        ShowFromTray();
        EmojiOverlay.Visibility = Visibility.Collapsed;
        ClipsOverlay.Visibility = Visibility.Collapsed;
        LoadSettingsIntoUi();
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    public void RequestQuit()
    {
        Close();
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

        _letterBrush = ResourceBrush("ControlFillColorDefaultBrush", Color.FromArgb(46, 255, 255, 255));
        _funcBrush = ResourceBrush("ControlFillColorSecondaryBrush", Color.FromArgb(22, 255, 255, 255));
        _pressedBrush = ResourceBrush("ControlFillColorTertiaryBrush", Color.FromArgb(85, 255, 255, 255));
        _accentBrush = ResourceBrush("AccentFillColorDefaultBrush", Colors.SlateBlue);

        ApplyTransparency();
    }

    /// <summary>
    /// Opacity used to only fade KeyboardRoot, which left the Acrylic backdrop
    /// fully opaque. For values below ~1 we drop the backdrop and apply
    /// WS_EX_LAYERED + SetLayeredWindowAttributes on the HWND (unpackaged path).
    /// </summary>
    private void ApplyTransparency()
    {
        nint hwnd = NoActivateWindow.GetHwnd(this);
        byte alpha = (byte)Math.Clamp((int)Math.Round(Settings.Opacity * 255), 64, 255);
        if (alpha >= 250)
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
            KeyboardRoot.Opacity = 1;
            NoActivateWindow.Apply(hwnd);
        }
        else
        {
            SystemBackdrop = null;
            KeyboardRoot.Opacity = 1;
            NoActivateWindow.Apply(hwnd, alpha);
        }
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
        double suggestionHeight = BaseSuggestionHeight * Scale;
        double contentHeight = TopBarHeight + 6 + suggestionHeight + 6 + keysHeight + 18 + 2;
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
        _letterKeyBorders.Clear();
        SuggestionScroller.Height = BaseSuggestionHeight * Scale;

        double keyHeight = BaseKeyHeight * Scale;
        double primaryFont = 19 * Scale * Settings.LetterFontScale;
        double secondaryFont = 10.5 * Scale * Settings.LetterFontScale;

        RebuildSuggestionBar();

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
            BorderThickness = Settings.ShowKeyOutlines ? new Thickness(1) : new Thickness(0),
            BorderBrush = Settings.ShowKeyOutlines
                ? ResourceBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(80, 255, 255, 255))
                : null,
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

        if (key.Kind == KeyKind.Character && key.Character is char letter && char.IsLetter(letter))
        {
            _letterKeyBorders.Add((char.ToLowerInvariant(letter), border));
        }

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
        _caretMode = false;
        _caretAccum = 0;
        _spacePressedAt = DateTime.UtcNow;

        Point p = e.GetCurrentPoint(RootGrid).Position;
        _pressStartX = p.X;
        _lastWordX = p.X;
        _lastCaretX = p.X;
        _swipeStartPoint = p;
        _swipeStartKey = context.Key;

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
            _pressMode = PressMode.SpaceHold;
            return;
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

        Point p = e.GetCurrentPoint(RootGrid).Position;
        double x = p.X;

        if (_swiping)
        {
            AppendSwipePoint(p);
            return;
        }

        if (_popupShown)
        {
            UpdatePopupHighlight(x);
            return;
        }

        if (_activeKey.Kind == KeyKind.Space)
        {
            HandleSpacePointerMoved(x);
            return;
        }

        if (Settings.SwipeEnabled
            && _activeKey.Kind == KeyKind.Character
            && _activeKey.Character is char letter
            && char.IsLetter(letter))
        {
            double dx = x - _swipeStartPoint.X;
            double dy = p.Y - _swipeStartPoint.Y;
            double threshold = 22 * Scale;
            if ((dx * dx) + (dy * dy) >= threshold * threshold)
            {
                BeginSwipe();
                AppendSwipePoint(p);
            }

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

        if (_swiping)
        {
            EndSwipe(commit);
            _pressMode = PressMode.None;
            return;
        }

        if (_caretMode)
        {
            _pressMode = PressMode.None;
            return;
        }

        if (key.Kind == KeyKind.Space && commit)
        {
            ResetSwipeContext();
            TimeSpan held = DateTime.UtcNow - _spacePressedAt;
            if (held.TotalMilliseconds >= Settings.LongPressDelayMs)
            {
                _layout.ToggleLanguage();
                _settingsService.Update(s => s.LayoutId = _layout.Current.Id);
            }
            else
            {
                KeyboardInjector.InjectCharacter(' ');
            }

            _pressMode = PressMode.None;
            return;
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
                ResetSwipeContext();
                KeyboardInjector.InjectBackspace();
                break;
            case KeyKind.Enter:
                ResetSwipeContext();
                KeyboardInjector.InjectEnter();
                break;
            case KeyKind.Symbols:
                _layout.ToggleSymbols();
                RelayoutWindow();
                break;
            case KeyKind.Emoji:
                ResetSwipeContext();
                OpenEmojiPanel();
                break;
            case KeyKind.Space:
                // Space tap / language / caret are handled in ResetPress.
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

        ResetSwipeContext();

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

        ResetSwipeContext();
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

    // --- Swipe typing ------------------------------------------------------

    private void HandleSpacePointerMoved(double x)
    {
        if (!_caretMode)
        {
            if (Math.Abs(x - _pressStartX) < 24 * Scale)
            {
                return;
            }

            _caretMode = true;
            _spaceHandled = true;
            _caretAccum = 0;
            _lastCaretX = x;
            return;
        }

        double delta = x - _lastCaretX;
        _lastCaretX = x;
        _caretAccum += delta;
        double step = CaretPixelsPerStep * Scale;
        while (_caretAccum >= step)
        {
            KeyboardInjector.InjectRight();
            _caretAccum -= step;
        }

        while (_caretAccum <= -step)
        {
            KeyboardInjector.InjectLeft();
            _caretAccum += step;
        }
    }

    private void BeginSwipe()
    {
        _swiping = true;
        _pressTimer.Stop();
        _repeatTimer.Stop();
        _popupShown = false;

        BuildLetterHitboxes();

        _swipePoints.Clear();
        _swipeChars.Clear();
        _swipePoints.Add(_swipeStartPoint);
        if (_swipeStartKey?.Character is char c && char.IsLetter(c))
        {
            _swipeChars.Add(char.ToLowerInvariant(c));
        }

        SwipeTrail.Children.Clear();
        _swipeTrail = null;
        if (Settings.ShowSwipeTrail)
        {
            _swipeTrail = new Polyline
            {
                Stroke = _accentBrush,
                StrokeThickness = 4 * Scale,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.85,
                IsHitTestVisible = false,
                Points = new PointCollection(),
            };
            _swipeTrail.Points.Add(_swipeStartPoint);
            SwipeTrail.Children.Add(_swipeTrail);
        }
    }

    private void BuildLetterHitboxes()
    {
        _letterBoxes.Clear();
        _letterCenters.Clear();

        double sizeSum = 0;
        int count = 0;
        foreach ((char letter, Border border) in _letterKeyBorders)
        {
            if (border.ActualWidth <= 0 || border.ActualHeight <= 0)
            {
                continue;
            }

            GeneralTransform transform = border.TransformToVisual(RootGrid);
            Point topLeft = transform.TransformPoint(new Point(0, 0));
            var bounds = new Rect(topLeft.X, topLeft.Y, border.ActualWidth, border.ActualHeight);
            var center = new Point(topLeft.X + (border.ActualWidth / 2), topLeft.Y + (border.ActualHeight / 2));

            _letterBoxes.Add(new LetterBox(letter, bounds, center));
            _letterCenters[letter] = center;
            sizeSum += border.ActualWidth;
            count++;
        }

        _letterKeySize = count > 0 ? sizeSum / count : BaseKeyHeight * Scale;
    }

    private void AppendSwipePoint(Point p)
    {
        _swipePoints.Add(p);
        _swipeTrail?.Points.Add(p);

        char c = HitTestLetter(p);
        if (c != '\0' && (_swipeChars.Count == 0 || _swipeChars[^1] != c))
        {
            _swipeChars.Add(c);
        }
    }

    private char HitTestLetter(Point p)
    {
        foreach (LetterBox box in _letterBoxes)
        {
            if (p.X >= box.Bounds.X && p.X <= box.Bounds.X + box.Bounds.Width
                && p.Y >= box.Bounds.Y && p.Y <= box.Bounds.Y + box.Bounds.Height)
            {
                return box.Letter;
            }
        }

        return '\0';
    }

    private void EndSwipe(bool commit)
    {
        _swiping = false;
        SwipeTrail.Children.Clear();
        _swipeTrail = null;

        if (!commit)
        {
            _swipePoints.Clear();
            _swipeChars.Clear();
            return;
        }

        if (_swipeChars.Count < 2)
        {
            // Not enough letters crossed: treat as a plain tap on the start key.
            if (_swipeStartKey is not null)
            {
                PerformTap(_swipeStartKey);
            }

            _swipePoints.Clear();
            _swipeChars.Clear();
            return;
        }

        WordList words = _wordLists.ForLayout(_layout.Current.Id);
        var path = _swipePoints.Select(pt => new Point2(pt.X, pt.Y)).ToList();
        var centers = _letterCenters.ToDictionary(kv => kv.Key, kv => new Point2(kv.Value.X, kv.Value.Y));
        IReadOnlyList<string> candidates = SwipeDecoder.Decode(
            _swipeChars, path, centers, words, _letterKeySize);

        _swipePoints.Clear();
        _swipeChars.Clear();

        if (candidates.Count == 0)
        {
            ClearSuggestions();
            _lastSwipeWordLength = 0;
            return;
        }

        InjectSwipeWord(candidates[0]);
        RebuildSuggestionBar(candidates);
    }

    private void InjectSwipeWord(string word)
    {
        string text = _layout.IsUpper && word.Length > 0
            ? char.ToUpperInvariant(word[0]) + word[1..]
            : word;

        // Trailing space separates consecutive swiped words (Gboard behavior).
        KeyboardInjector.InjectText(text + " ");
        _layout.ConsumeShift();
        _lastSwipeWordLength = text.Length + 1;
    }

    private void RebuildSuggestionBar(IReadOnlyList<string>? candidates = null)
    {
        SuggestionBar.Children.Clear();
        SuggestionBar.Children.Add(BuildClipboardButton());

        if (candidates is null)
        {
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            string word = candidates[i];
            var chip = new Button
            {
                Content = word,
                Tag = word,
                Height = 28 * Scale,
                MinWidth = 0,
                Padding = new Thickness(14, 0, 14, 0),
                CornerRadius = new CornerRadius(14),
                FontSize = 15 * Scale,
                FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                AllowFocusOnInteraction = false,
                IsTabStop = false,
                Background = i == 0 ? _funcBrush : new SolidColorBrush(Colors.Transparent),
            };
            chip.Click += OnSuggestionClicked;
            SuggestionBar.Children.Add(chip);
        }
    }

    private Button BuildClipboardButton()
    {
        var button = new Button
        {
            Content = "📋",
            Width = 36,
            Height = 28 * Scale,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            AllowFocusOnInteraction = false,
            IsTabStop = false,
        };
        ToolTipService.SetToolTip(button, "MyClipboard");
        button.Click += OnClipboardClicked;
        return button;
    }

    private void OnClipboardClicked(object sender, RoutedEventArgs e)
    {
        EmojiOverlay.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Collapsed;
        PopulateClipsPanel();
        ClipsOverlay.Visibility = Visibility.Visible;
    }

    private void OnCloseClipsClicked(object sender, RoutedEventArgs e)
    {
        ClipsOverlay.Visibility = Visibility.Collapsed;
    }

    private void PopulateClipsPanel()
    {
        ClipsList.Children.Clear();
        if (!_clips.IsAvailable)
        {
            ClipsEmpty.Visibility = Visibility.Visible;
            ClipsEmpty.Text =
                "MyClipboard n’est pas disponible. WinBoard lit %LOCALAPPDATA%\\MyClipBoard\\clips.json (100 % local). Voir le README, section « Connexion MyClipboard ».";
            return;
        }

        IReadOnlyList<ClipboardClip> clips = _clips.GetRecentClips(12);
        if (clips.Count == 0)
        {
            ClipsEmpty.Visibility = Visibility.Visible;
            ClipsEmpty.Text = "Aucun extrait dans MyClipboard.";
            return;
        }

        ClipsEmpty.Visibility = Visibility.Collapsed;
        foreach (ClipboardClip clip in clips)
        {
            var row = new Button
            {
                Content = clip.Preview,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Tag = clip.Text,
                AllowFocusOnInteraction = false,
                IsTabStop = false,
            };
            row.Click += OnClipRowClicked;
            ClipsList.Children.Add(row);
        }
    }

    private void OnClipRowClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text } || string.IsNullOrEmpty(text))
        {
            return;
        }

        KeyboardInjector.InjectText(text);
        ClipsOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnSuggestionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string word })
        {
            return;
        }

        for (int i = 0; i < _lastSwipeWordLength; i++)
        {
            KeyboardInjector.InjectBackspace();
        }

        KeyboardInjector.InjectText(word + " ");
        _lastSwipeWordLength = word.Length + 1;
    }

    private void ClearSuggestions()
    {
        RebuildSuggestionBar();
    }

    /// <summary>Clears swipe suggestions/replacement context after manual input.</summary>
    private void ResetSwipeContext()
    {
        RebuildSuggestionBar();
        _lastSwipeWordLength = 0;
    }

    // --- Emoji panel -------------------------------------------------------

    private void OpenEmojiPanel()
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        ClipsOverlay.Visibility = Visibility.Collapsed;
        if (string.IsNullOrEmpty(_emojiTab))
        {
            _emojiTab = EmojiCatalog.RecentsId;
        }

        BuildEmojiCategoryBar();
        RenderEmojiGrid();
        EmojiOverlay.Visibility = Visibility.Visible;
    }

    private void OnCloseEmojiClicked(object sender, RoutedEventArgs e)
    {
        EmojiOverlay.Visibility = Visibility.Collapsed;
    }

    private void BuildEmojiCategoryBar()
    {
        EmojiCategoryBar.Children.Clear();
        AddEmojiTabButton(EmojiCatalog.RecentsId, "🕒", "Récents");
        AddEmojiTabButton(EmojiCatalog.SearchId, "🔍", "Recherche");
        foreach (EmojiCategory category in EmojiCatalog.Categories)
        {
            AddEmojiTabButton(category.Id, category.Icon, category.Label);
        }
    }

    private void AddEmojiTabButton(string id, string icon, string label)
    {
        bool selected = _emojiTab == id;
        var button = new Button
        {
            Content = icon,
            Tag = id,
            MinWidth = 44,
            Height = 40,
            Padding = new Thickness(8, 0, 8, 0),
            AllowFocusOnInteraction = false,
            IsTabStop = false,
            FontSize = 18,
            FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI"),
        };
        if (selected)
        {
            button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        }

        ToolTipService.SetToolTip(button, label);
        button.Click += OnEmojiTabClicked;
        EmojiCategoryBar.Children.Add(button);
    }

    private void OnEmojiTabClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        _emojiTab = id;
        if (id != EmojiCatalog.SearchId)
        {
            _emojiQuery = string.Empty;
        }

        BuildEmojiCategoryBar();
        RenderEmojiGrid();
    }

    private void RenderEmojiGrid()
    {
        bool search = _emojiTab == EmojiCatalog.SearchId;
        EmojiSearchBar.Visibility = search ? Visibility.Visible : Visibility.Collapsed;
        EmojiLetterGrid.Visibility = search ? Visibility.Visible : Visibility.Collapsed;
        EmojiSearchQuery.Text = string.IsNullOrEmpty(_emojiQuery)
            ? "Rechercher (lettres ci-dessous — le focus reste dans l’app cible)"
            : _emojiQuery;

        if (search)
        {
            BuildEmojiLetterGrid();
        }

        IReadOnlyList<EmojiItem> items = ResolveEmojiItems();
        EmojiGridHost.Children.Clear();
        EmojiGridHost.RowDefinitions.Clear();
        EmojiGridHost.ColumnDefinitions.Clear();

        if (items.Count == 0)
        {
            EmojiEmpty.Visibility = Visibility.Visible;
            EmojiEmpty.Text = _emojiTab == EmojiCatalog.RecentsId
                ? "Aucun emoji récent. Touchez un emoji pour le mémoriser ici (local, sans réseau)."
                : search && _emojiQuery.Length == 0
                    ? "Tapez un mot-clé avec les lettres (ex. coeur, smile, france)."
                    : "Aucun emoji ne correspond.";
            return;
        }

        EmojiEmpty.Visibility = Visibility.Collapsed;
        const int columns = 8;
        for (int c = 0; c < columns; c++)
        {
            EmojiGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int i = 0; i < items.Count; i++)
        {
            int row = i / columns;
            int col = i % columns;
            while (EmojiGridHost.RowDefinitions.Count <= row)
            {
                EmojiGridHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            Button button = CreateEmojiButton(items[i].Glyph);
            Grid.SetRow(button, row);
            Grid.SetColumn(button, col);
            EmojiGridHost.Children.Add(button);
        }
    }

    private IReadOnlyList<EmojiItem> ResolveEmojiItems()
    {
        if (_emojiTab == EmojiCatalog.RecentsId)
        {
            return EmojiCatalog.ResolveRecent(Settings.RecentEmojis);
        }

        if (_emojiTab == EmojiCatalog.SearchId)
        {
            return EmojiCatalog.Search(_emojiQuery);
        }

        EmojiCategory? category = EmojiCatalog.Categories.FirstOrDefault(c => c.Id == _emojiTab);
        return category?.Items ?? [];
    }

    private Button CreateEmojiButton(string glyph)
    {
        var button = new Button
        {
            Content = glyph,
            Tag = glyph,
            MinWidth = 44,
            MinHeight = 44,
            Height = 44,
            Padding = new Thickness(0),
            Margin = new Thickness(1),
            FontSize = 22,
            FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI"),
            AllowFocusOnInteraction = false,
            IsTabStop = false,
        };
        button.Click += OnEmojiGlyphClicked;
        return button;
    }

    private void BuildEmojiLetterGrid()
    {
        EmojiLetterGrid.Children.Clear();
        EmojiLetterGrid.RowDefinitions.Clear();
        EmojiLetterGrid.ColumnDefinitions.Clear();
        const string letters = "abcdefghijklmnopqrstuvwxyz";
        const int columns = 10;
        for (int c = 0; c < columns; c++)
        {
            EmojiLetterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int i = 0; i < letters.Length; i++)
        {
            int row = i / columns;
            int col = i % columns;
            while (EmojiLetterGrid.RowDefinitions.Count <= row)
            {
                EmojiLetterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            char letter = letters[i];
            var button = new Button
            {
                Content = letter.ToString(),
                Tag = letter.ToString(),
                MinHeight = 36,
                Height = 36,
                Padding = new Thickness(0),
                Margin = new Thickness(1),
                AllowFocusOnInteraction = false,
                IsTabStop = false,
            };
            button.Click += OnEmojiSearchLetter;
            Grid.SetRow(button, row);
            Grid.SetColumn(button, col);
            EmojiLetterGrid.Children.Add(button);
        }
    }

    private void OnEmojiSearchLetter(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string letter })
        {
            return;
        }

        _emojiQuery += letter;
        RenderEmojiGrid();
    }

    private void OnEmojiSearchBackspace(object sender, RoutedEventArgs e)
    {
        if (_emojiQuery.Length == 0)
        {
            return;
        }

        _emojiQuery = _emojiQuery[..^1];
        RenderEmojiGrid();
    }

    private void OnEmojiSearchClear(object sender, RoutedEventArgs e)
    {
        _emojiQuery = string.Empty;
        RenderEmojiGrid();
    }

    private void OnEmojiGlyphClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string glyph } || string.IsNullOrEmpty(glyph))
        {
            return;
        }

        KeyboardInjector.InjectText(glyph);
        _settingsService.Update(s => s.RecentEmojis = EmojiCatalog.PushRecent(s.RecentEmojis, glyph), notify: false);
        if (_emojiTab == EmojiCatalog.RecentsId)
        {
            RenderEmojiGrid();
        }
    }

    // --- Settings UI -------------------------------------------------------

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        EmojiOverlay.Visibility = Visibility.Collapsed;
        ClipsOverlay.Visibility = Visibility.Collapsed;
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

    private void OnSwipeToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.SwipeEnabled = SwipeToggle.IsOn);
    }

    private void OnSwipeTrailToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.ShowSwipeTrail = SwipeTrailToggle.IsOn);
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

    private void OnFontChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.LetterFontScale = e.NewValue);
    }

    private void OnOutlinesToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settingsService.Update(s => s.ShowKeyOutlines = OutlinesToggle.IsOn);
    }

    // --- Top bar / window chrome ------------------------------------------

    private void OnCloseClicked(object sender, RoutedEventArgs e) => HideToTray();

    private void OnQuitClicked(object sender, RoutedEventArgs e) => RequestQuit();

    private void OnDragHandlePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = true;
        _dragPointerId = e.Pointer.PointerId;
        ((UIElement)sender).CapturePointer(e.Pointer);
        TryGetScreenPoint(_dragPointerId, out _dragCursorStart);
        _dragWindowStart = AppWindow.Position;
        e.Handled = true;
    }

    private void OnDragHandlePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        if (!TryGetScreenPoint(e.Pointer.PointerId, out POINT now))
        {
            return;
        }

        AppWindow.Move(new PointInt32(
            _dragWindowStart.X + (now.X - _dragCursorStart.X),
            _dragWindowStart.Y + (now.Y - _dragCursorStart.Y)));
        e.Handled = true;
    }

    private static bool TryGetScreenPoint(uint pointerId, out POINT point)
    {
        if (NativeMethods.GetPointerInfo(pointerId, out POINTER_INFO info))
        {
            point = info.ptPixelLocation;
            return true;
        }

        return NativeMethods.GetCursorPos(out point);
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
        // Real exit (Quitter). Hide-to-tray uses AppWindow.Hide and does not raise Closed.
        Application.Current.Exit();
    }

    private sealed record KeyContext(KeyDefinition Key, Brush BaseBrush);
}

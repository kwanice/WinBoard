using System.Diagnostics;
using System.Threading.Tasks;
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
    private const double TopBarHeight = 32;
    private const double BaseSuggestionHeight = 34;
    private const double CaretPixelsPerStep = 18;

    private readonly SettingsService _settingsService = SettingsService.Shared;
    private readonly LayoutService _layout = new();
    private readonly WordListService _wordLists = new();
    private readonly IClipboardClipSource _clips = new FileClipboardClipSource();
    private string _emojiTab = EmojiCatalog.RecentsId;
    private string _emojiQuery = string.Empty;

    private readonly DispatcherQueueTimer _pressTimer;
    private readonly DispatcherQueueTimer _repeatTimer;
    private readonly DispatcherQueueTimer _windowDragTimer;
    private readonly DispatcherQueueTimer _clipsWatchTimer;
    private FileSystemWatcher? _clipsWatcher;
    private string _lastClipsFingerprint = string.Empty;

    // Active press state. Primary pointer = tap / swipe / space / repeat.
    // Shift may be held on a second PointerId without ResetPress'ing the primary.
    private Border? _activeBorder;
    private KeyDefinition? _activeKey;
    private uint _activePointerId;
    private uint? _shiftHoldPointerId;
    private Border? _shiftHoldBorder;
    private bool _pressShifted;
    private readonly List<Border> _allKeyBorders = new();
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
    private readonly List<Point> _pendingSwipePoints = new();
    private readonly List<long> _swipeTimesMs = new();
    private long _swipeStartTick;
    private Action<SwipeGestureCapture>? _diagSink;
    private Rect2 _swipeStartKeyBounds;
    private bool _swipeStartBoundsValid;
    private double _letterKeySize = BaseKeyHeight;
    private bool _swiping;
    private uint? _endedSwipePointerId;
    private Point _swipeStartPoint;
    private KeyDefinition? _swipeStartKey;
    private Polyline? _swipeTrail;
    private readonly SwipeCommitTracker _swipeCommit = new();
    private int _swipeDecodeSerial;
    private int _appliedSwipeInjectSerial;
    private readonly Dictionary<int, SwipeInjectReady> _readySwipeInjects = [];
    private string[]? _deferredSuggestionWords;
    private bool _deferKeyboardRender;
    private int _diagCaptureLostThisGesture;
    private bool _diagTrailClearedMidGesture;
    private uint _diagGesturePointerId;
    private int _diagGestureOrdinal;
    private long _diagLastSwipeEndTick;
    private double? _diagTimeSincePreviousMs;
    private string? _prevWord;
    private string _typedWord = string.Empty;

    private readonly record struct LetterBox(char Letter, Rect Bounds, Point Center);

    private readonly record struct SwipeInjectReady(string[] Candidates, bool Upper);

    // Bandeau drag state. All coordinates are physical screen pixels.
    private bool _windowDragging;
    private bool _windowDragMouse;
    private bool _windowDragNativeTracking;
    private uint _windowDragPointerId;
    private POINT _windowDragPointerStart;
    private PointInt32 _windowDragPositionStart;
    private int _windowDragReadMisses;

    // Cached brushes (rebuilt when the theme changes).
    private Brush _letterBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    private Brush _funcBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
    private Brush _pressedBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
    private Brush _accentBrush = new SolidColorBrush(Colors.SlateBlue);
    private Brush _outlineBrush = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255));
    private bool _initialPlacementDone;
    private bool _presenterChromeApplied;
    private readonly DispatcherQueueTimer _topmostWatchTimer;

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

        _pressTimer = DispatcherQueue.CreateTimer();
        _pressTimer.IsRepeating = false;
        _pressTimer.Tick += OnPressTimerTick;

        _repeatTimer = DispatcherQueue.CreateTimer();
        _repeatTimer.IsRepeating = true;
        _repeatTimer.Tick += OnRepeatTimerTick;

        _windowDragTimer = DispatcherQueue.CreateTimer();
        _windowDragTimer.IsRepeating = true;
        _windowDragTimer.Interval = TimeSpan.FromMilliseconds(8);
        _windowDragTimer.Tick += OnWindowDragTimerTick;

        _clipsWatchTimer = DispatcherQueue.CreateTimer();
        _clipsWatchTimer.IsRepeating = true;
        _clipsWatchTimer.Interval = TimeSpan.FromMilliseconds(800);
        _clipsWatchTimer.Tick += (_, _) => RefreshClipsPanelIfOpen();

        _topmostWatchTimer = DispatcherQueue.CreateTimer();
        _topmostWatchTimer.IsRepeating = true;
        _topmostWatchTimer.Interval = TimeSpan.FromSeconds(2);
        _topmostWatchTimer.Tick += OnTopmostWatchTick;

        _layout.SetAlphabetic(_settingsService.Current.LayoutId);
        _layout.Changed += (_, _) => OnLayoutChanged();
        _settingsService.Changed += (_, _) => OnSettingsChanged();
        Closed += OnClosed;
        Activated += OnKeyboardActivated;
        RootGrid.GettingFocus += OnRootGettingFocus;
        RootGrid.PointerMoved += OnRootSessionPointerMoved;
        RootGrid.PointerReleased += OnRootSessionPointerReleased;
        RootGrid.PointerCanceled += OnRootSessionPointerCanceled;
        RootGrid.PointerCaptureLost += OnRootSessionPointerCaptureLost;

        ConfigurePresenter();
        NoActivateWindow.Apply(NoActivateWindow.GetHwnd(this));
        InputTargetGuard.BindKeyboard(NoActivateWindow.GetHwnd(this));
        AppWindow.Changed += OnAppWindowChanged;
        _topmostWatchTimer.Start();

        ApplyAppearance();
        RenderKeyboard();
        RelayoutWindow();
        _ = Task.Run(() =>
        {
            try
            {
                _wordLists.Warm();
            }
            catch
            {
                // First swipe will load; UI must not crash on warmup failure.
            }
        });
    }

    /// <summary>Shows the overlay without activation so the target app keeps focus.</summary>
    public void ShowWithoutActivating()
    {
        ConfigurePresenter();
        AppWindow.Show(activateWindow: false);
        ApplyTransparency();
        InputTargetGuard.BindKeyboard(NoActivateWindow.GetHwnd(this));
        InputTargetGuard.EnsureTargetForeground();
        KeepTopmost();
        if (ClipsOverlay.Visibility == Visibility.Visible)
        {
            StartClipsWatch();
        }
    }

    public void HideToTray()
    {
        StopClipsWatch();
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
        OpenSettings();
    }

    /// <summary>
    /// While the diagnostic window is open, each finished swipe (commit or
    /// abort) is forwarded here. Null disables capture — normal typing is
    /// unchanged. Attaching a sink resets gesture ordinals so a new session
    /// starts at 1.
    /// </summary>
    public void AttachSwipeDiagnostic(Action<SwipeGestureCapture>? sink)
    {
        _diagSink = sink;
        if (sink is not null)
        {
            _diagGestureOrdinal = 0;
            _diagLastSwipeEndTick = 0;
        }
    }

    public string DiagnosticLayoutLabel => SwipeDiagnostic.LayoutLabel(_layout.Current.Id);

    public double DiagnosticKeyboardScale => Settings.SizeScale;

    public void OpenSwipeDiagnostic()
    {
        EmojiOverlay.Visibility = Visibility.Collapsed;
        HideClipsPanel();
        SwipeDiagnosticWindow.Show(this);
        KeepTopmost();
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

        if (!presenter.IsAlwaysOnTop)
        {
            presenter.IsAlwaysOnTop = true;
        }

        if (_presenterChromeApplied)
        {
            return;
        }

        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        _presenterChromeApplied = true;
    }

    /// <summary>
    /// Re-apply HWND_TOPMOST without activating. WinUI often posts a follow-up
    /// z-order change after Show / style / AppWindow, so a deferred pass runs too.
    /// </summary>
    public void KeepTopmost(bool defer = true)
    {
        if (!AppWindow.IsVisible)
        {
            return;
        }

        nint hwnd = NoActivateWindow.GetHwnd(this);
        NativeMethods.AssertTopmost(hwnd);
        InputTargetGuard.RestoreIfStolen();
        if (!defer)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (AppWindow.IsVisible)
            {
                NativeMethods.AssertTopmost(NoActivateWindow.GetHwnd(this));
                InputTargetGuard.RestoreIfStolen();
            }
        });
    }

    private void OnKeyboardActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated)
        {
            InputTargetGuard.RestoreIfStolen();
        }
    }

    private static void OnRootGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        args.TryCancel();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_windowDragging || !AppWindow.IsVisible)
        {
            return;
        }

        if (args.DidPresenterChange || args.DidVisibilityChange)
        {
            ConfigurePresenter();
            KeepTopmost();
            return;
        }

        if (args.DidPositionChange || args.DidSizeChange)
        {
            nint hwnd = NoActivateWindow.GetHwnd(this);
            if (!NativeMethods.IsTopmost(hwnd))
            {
                NativeMethods.AssertTopmost(hwnd);
            }
        }
    }

    private void OnTopmostWatchTick(object sender, object e)
    {
        if (_windowDragging || !AppWindow.IsVisible)
        {
            return;
        }

        nint hwnd = NoActivateWindow.GetHwnd(this);
        if (!NativeMethods.IsTopmost(hwnd))
        {
            ConfigurePresenter();
            NativeMethods.AssertTopmost(hwnd);
        }
    }

    private void ApplyAppearance()
    {
        ElementTheme theme = Settings.Theme == "Light" ? ElementTheme.Light : ElementTheme.Dark;
        RootGrid.RequestedTheme = theme;

        _letterBrush = ResourceBrush("ControlFillColorDefaultBrush", Color.FromArgb(46, 255, 255, 255));
        _funcBrush = ResourceBrush("ControlFillColorSecondaryBrush", Color.FromArgb(22, 255, 255, 255));
        _pressedBrush = ResourceBrush("ControlFillColorTertiaryBrush", Color.FromArgb(85, 255, 255, 255));
        _accentBrush = ResourceBrush("AccentFillColorDefaultBrush", Colors.SlateBlue);
        // Hairline Fluent edge: low-contrast 1 px, not a chunky box.
        _outlineBrush = Settings.Theme == "Light"
            ? new SolidColorBrush(Color.FromArgb(40, 0, 0, 0))
            : new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));

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
        int x;
        int y;
        if (!_initialPlacementDone)
        {
            x = work.X + Math.Max(0, (work.Width - width) / 2);
            y = work.Y + Math.Max(0, work.Height - height - DipToPixels(hwnd, 16));
            _initialPlacementDone = true;
        }
        else
        {
            x = AppWindow.Position.X;
            y = AppWindow.Position.Y;
            x = Math.Clamp(x, work.X, Math.Max(work.X, work.X + work.Width - width));
            y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Y + work.Height - height));
        }

        NativeMethods.MoveResizeNoActivate(hwnd, x, y, width, height);
        KeepTopmost(defer: false);
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
        DropShiftHold(cancelLatch: true);
        KeysHost.Children.Clear();
        _letterKeyBorders.Clear();
        _allKeyBorders.Clear();
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
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(1.5),
            BorderThickness = Settings.ShowKeyOutlines ? new Thickness(1) : new Thickness(0),
            BorderBrush = Settings.ShowKeyOutlines ? _outlineBrush : new SolidColorBrush(Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = BuildKeyContent(key, primaryFont, secondaryFont),
            Tag = new KeyContext(key, baseBrush),
        };

        _allKeyBorders.Add(border);
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
        if (key.Kind == KeyKind.Shift && _layout.IsUpper)
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
            case KeyKind.Character when key.Character is char c:
                return ShiftChord.Resolve(c, key.SecondaryGlyph, _layout.IsUpper).ToString();
            default:
                return key.DisplayLabel;
        }
    }

    // --- Press handling ----------------------------------------------------

    private void OnKeyPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var border = (Border)sender;
        var context = (KeyContext)border.Tag;
        uint id = e.Pointer.PointerId;
        if (!SwipeInjectPolicy.AllowLetterPress(id, _endedSwipePointerId))
        {
            e.Handled = true;
            return;
        }

        uint? primaryId = _activeBorder is null ? null : _activePointerId;

        KeyPressAction action = KeyPointerPolicy.OnPressed(
            id,
            isShiftKey: context.Key.Kind == KeyKind.Shift,
            primaryPointerId: primaryId,
            shiftHoldPointerId: _shiftHoldPointerId,
            swiping: _swiping);

        switch (action)
        {
            case KeyPressAction.Ignore:
                e.Handled = true;
                return;
            case KeyPressAction.BeginShiftHold:
                BeginShiftHold(border, e);
                return;
            case KeyPressAction.CancelPrimaryThenBegin:
                ResetPress(commit: false);
                BeginPrimaryPress(border, e);
                return;
            default:
                BeginPrimaryPress(border, e);
                return;
        }
    }

    private void BeginShiftHold(Border border, PointerRoutedEventArgs e)
    {
        _shiftHoldPointerId = e.Pointer.PointerId;
        _shiftHoldBorder = border;
        _layout.BeginShiftHold();
        if (_activeKey is { Kind: KeyKind.Character })
        {
            _layout.MarkShiftModifierUsed();
            _pressShifted = true;
        }

        border.CapturePointer(e.Pointer);
        border.Background = _pressedBrush;
        RefreshKeyAppearance();
        SyncInputContact();
        e.Handled = true;
    }

    private void BeginPrimaryPress(Border border, PointerRoutedEventArgs e)
    {
        var context = (KeyContext)border.Tag;

        InputTargetGuard.NoteTarget();

        _activeBorder = border;
        _activeKey = context.Key;
        _activePointerId = e.Pointer.PointerId;
        _pressShifted = _layout.IsUpper;
        _repeatFired = false;
        _popupShown = false;
        _spaceHandled = false;
        _wordDeleted = false;
        _caretMode = false;
        _caretAccum = 0;
        _spacePressedAt = DateTime.UtcNow;

        if (_layout.ShiftHeld && context.Key.Kind == KeyKind.Character)
        {
            _layout.MarkShiftModifierUsed();
        }

        Point p = e.GetCurrentPoint(RootGrid).Position;
        _pressStartX = p.X;
        _lastWordX = p.X;
        _lastCaretX = p.X;
        _swipeStartPoint = p;
        _swipeStartKey = context.Key;
        _pendingSwipePoints.Clear();
        _pendingSwipePoints.Add(p);
        _swipeStartBoundsValid = false;
        if (context.Key.Kind == KeyKind.Character
            && context.Key.Character is char startLetter
            && char.IsLetter(startLetter))
        {
            CaptureStartKeyBounds(startLetter);
        }

        _diagCaptureLostThisGesture = 0;
        _diagTrailClearedMidGesture = false;
        _diagGesturePointerId = _activePointerId;

        try
        {
            border.CapturePointer(e.Pointer);
        }
        catch (UnauthorizedAccessException)
        {
            TryRecapturePointer(e);
        }

        border.Background = _pressedBrush;
        SyncInputContact();

        if (context.Key.Kind != KeyKind.Shift)
        {
            StartPressTimer(context.Key);
        }

        e.Handled = true;
    }

    private void DropShiftHold(bool cancelLatch)
    {
        Border? border = _shiftHoldBorder;
        _shiftHoldPointerId = null;
        _shiftHoldBorder = null;
        if (cancelLatch)
        {
            _layout.CancelShiftHold();
        }

        if (border is null)
        {
            return;
        }

        if (border.Tag is KeyContext context)
        {
            border.Background = BaseBrushFor(context.Key);
        }

        SyncInputContact();
    }

    private void FinishShiftHold(bool commit)
    {
        if (_shiftHoldPointerId is null)
        {
            return;
        }

        DropShiftHold(cancelLatch: false);
        _layout.EndShiftHold(commit);
        RefreshKeyAppearance();
        SyncInputContact();
        FlushDeferredOverlayWork();
    }

    private void RefreshKeyAppearance()
    {
        foreach (Border border in _allKeyBorders)
        {
            if (border.Tag is not KeyContext context)
            {
                continue;
            }

            if (border == _activeBorder || border == _shiftHoldBorder)
            {
                border.Background = _pressedBrush;
            }
            else
            {
                border.Background = BaseBrushFor(context.Key);
            }

            if (border.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is TextBlock label)
            {
                label.Text = PrimaryLabel(context.Key);
            }
        }
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
        else if (key.Repeatable
            && Settings.KeyRepeatEnabled
            && SwipeInjectPolicy.AllowLetterKeyRepeat(IsSwipeableLetter(key)))
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
        if (_activeKey is null)
        {
            return;
        }

        InjectForKey(_activeKey);
    }

    private void OnKeyPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        TrackPrimaryPointer(e);
        e.Handled = true;
    }

    private void OnRootSessionPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        TrackPrimaryPointer(e);
    }

    private void TrackPrimaryPointer(PointerRoutedEventArgs e)
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

        if (_activeKey.Kind == KeyKind.Character
            && _activeKey.Character is char letter
            && char.IsLetter(letter))
        {
            // Always keep the first PointerMoved — never abort it.
            _pendingSwipePoints.Add(p);
            double keyW = _letterKeySize > 1 ? _letterKeySize : BaseKeyHeight * Scale;
            var origin = new Point2(_swipeStartPoint.X, _swipeStartPoint.Y);
            var current = new Point2(p.X, p.Y);
            if (!GestureStart.IsJitter(origin, current, keyW))
            {
                // Moving: this is not a long-press. Tap vs swipe decided on leave.
                _pressTimer.Stop();
            }

            if (Settings.SwipeEnabled && KeyPointerPolicy.AllowSwipeLatch(_layout.ShiftHeld))
            {
                Rect2 startRect = _swipeStartBoundsValid
                    ? _swipeStartKeyBounds
                    : new Rect2(origin.X - (keyW / 2), origin.Y - (keyW / 2), keyW, keyW);
                if (GestureStart.ShouldLatch(origin, current, startRect, keyW))
                {
                    BeginSwipe();
                    TryRecapturePointer(e);
                    for (int i = 1; i < _pendingSwipePoints.Count; i++)
                    {
                        AppendSwipePoint(_pendingSwipePoints[i]);
                    }
                }
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
                _typedWord = string.Empty;
                _prevWord = null;
                _swipeCommit.OnOtherCommit();
                _lastWordX = x;
            }
        }
    }

    private void OnKeyPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        HandlePointerEnd(e.Pointer.PointerId, commit: true);
        e.Handled = true;
    }

    private void OnRootSessionPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        HandlePointerEnd(e.Pointer.PointerId, commit: true);
        e.Handled = true;
    }

    private void OnKeyPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        HandlePointerEnd(e.Pointer.PointerId, commit: false);
        e.Handled = true;
    }

    private void OnRootSessionPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        HandlePointerEnd(e.Pointer.PointerId, commit: false);
        e.Handled = true;
    }

    private void OnKeyPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        HandleCaptureLost(e);
        e.Handled = true;
    }

    private void OnRootSessionPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        HandleCaptureLost(e);
        e.Handled = true;
    }

    private void HandleCaptureLost(PointerRoutedEventArgs e)
    {
        if (_shiftHoldPointerId == e.Pointer.PointerId)
        {
            if (IsPointerInContact(e) && _shiftHoldBorder is not null)
            {
                try
                {
                    _shiftHoldBorder.CapturePointer(e.Pointer);
                }
                catch (UnauthorizedAccessException)
                {
                    // Hold visuals stay; EndShiftHold still runs on release.
                }

                return;
            }

            FinishShiftHold(commit: false);
            return;
        }

        bool session = _activeBorder is not null && e.Pointer.PointerId == _activePointerId;
        bool down = IsPointerInContact(e);
        SwipeContactAction action = SwipeContactPolicy.OnEndSignal(
            SwipeContactSignal.CaptureLost,
            session,
            down);
        if (action == SwipeContactAction.Continue)
        {
            _diagCaptureLostThisGesture++;
            TryRecapturePointer(e);
            return;
        }

        if (action == SwipeContactAction.EndCancel)
        {
            _diagCaptureLostThisGesture++;
            HandlePointerEnd(e.Pointer.PointerId, commit: false);
        }
    }

    private void TryRecapturePointer(PointerRoutedEventArgs e)
    {
        UIElement target = (UIElement?)_activeBorder ?? RootGrid;
        try
        {
            target.CapturePointer(e.Pointer);
        }
        catch (UnauthorizedAccessException)
        {
            try
            {
                RootGrid.CapturePointer(e.Pointer);
            }
            catch (UnauthorizedAccessException)
            {
                // Moves may still arrive on whichever key is under the finger.
            }
        }
    }

    private static bool IsPointerInContact(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(null);
        return point.IsInContact || point.Properties.IsLeftButtonPressed;
    }

    private bool IsPointerSessionActive => _activeBorder is not null || _swiping;

    private bool ShouldDeferOverlay => IsPointerSessionActive || _shiftHoldPointerId is not null;

    private void SyncInputContact() =>
        InputTargetGuard.SetContactDown(
            _activeBorder is not null || _swiping || _shiftHoldPointerId is not null);

    private void OnLayoutChanged()
    {
        if (ShouldDeferOverlay)
        {
            _deferKeyboardRender = true;
            return;
        }

        RenderKeyboard();
    }

    private static bool IsSwipeableLetter(KeyDefinition key) =>
        key.Kind == KeyKind.Character && key.Character is char c && char.IsLetter(c);

    private void HandlePointerEnd(uint pointerId, bool commit)
    {
        if (_shiftHoldPointerId == pointerId)
        {
            FinishShiftHold(commit);
            return;
        }

        if (_activeBorder is not null && pointerId == _activePointerId)
        {
            ResetPress(commit);
        }
    }

    private void ResetPress(bool commit)
    {
        Border? border = _activeBorder;
        KeyDefinition? key = _activeKey;
        if (border is null || key is null)
        {
            return;
        }

        _pressTimer.Stop();
        _repeatTimer.Stop();

        if (border.Tag is KeyContext context)
        {
            border.Background = context.BaseBrush;
        }

        bool swipeLatched = _swiping;
        if (swipeLatched)
        {
            _endedSwipePointerId = _activePointerId;
            EndSwipe(commit);
        }
        else
        {
            // This pointer has been released. Allow SendInput to restore the
            // remembered HWND without draining the swipe queue (session is
            // still active until we clear _activeBorder below).
            InputTargetGuard.SetContactDown(_shiftHoldPointerId is not null);
            EmitIncompleteIfUnlatched(commit, key);

            if (_caretMode)
            {
                // Caret already injected on move.
            }
            else if (key.Kind == KeyKind.Space && commit)
            {
                ClearSuggestions();
                TimeSpan held = DateTime.UtcNow - _spacePressedAt;
                if (held.TotalMilliseconds >= Settings.LongPressDelayMs)
                {
                    _swipeCommit.DismissPending();
                    _layout.ToggleLanguage();
                    _settingsService.Update(s => s.LayoutId = _layout.Current.Id);
                }
                else
                {
                    _swipeCommit.OnSpace();
                    CommitTypedAsPrev();
                    KeyboardInjector.InjectCharacter(' ');
                }
            }
            else if (_popupShown)
            {
                if (commit)
                {
                    CommitPopupSelection();
                }

                CloseLongPressPopup();
            }
            else if (commit && !_wordDeleted && !_repeatFired && !_spaceHandled
                && SwipeInjectPolicy.AllowLetterPress(_activePointerId, _endedSwipePointerId))
            {
                PerformTap(key);
            }
        }

        _activeBorder = null;
        _activeKey = null;
        _pressMode = PressMode.None;
        _repeatFired = false;
        SyncInputContact();
        FlushDeferredOverlayWork();
        ScheduleDecodedInject();
    }

    private void PerformTap(KeyDefinition key)
    {
        switch (key.Kind)
        {
            case KeyKind.Shift:
                _layout.CycleShift();
                break;
            case KeyKind.Backspace:
                if (TryUndoLastSwipeCommit())
                {
                    ClearSuggestions();
                    break;
                }

                ClearSuggestions();
                _swipeCommit.DismissPending();
                TrimTypedWord();
                KeyboardInjector.InjectBackspace();
                break;
            case KeyKind.Enter:
                ClearSuggestions();
                _swipeCommit.OnEnter();
                _prevWord = null;
                _typedWord = string.Empty;
                KeyboardInjector.InjectEnter();
                break;
            case KeyKind.Symbols:
                _layout.ToggleSymbols();
                RelayoutWindow();
                break;
            case KeyKind.Emoji:
                ClearSuggestions();
                _swipeCommit.OnOtherCommit();
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
                if (TryUndoLastSwipeCommit())
                {
                    ClearSuggestions();
                    break;
                }

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
        if (!SwipeInjectPolicy.AllowLetterPress(_activePointerId, _endedSwipePointerId))
        {
            return;
        }

        if (key.Character is not char c)
        {
            return;
        }

        ClearSuggestions();
        _swipeCommit.OnTyped(c);

        bool shifted = _pressShifted || _layout.IsUpper;
        c = ShiftChord.Resolve(c, key.SecondaryGlyph, shifted);

        if (char.IsLetter(c))
        {
            _typedWord += char.ToLowerInvariant(c);
        }
        else
        {
            _typedWord = string.Empty;
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
                CornerRadius = new CornerRadius(3),
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
        bool shifted = _pressShifted || _layout.IsUpper;
        if (shifted && glyph.Length == 1 && char.IsLetter(glyph[0]))
        {
            glyph = glyph.ToUpperInvariant();
        }

        ClearSuggestions();
        _swipeCommit.OnOtherCommit();
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
            _swipeCommit.DismissPending();
            ClearSuggestions();
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
        _pressMode = PressMode.None;
        _repeatFired = false;
        _popupShown = false;
        _diagGesturePointerId = _activePointerId;
        long now = Environment.TickCount64;
        _diagTimeSincePreviousMs = _diagLastSwipeEndTick == 0
            ? null
            : now - _diagLastSwipeEndTick;

        BuildLetterHitboxes();

        _swipePoints.Clear();
        _swipeChars.Clear();
        _swipeTimesMs.Clear();
        _swipeStartTick = Environment.TickCount64;
        _swipePoints.Add(_swipeStartPoint);
        _swipeTimesMs.Add(0);
        if (_swipeStartKey?.Character is char c && char.IsLetter(c))
        {
            _swipeChars.Add(char.ToLowerInvariant(c));
        }

        ClearSwipeTrail(expected: true);
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

        var centers = new Dictionary<char, Point2>();
        foreach ((char letter, Border border) in _letterKeyBorders)
        {
            if (border.ActualWidth <= 0 || border.ActualHeight <= 0)
            {
                continue;
            }

            // Transform BOTH corners into RootGrid space (same as GetCurrentPoint).
            // Using a transformed origin with unscaled ActualWidth breaks after SizeScale.
            GeneralTransform transform = border.TransformToVisual(RootGrid);
            Point topLeft = transform.TransformPoint(new Point(0, 0));
            Point bottomRight = transform.TransformPoint(new Point(border.ActualWidth, border.ActualHeight));
            Rect2 visual = Rect2.FromCorners(
                new Point2(topLeft.X, topLeft.Y),
                new Point2(bottomRight.X, bottomRight.Y));
            if (visual.Width <= 0 || visual.Height <= 0)
            {
                continue;
            }

            // Same space as the swipe trail (RootGrid / GetCurrentPoint).
            // Slight inset so a gap between keys is not a hit; the visible
            // key face still counts so “trail on M” == decoder saw M.
            Rect2 hit = visual.InsetFraction(0.08);
            var bounds = new Rect(hit.X, hit.Y, hit.Width, hit.Height);
            var center = new Point(visual.Center.X, visual.Center.Y);

            _letterBoxes.Add(new LetterBox(letter, bounds, center));
            _letterCenters[letter] = center;
            centers[letter] = visual.Center;
        }

        _letterKeySize = centers.Count >= 2
            ? SwipeGeometry.KeyPitch(centers)
            : BaseKeyHeight * Scale;
    }

    private void CaptureStartKeyBounds(char letter)
    {
        BuildLetterHitboxes();
        char folded = char.ToLowerInvariant(letter);
        foreach (LetterBox box in _letterBoxes)
        {
            if (box.Letter == folded)
            {
                _swipeStartKeyBounds = new Rect2(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
                _swipeStartBoundsValid = true;
                return;
            }
        }

        _swipeStartBoundsValid = false;
    }

    private void AppendSwipePoint(Point p)
    {
        if (_swipePoints.Count > 0)
        {
            Point last = _swipePoints[^1];
            if (last.X == p.X && last.Y == p.Y)
            {
                return;
            }
        }

        _swipePoints.Add(p);
        _swipeTimesMs.Add(Environment.TickCount64 - _swipeStartTick);
        _swipeTrail?.Points.Add(p);

        char c = HitTestLetter(p);
        if (c != '\0' && (_swipeChars.Count == 0 || _swipeChars[^1] != c))
        {
            _swipeChars.Add(c);
        }
    }

    private char HitTestLetter(Point p)
    {
        var keys = new List<(char Letter, Rect2 Bounds)>(_letterBoxes.Count);
        foreach (LetterBox box in _letterBoxes)
        {
            keys.Add((box.Letter, new Rect2(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height)));
        }

        return SwipeGeometry.HitTest(new Point2(p.X, p.Y), keys);
    }

    private void ClearSwipeTrail(bool expected)
    {
        if (_swiping && !expected && _swipePoints.Count > 1)
        {
            _diagTrailClearedMidGesture = true;
        }

        SwipeTrail.Children.Clear();
        _swipeTrail = null;
    }

    private DiagChainSnap SnapshotDiagChain(bool aborted, bool assignOrdinal, string? abortReason = null)
    {
        int ordinal = 0;
        if (assignOrdinal)
        {
            _diagGestureOrdinal++;
            ordinal = _diagGestureOrdinal;
        }

        _diagLastSwipeEndTick = Environment.TickCount64;
        return new DiagChainSnap(
            ordinal,
            _diagCaptureLostThisGesture,
            Math.Max(_swipePoints.Count, _swipeTrail?.Points.Count ?? 0),
            aborted,
            _diagTimeSincePreviousMs,
            _diagGesturePointerId,
            _diagTrailClearedMidGesture,
            abortReason);
    }

    private readonly record struct DiagChainSnap(
        int GestureOrdinal,
        int CaptureLostCount,
        int TrailPointCount,
        bool GestureAborted,
        double? TimeSincePreviousSwipeMs,
        uint PointerId,
        bool TrailClearedMidGesture,
        string? AbortReason);

    private void EndSwipe(bool commit)
    {
        bool abort = !commit;
        var path = _swipePoints.Select(pt => new Point2(pt.X, pt.Y)).ToList();
        var times = _swipeTimesMs.ToList();
        var hits = _swipeChars.ToList();
        var centers = _letterCenters.ToDictionary(kv => kv.Key, kv => new Point2(kv.Value.X, kv.Value.Y));
        Action<SwipeGestureCapture>? sink = _diagSink;
        string layoutLabel = DiagnosticLayoutLabel;
        double keyboardScale = DiagnosticKeyboardScale;
        double keySize = _letterKeySize;
        bool capture = sink is not null;
        if (abort && _diagCaptureLostThisGesture > 0)
        {
            _diagTrailClearedMidGesture = true;
        }

        DiagChainSnap chain = default;

        _swiping = false;
        ClearSwipeTrail(expected: true);
        InputTargetGuard.NoteTarget();

        if (!commit)
        {
            if (capture)
            {
                string reason = _diagCaptureLostThisGesture > 0
                    ? SwipeAbortReason.CaptureLost
                    : _diagTrailClearedMidGesture
                        ? SwipeAbortReason.TrailCleared
                        : SwipeAbortReason.Canceled;
                chain = SnapshotDiagChain(aborted: true, assignOrdinal: true, reason);
                EmitDiagCapture(
                    sink!,
                    path,
                    times,
                    hits,
                    centers,
                    [],
                    chosen: null,
                    layoutLabel,
                    keyboardScale,
                    keySize,
                    decodeMs: null,
                    chain);
            }

            ClearSwipeBuffers();
            return;
        }

        if (_swipeChars.Count < 2
            && !GestureStart.IsCommittedGesture(
                path,
                _letterKeySize > 1 ? _letterKeySize : BaseKeyHeight * Scale))
        {
            if (capture)
            {
                chain = SnapshotDiagChain(aborted: true, assignOrdinal: true, SwipeAbortReason.TooShort);
                EmitDiagCapture(
                    sink!,
                    path,
                    times,
                    hits,
                    centers,
                    [],
                    chosen: null,
                    layoutLabel,
                    keyboardScale,
                    keySize,
                    decodeMs: null,
                    chain);
            }

            ClearSwipeBuffers();
            return;
        }

        chain = SnapshotDiagChain(aborted: false, assignOrdinal: capture);
        _swipeDecodeSerial++;
        int serial = _swipeDecodeSerial;
        bool upper = _layout.IsUpper;
        string? previousWord = _prevWord;
        WordListService lists = _wordLists;

        ClearSwipeBuffers();

        _ = Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            IReadOnlyList<ExplainedSwipe> explained;
            try
            {
                WordList words = lists.ForSwipe();
                LanguageModel language = lists.LanguageForSwipe();
                explained = SwipeDecoder.Explain(
                    hits,
                    path,
                    centers,
                    words,
                    keySize,
                    previousWord: previousWord,
                    language: language,
                    includeBreakdown: capture);
            }
            catch
            {
                explained = [];
            }

            clock.Stop();
            double decodeMs = clock.Elapsed.TotalMilliseconds;
            DispatcherQueue.TryEnqueue(() => ApplySwipeDecode(
                serial,
                explained,
                capture,
                path,
                times,
                hits,
                centers,
                sink,
                layoutLabel,
                keyboardScale,
                keySize,
                upper,
                decodeMs,
                chain));
        });
    }

    private void ApplySwipeDecode(
        int serial,
        IReadOnlyList<ExplainedSwipe> explained,
        bool capture,
        List<Point2> path,
        List<long> times,
        List<char> hits,
        Dictionary<char, Point2> centers,
        Action<SwipeGestureCapture>? sink,
        string layoutLabel,
        double keyboardScale,
        double keySize,
        bool upper,
        double decodeMs,
        DiagChainSnap chain)
    {
        // Successful completes emit diag and enqueue inject together so the
        // field matches the capture. Drain is a separate queue: it never
        // mutates pointer ownership and runs only when no contact is active.
        if (capture && sink is not null)
        {
            EmitDiagCapture(
                sink,
                path,
                times,
                hits,
                centers,
                explained,
                explained.Count > 0 ? explained[0].Word : null,
                layoutLabel,
                keyboardScale,
                keySize,
                decodeMs,
                chain);
        }

        var candidates = new string[explained.Count];
        for (int i = 0; i < explained.Count; i++)
        {
            candidates[i] = explained[i].Word;
        }

        _readySwipeInjects[serial] = new SwipeInjectReady(candidates, upper);
        DrainSwipeInjectsIfIdle();
    }

    private void ScheduleDecodedInject() =>
        DispatcherQueue.TryEnqueue(DrainSwipeInjectsIfIdle);

    private void DrainSwipeInjectsIfIdle()
    {
        if (!SwipeInjectPolicy.AllowDecodedInject(IsPointerSessionActive, InputTargetGuard.ContactDown))
        {
            return;
        }

        bool drained = false;
        while (_readySwipeInjects.Remove(_appliedSwipeInjectSerial + 1, out SwipeInjectReady ready))
        {
            drained = true;
            _appliedSwipeInjectSerial++;
            if (ready.Candidates.Length == 0)
            {
                FinishSwipeDecodeOverlay(null);
                continue;
            }

            InjectSwipeWord(ready.Candidates[0], ready.Upper);
            FinishSwipeDecodeOverlay(ready.Candidates);
        }

        if (drained)
        {
            _endedSwipePointerId = null;
        }
    }

    private void EmitDiagCapture(
        Action<SwipeGestureCapture> sink,
        IReadOnlyList<Point2> path,
        IReadOnlyList<long> times,
        IReadOnlyList<char> hits,
        Dictionary<char, Point2> centers,
        IReadOnlyList<ExplainedSwipe> explained,
        string? chosen,
        string layoutLabel,
        double keyboardScale,
        double keySize,
        double? decodeMs,
        DiagChainSnap chain)
    {
        sink(new SwipeGestureCapture
        {
            PathDip = path,
            PathElapsedMs = times,
            HitKeys = hits,
            Candidates = explained,
            Chosen = chosen,
            Layout = layoutLabel,
            KeyboardScale = keyboardScale,
            PitchDip = keySize > 1 ? keySize : BaseKeyHeight * Scale,
            CentersDip = centers,
            TimestampUtc = DateTimeOffset.UtcNow,
            DecodeMs = decodeMs,
            GestureOrdinal = chain.GestureOrdinal,
            CaptureLostCount = chain.CaptureLostCount,
            TrailPointCount = chain.TrailPointCount,
            GestureAborted = chain.GestureAborted,
            TimeSincePreviousSwipeMs = chain.TimeSincePreviousSwipeMs,
            PointerId = chain.PointerId,
            TrailClearedMidGesture = chain.TrailClearedMidGesture,
            AbortReason = chain.AbortReason,
        });
    }

    private void EmitIncompleteIfUnlatched(bool commit, KeyDefinition key)
    {
        Action<SwipeGestureCapture>? sink = _diagSink;
        if (sink is null)
        {
            return;
        }

        if (key.Kind != KeyKind.Character
            || key.Character is not char letter
            || !char.IsLetter(letter))
        {
            return;
        }

        double keyW = _letterKeySize > 1 ? _letterKeySize : BaseKeyHeight * Scale;
        bool moved = _pendingSwipePoints.Count >= 2
            && !GestureStart.IsJitter(
                new Point2(_swipeStartPoint.X, _swipeStartPoint.Y),
                new Point2(_pendingSwipePoints[^1].X, _pendingSwipePoints[^1].Y),
                keyW);
        if (!moved && _diagCaptureLostThisGesture == 0)
        {
            return;
        }

        string reason = _diagCaptureLostThisGesture > 0
            ? SwipeAbortReason.CaptureLost
            : commit ? SwipeAbortReason.NeverLatched : SwipeAbortReason.Canceled;
        var path = _pendingSwipePoints.Select(pt => new Point2(pt.X, pt.Y)).ToList();
        var centers = _letterCenters.ToDictionary(kv => kv.Key, kv => new Point2(kv.Value.X, kv.Value.Y));
        DiagChainSnap chain = SnapshotDiagChain(aborted: true, assignOrdinal: true, reason);
        EmitDiagCapture(
            sink,
            path,
            [],
            char.IsLetter(letter) ? [char.ToLowerInvariant(letter)] : [],
            centers,
            [],
            chosen: null,
            DiagnosticLayoutLabel,
            DiagnosticKeyboardScale,
            keyW,
            decodeMs: null,
            chain);
    }

    private void FinishSwipeDecodeOverlay(string[]? candidates)
    {
        if (!SwipeContactPolicy.AllowOverlayMutation(ShouldDeferOverlay))
        {
            if (candidates is not null)
            {
                _deferredSuggestionWords = candidates;
            }

            return;
        }

        if (candidates is not null)
        {
            RebuildSuggestionBar(candidates);
        }

        InputTargetGuard.RestoreIfStolen();
    }

    private void FlushDeferredOverlayWork()
    {
        if (ShouldDeferOverlay)
        {
            return;
        }

        if (_deferKeyboardRender)
        {
            _deferKeyboardRender = false;
            string[]? chips = _deferredSuggestionWords;
            _deferredSuggestionWords = null;
            RenderKeyboard();
            if (chips is not null)
            {
                RebuildSuggestionBar(chips);
            }

            return;
        }

        if (_deferredSuggestionWords is not null)
        {
            string[] chips = _deferredSuggestionWords;
            _deferredSuggestionWords = null;
            RebuildSuggestionBar(chips);
        }
    }

    private void ClearSwipeBuffers()
    {
        _swipePoints.Clear();
        _swipeChars.Clear();
        _swipeTimesMs.Clear();
        _pendingSwipePoints.Clear();
    }

    private void InjectSwipeWord(string word, bool? upper = null)
    {
        bool makeUpper = upper ?? _layout.IsUpper;
        string text = makeUpper && word.Length > 0
            ? char.ToUpperInvariant(word[0]) + word[1..]
            : word;

        string injected = _swipeCommit.PlanSwipeInject(text);
        KeyboardInjector.InjectSwipeText(injected);
        _layout.ConsumeShift();
        _swipeCommit.CommitSwipe(injected);
        RememberCommittedWord(word);
        _typedWord = string.Empty;
    }

    private bool TryUndoLastSwipeCommit()
    {
        if (!_swipeCommit.TryConsumeUndo(out int count) || count <= 0)
        {
            return false;
        }

        KeyboardInjector.InjectBackspaces(count);
        return true;
    }

    private void RebuildSuggestionBar(IReadOnlyList<string>? candidates = null)
    {
        // Chip order is decoder order (index 0 was injected). Do not re-sort.
        // Borders, not Buttons: destroying a focused Button after each swipe
        // walks focus onto the overlay and steals the target app.
        SuggestionBar.Children.Clear();
        SuggestionBar.Children.Add(BuildClipboardButton());

        if (candidates is null)
        {
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            string word = candidates[i];
            var label = new TextBlock
            {
                Text = word,
                FontSize = 15 * Scale,
                FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false,
            };
            var chip = new Border
            {
                Child = label,
                Tag = word,
                Height = 28 * Scale,
                MinWidth = 0,
                Padding = new Thickness(14, 0, 14, 0),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Background = i == 0 ? _funcBrush : new SolidColorBrush(Colors.Transparent),
            };
            chip.PointerPressed += OnSuggestionPointerPressed;
            SuggestionBar.Children.Add(chip);
        }
    }

    private Border BuildClipboardButton()
    {
        var button = new Border
        {
            Child = new TextBlock
            {
                Text = "📋",
                FontSize = 16 * Scale,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            },
            Width = 36,
            Height = 28 * Scale,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        ToolTipService.SetToolTip(button, "MyClipboard");
        button.PointerPressed += OnClipboardPointerPressed;
        return button;
    }

    private void OnClipboardPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        OpenClipboardPanel();
    }

    private void OpenClipboardPanel()
    {
        EmojiOverlay.Visibility = Visibility.Collapsed;
        ClipsAuthorizeHint.Visibility = Visibility.Collapsed;
        PopulateClipsPanel(force: true);
        ClipsOverlay.Visibility = Visibility.Visible;
        StartClipsWatch();
    }

    private void OnCloseClipsClicked(object sender, RoutedEventArgs e) => HideClipsPanel();

    private void HideClipsPanel()
    {
        StopClipsWatch();
        ClipsOverlay.Visibility = Visibility.Collapsed;
    }

    private void RefreshClipsPanelIfOpen()
    {
        if (ClipsOverlay.Visibility != Visibility.Visible)
        {
            StopClipsWatch();
            return;
        }

        if (_clipsWatcher is null)
        {
            AttachClipsWatcher();
        }

        PopulateClipsPanel(force: false);
    }

    private void StartClipsWatch()
    {
        _clipsWatchTimer.Start();
        AttachClipsWatcher();
    }

    private void StopClipsWatch()
    {
        _clipsWatchTimer.Stop();
        DetachClipsWatcher();
    }

    private void AttachClipsWatcher()
    {
        DetachClipsWatcher();
        string dir = MyClipboardContract.GetPreferredDirectory();
        if (!Directory.Exists(dir))
        {
            return;
        }

        try
        {
            _clipsWatcher = new FileSystemWatcher(dir)
            {
                Filter = MyClipboardContract.FileName,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _clipsWatcher.Changed += OnClipsFileChanged;
            _clipsWatcher.Created += OnClipsFileChanged;
            _clipsWatcher.Deleted += OnClipsFileChanged;
            _clipsWatcher.Renamed += OnClipsFileChanged;
        }
        catch
        {
            DetachClipsWatcher();
        }
    }

    private void DetachClipsWatcher()
    {
        if (_clipsWatcher is null)
        {
            return;
        }

        _clipsWatcher.EnableRaisingEvents = false;
        _clipsWatcher.Changed -= OnClipsFileChanged;
        _clipsWatcher.Created -= OnClipsFileChanged;
        _clipsWatcher.Deleted -= OnClipsFileChanged;
        _clipsWatcher.Renamed -= OnClipsFileChanged;
        _clipsWatcher.Dispose();
        _clipsWatcher = null;
    }

    private void OnClipsFileChanged(object sender, FileSystemEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => RefreshClipsPanelIfOpen());
    }

    private void PopulateClipsPanel(bool force)
    {
        ClipSnapshot snapshot = _clips.GetSnapshot(12);
        if (!force && snapshot.Fingerprint == _lastClipsFingerprint)
        {
            return;
        }

        _lastClipsFingerprint = snapshot.Fingerprint;
        ClipsList.Children.Clear();

        bool needsAccess = snapshot.Status is ClipFileStatus.Missing
            or ClipFileStatus.Unauthorized
            or ClipFileStatus.Empty
            or ClipFileStatus.Invalid;
        ClipsAuthorizeButton.Visibility = needsAccess ? Visibility.Visible : Visibility.Collapsed;

        switch (snapshot.Status)
        {
            case ClipFileStatus.Missing:
                ClipsEmpty.Visibility = Visibility.Visible;
                ClipsEmpty.Text =
                    "WinBoard n’a pas encore accès à MyClipboard (fichier d’intégration absent). "
                    + "Ce n’est pas un crash WinBoard.\n\n"
                    + "Chemin attendu (casse MyClipBoard uniquement sur le suffixe) :\n"
                    + snapshot.PreferredPath
                    + "\n"
                    + snapshot.DebugExistenceLine
                    + "\n\nAppuyez sur « Demander l’accès à MyClipboard » pour ouvrir "
                    + "myclipboard://authorize-winboard. 100 % local, pas de SQLite, pas de réseau.";
                return;
            case ClipFileStatus.Unauthorized:
                ClipsEmpty.Visibility = Visibility.Visible;
                ClipsEmpty.Text =
                    "MyClipboard a écrit le fichier mais WinBoard n’est pas autorisé (authorized: false).\n\n"
                    + snapshot.DebugExistenceLine
                    + "\n\nDemandez l’accès pour que MyClipboard passe authorized à true.";
                return;
            case ClipFileStatus.Invalid:
                ClipsEmpty.Visibility = Visibility.Visible;
                ClipsEmpty.Text =
                    "Fichier trouvé mais illisible (schéma version 1 attendu).\n"
                    + snapshot.DebugExistenceLine
                    + (string.IsNullOrEmpty(snapshot.ParseHint) ? "" : "\n" + snapshot.ParseHint)
                    + "\nJSON UTF-8 : { \"version\": 1, \"updatedAtMs\": 0, \"authorized\": true, \"clips\": [ { \"id\": \"…\", \"text\": \"…\", \"type\": \"text\", \"updatedAtMs\": 0 } ] }";
                return;
            case ClipFileStatus.Empty:
                ClipsEmpty.Visibility = Visibility.Visible;
                ClipsEmpty.Text =
                    "Accès OK, mais aucun extrait pour l’instant.\n"
                    + snapshot.DebugExistenceLine;
                return;
        }

        if (snapshot.Clips.Count == 0)
        {
            ClipsEmpty.Visibility = Visibility.Visible;
            ClipsEmpty.Text = "Aucun extrait dans MyClipboard.";
            ClipsAuthorizeButton.Visibility = Visibility.Visible;
            return;
        }

        ClipsEmpty.Visibility = Visibility.Collapsed;
        foreach (ClipboardClip clip in snapshot.Clips)
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

    private void OnClipsAuthorizeClicked(object sender, RoutedEventArgs e)
    {
        bool opened = MyClipboardAccess.TryOpenAuthorizeProtocol();
        ClipsAuthorizeHint.Visibility = Visibility.Visible;
        ClipsAuthorizeHint.Text = opened
            ? "MyClipboard devrait s’ouvrir. Autorisez WinBoard, puis les extraits apparaîtront ici (le fichier est relu automatiquement)."
            : "Impossible d’ouvrir le protocole myclipboard://authorize-winboard.\n\n"
              + "Ouvrez MyClipboard Desktop et autorisez WinBoard. Fichier attendu :\n"
              + _clips.PreferredPath
              + "\n100 % local, aucun réseau.";
        AttachClipsWatcher();
        PopulateClipsPanel(force: true);
    }

    private void OnClipRowClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text } || string.IsNullOrEmpty(text))
        {
            return;
        }

        KeyboardInjector.InjectText(text);
        _swipeCommit.OnOtherCommit();
        HideClipsPanel();
    }

    private void OnSuggestionPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string word })
        {
            return;
        }

        int undo = _swipeCommit.PendingCharCount;
        string injected = _swipeCommit.PlanSuggestionReplace(word);
        KeyboardInjector.InjectBackspaces(undo);
        KeyboardInjector.InjectText(injected);
        _swipeCommit.CommitSuggestionReplace(injected);
        RememberCommittedWord(word);
        _typedWord = string.Empty;
        InputTargetGuard.RestoreIfStolen();
    }

    private void ClearSuggestions()
    {
        RebuildSuggestionBar();
    }

    private void RememberCommittedWord(string word)
    {
        string folded = LanguageModel.FoldKey(word);
        _prevWord = folded.Length >= 2 ? folded : null;
    }

    private void CommitTypedAsPrev()
    {
        if (_typedWord.Length >= 2)
        {
            RememberCommittedWord(_typedWord);
        }

        _typedWord = string.Empty;
    }

    private void TrimTypedWord()
    {
        if (_typedWord.Length > 0)
        {
            _typedWord = _typedWord[..^1];
        }
    }

    // --- Emoji panel -------------------------------------------------------

    private void OpenEmojiPanel()
    {
        HideClipsPanel();
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
        _swipeCommit.OnOtherCommit();
        _settingsService.Update(s => s.RecentEmojis = EmojiCatalog.PushRecent(s.RecentEmojis, glyph), notify: false);
        if (_emojiTab == EmojiCatalog.RecentsId)
        {
            RenderEmojiGrid();
        }
    }

    // --- Settings (separate window; keyboard stays visible for live preview) ---

    private void OnSettingsChanged()
    {
        _layout.SetAlphabetic(Settings.LayoutId);
        ConfigurePresenter();
        ApplyAppearance();
        if (ShouldDeferOverlay)
        {
            _deferKeyboardRender = true;
        }
        else
        {
            RenderKeyboard();
        }

        RelayoutWindow();
        KeepTopmost();
    }

    private void OpenSettings()
    {
        EmojiOverlay.Visibility = Visibility.Collapsed;
        HideClipsPanel();
        SettingsWindow.Show(_settingsService, this);
        KeepTopmost();
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>
    /// Settings is a normal activatable window. After it closes, re-assert
    /// WS_EX_NOACTIVATE and HWND_TOPMOST so the keyboard stays above the
    /// target app without keeping foreground focus.
    /// </summary>
    public void RestoreAfterSettings()
    {
        ConfigurePresenter();
        ShowWithoutActivating();
    }

    // --- Top bar / window chrome ------------------------------------------

    private void OnCloseClicked(object sender, RoutedEventArgs e) => HideToTray();

    private void OnCaptionPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        nint hwnd = NoActivateWindow.GetHwnd(this);
        Point local = e.GetCurrentPoint(RootGrid).Position;
        var expectedScreen = new POINT
        {
            X = DipToPixels(hwnd, local.X),
            Y = DipToPixels(hwnd, local.Y),
        };
        NativeMethods.ClientToScreen(hwnd, ref expectedScreen);

        bool mouse = e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse;
        bool nativeTracking = ScreenPointerTracker.TryBegin(
                e.Pointer.PointerId,
                mouse,
                expectedScreen,
                out uint pointerId,
                out POINT screenPoint);
        if (!nativeTracking)
        {
            // The XAML event itself still provides a valid screen point. This
            // keeps touch usable even if WinUI's id is not a Win32 pointer id;
            // PointerMoved drives live movement while capture remains.
            pointerId = e.Pointer.PointerId;
            screenPoint = expectedScreen;
        }

        _windowDragging = true;
        _windowDragMouse = mouse;
        _windowDragNativeTracking = nativeTracking;
        _windowDragPointerId = pointerId;
        _windowDragPointerStart = screenPoint;
        _windowDragPositionStart = AppWindow.Position;
        _windowDragReadMisses = 0;

        // Capture helps regular XAML move/release delivery. Moving the HWND can
        // drop capture on touch; the timer deliberately continues in that case.
        try
        {
            ((UIElement)sender).CapturePointer(e.Pointer);
        }
        catch (UnauthorizedAccessException)
        {
            // Polling screen coordinates does not depend on XAML capture.
        }

        _windowDragTimer.Start();
        e.Handled = true;
    }

    private void OnCaptionPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_windowDragging)
        {
            nint hwnd = NoActivateWindow.GetHwnd(this);
            Point local = e.GetCurrentPoint(RootGrid).Position;
            var screen = new POINT
            {
                X = DipToPixels(hwnd, local.X),
                Y = DipToPixels(hwnd, local.Y),
            };
            if (NativeMethods.ClientToScreen(hwnd, ref screen))
            {
                MoveWindowForDrag(screen);
            }
            else
            {
                UpdateWindowDrag();
            }
        }

        e.Handled = true;
    }

    private void OnWindowDragTimerTick(DispatcherQueueTimer sender, object args)
    {
        UpdateWindowDrag();
    }

    private void UpdateWindowDrag()
    {
        if (!_windowDragging)
        {
            _windowDragTimer.Stop();
            return;
        }

        // When a WinUI id cannot be resolved, XAML PointerMoved remains the
        // live path. Do not cancel it merely because native tracking is absent.
        if (!_windowDragNativeTracking)
        {
            return;
        }

        if (!ScreenPointerTracker.TryTrack(
                _windowDragPointerId,
                _windowDragMouse,
                out POINT current,
                out bool isDown))
        {
            // GetPointerInfo can miss one frame while the XAML island changes
            // target HWND. Do not turn that transient miss into a canceled drag.
            if (++_windowDragReadMisses >= 4)
            {
                StopWindowDrag();
            }

            return;
        }

        _windowDragReadMisses = 0;
        if (!isDown)
        {
            StopWindowDrag();
            return;
        }

        MoveWindowForDrag(current);
    }

    private void MoveWindowForDrag(POINT current)
    {
        NativeMethods.MoveNoActivate(
            NoActivateWindow.GetHwnd(this),
            _windowDragPositionStart.X + (current.X - _windowDragPointerStart.X),
            _windowDragPositionStart.Y + (current.Y - _windowDragPointerStart.Y));
    }

    private void OnCaptionPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        StopWindowDrag();
        if (sender is UIElement element)
        {
            try
            {
                element.ReleasePointerCapture(e.Pointer);
            }
            catch (UnauthorizedAccessException)
            {
                // Capture was already dropped when the HWND moved.
            }
        }

        e.Handled = true;
    }

    private void OnCaptionPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        StopWindowDrag();
        e.Handled = true;
    }

    private void OnCaptionPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Expected when the no-activate HWND moves under a touch contact.
        // ScreenPointerTracker remains valid until GetPointerInfo reports up.
        e.Handled = true;
    }

    private void StopWindowDrag()
    {
        _windowDragging = false;
        _windowDragNativeTracking = false;
        _windowDragTimer.Stop();
        _windowDragReadMisses = 0;
        KeepTopmost();
    }

    private static void OnClosed(object sender, WindowEventArgs args)
    {
        if (sender is KeyboardWindow window)
        {
            window._topmostWatchTimer.Stop();
            window.AppWindow.Changed -= window.OnAppWindowChanged;
            window.StopClipsWatch();
            window.AttachSwipeDiagnostic(null);
            SwipeDiagnosticWindow.CloseIfOpen();
        }

        // Real exit (Quitter). Hide-to-tray uses AppWindow.Hide and does not raise Closed.
        Application.Current.Exit();
    }

    private sealed record KeyContext(KeyDefinition Key, Brush BaseBrush);
}

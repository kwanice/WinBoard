using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinBoard.Input;
using WinBoard.Layouts;
using WinBoard.Services;

namespace WinBoard.UI;

public sealed partial class KeyboardWindow : Window
{
    private const double WindowWidthDip = 820;
    private const double WindowHeightDip = 308;
    private const double LetterKeySize = 52;
    private const double KeyGap = 8;

    private readonly LayoutService _layouts = new();
    private bool _dragging;
    private POINT _dragCursorStart;
    private PointInt32 _dragWindowStart;

    public KeyboardWindow()
    {
        InitializeComponent();

        Title = "WinBoard";
        SystemBackdrop = new DesktopAcrylicBackdrop();

        _layouts.LayoutChanged += (_, _) => RenderLayout();
        Closed += OnClosed;

        ConfigurePresenter();
        NoActivateWindow.Apply(NoActivateWindow.GetHwnd(this));
        PositionOnWorkArea();
        RenderLayout();
    }

    /// <summary>
    /// Shows the overlay without calling <see cref="Window.Activate"/>, which
    /// would steal focus from the target application.
    /// </summary>
    public void ShowWithoutActivating()
    {
        AppWindow.Show(activateWindow: false);
        // Presenter/Show can reset extended styles; re-apply no-activate afterwards.
        NoActivateWindow.Apply(NoActivateWindow.GetHwnd(this));
    }

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

    private void PositionOnWorkArea()
    {
        nint hwnd = NoActivateWindow.GetHwnd(this);
        int width = DipToPixels(hwnd, WindowWidthDip);
        int height = DipToPixels(hwnd, WindowHeightDip);

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

    private void RenderLayout()
    {
        KeysHost.Children.Clear();
        RefreshLayoutButtons();

        foreach (IReadOnlyList<KeyDefinition> row in _layouts.Current.Rows)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = KeyGap,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            foreach (KeyDefinition key in row)
            {
                panel.Children.Add(CreateKeyButton(key));
            }

            KeysHost.Children.Add(panel);
        }
    }

    private void RefreshLayoutButtons()
    {
        bool azerty = _layouts.Current.Id == LayoutCatalog.AzertyId;
        FrButton.Style = AccentOrDefault(azerty);
        EnButton.Style = AccentOrDefault(!azerty);
    }

    private static Style? AccentOrDefault(bool selected)
    {
        string key = selected ? "AccentButtonStyle" : "DefaultButtonStyle";
        return Application.Current.Resources[key] as Style;
    }

    private Button CreateKeyButton(KeyDefinition key)
    {
        var button = new Button
        {
            Content = key.Label,
            Width = LetterKeySize * key.WidthUnits + KeyGap * Math.Max(0, key.WidthUnits - 1),
            Height = LetterKeySize,
            Padding = new Thickness(0),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            CornerRadius = new CornerRadius(8),
            AllowFocusOnInteraction = false,
            IsTabStop = false,
            Tag = key,
        };

        button.Click += OnKeyClicked;
        // TODO(swipe): attach PointerMoved here to sample the stroke path for swipe typing.
        button.PointerMoved += OnKeyPointerMoved;
        return button;
    }

    private void OnKeyClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: KeyDefinition key })
        {
            return;
        }

        switch (key.Kind)
        {
            case KeyKind.Backspace:
                KeyboardInjector.InjectBackspace();
                break;
            case KeyKind.Space:
            case KeyKind.Character:
                if (key.Character is char character)
                {
                    KeyboardInjector.InjectCharacter(character);
                }

                break;
        }
    }

    private static void OnKeyPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // TODO(swipe): collect pointer samples (x, y, timestamp) while pressed
        // and hand them to a swipe decoder. Tap injection stays on Click for now.
        _ = sender;
        _ = e;
    }

    private void OnFrClicked(object sender, RoutedEventArgs e)
    {
        _layouts.SetLayout(LayoutCatalog.AzertyId);
    }

    private void OnEnClicked(object sender, RoutedEventArgs e)
    {
        _layouts.SetLayout(LayoutCatalog.QwertyId);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

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

    private static void OnClosed(Window sender, WindowEventArgs args)
    {
        Application.Current.Exit();
    }
}

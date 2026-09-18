using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinBoard.Core;
using WinBoard.Input;
using WinBoard.Services;

namespace WinBoard.UI;

/// <summary>
/// Guided swipe diagnostic (focusable, like Settings). Capture is active only
/// while this window is open. Closing detaches the keyboard sink. Export is
/// a local file write — never uploaded.
/// </summary>
public sealed partial class SwipeDiagnosticWindow : Window
{
    private const int WidthDip = 480;
    private const int HeightDip = 720;

    private static SwipeDiagnosticWindow? _open;

    private readonly KeyboardWindow _keyboard;
    private readonly SwipeDiagnosticRun _run = new();
    private string? _lastExportPath;

    private SwipeDiagnosticWindow(KeyboardWindow keyboard)
    {
        _keyboard = keyboard;
        InitializeComponent();
        Title = "Diag swipe — WinBoard";
        SystemBackdrop = new DesktopAcrylicBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: true);
        }

        RootGrid.RequestedTheme = SettingsService.Shared.Current.Theme == "Light"
            ? ElementTheme.Light
            : ElementTheme.Dark;
        Closed += OnClosed;
        RootGrid.Loaded += (_, _) => ResizeClient();
        _keyboard.AttachSwipeDiagnostic(OnSwipeCaptured);
        RefreshUi();
        ResizeClient();
    }

    public static void Show(KeyboardWindow keyboard)
    {
        if (_open is not null)
        {
            _open.AppWindow.Show(activateWindow: true);
            _open.Activate();
            return;
        }

        _open = new SwipeDiagnosticWindow(keyboard);
        _open.PlaceBeside(keyboard);
        _open.Activate();
    }

    public static void CloseIfOpen()
    {
        _open?.Close();
    }

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

    private void OnSwipeCaptured(SwipeGestureCapture capture)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_run.IsComplete || _run.CurrentExpected is null)
            {
                return;
            }

            SwipeDiagnosticWord word = SwipeDiagnostic.FromGesture(_run.CurrentExpected, capture);
            ApplyNotes(word);
            _run.SetPending(word);
            RefreshUi();
        });
    }

    private void ApplyNotes(SwipeDiagnosticWord word)
    {
        string notes = NotesBox.Text?.Trim() ?? string.Empty;
        word.Notes = notes.Length == 0 ? null : notes;
    }

    private void OnNotesChanged(object sender, TextChangedEventArgs e)
    {
        if (_run.Pending is { } pending)
        {
            ApplyNotes(pending);
        }
    }

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        if (_run.Pending is not null)
        {
            ApplyNotes(_run.Pending);
        }

        _run.TryPass();
        NotesBox.Text = string.Empty;
        RefreshUi();
    }

    private void OnFailClicked(object sender, RoutedEventArgs e)
    {
        if (_run.Pending is not null)
        {
            ApplyNotes(_run.Pending);
        }

        _run.TryFail();
        NotesBox.Text = string.Empty;
        RefreshUi();
    }

    private void OnSkipClicked(object sender, RoutedEventArgs e)
    {
        _run.TrySkip();
        NotesBox.Text = string.Empty;
        RefreshUi();
    }

    private void OnRetryClicked(object sender, RoutedEventArgs e)
    {
        _run.TryRetry();
        RefreshUi();
    }

    private void OnNextClicked(object sender, RoutedEventArgs e)
    {
        if (_run.Pending is not null)
        {
            ApplyNotes(_run.Pending);
        }

        _run.TryNext();
        NotesBox.Text = string.Empty;
        RefreshUi();
    }

    private void OnRestartClicked(object sender, RoutedEventArgs e)
    {
        _run.Restart();
        NotesBox.Text = string.Empty;
        ExportStatusText.Text = string.Empty;
        RefreshUi();
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        if (_run.Pending is not null)
        {
            ApplyNotes(_run.Pending);
        }

        SwipeDiagnosticDocument doc = _run.ToDocument(
            AppInfo.Version,
            _keyboard.DiagnosticLayoutLabel,
            _keyboard.DiagnosticKeyboardScale);
        try
        {
            string path = SwipeDiagnostic.WriteExport(doc, DateTime.Now);
            _lastExportPath = path;
            ExportStatusText.Text = "Écrit : " + path;
        }
        catch (Exception ex)
        {
            ExportStatusText.Text = "Export impossible : " + ex.Message;
        }
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        string dir = SwipeDiagnostic.GetDirectory();
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
            ExportStatusText.Text = dir;
        }
        catch (Exception ex)
        {
            ExportStatusText.Text = "Dossier : " + ex.Message;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void RefreshUi()
    {
        ProgressText.Text = _run.IsComplete
            ? $"{_run.Count} / {_run.Count} — session terminée"
            : $"{_run.DisplayIndex} / {_run.Count}";
        TargetText.Text = _run.CurrentExpected ?? "—";

        bool hasPending = _run.Pending is not null;
        bool active = !_run.IsComplete;
        OkButton.IsEnabled = active && hasPending;
        FailButton.IsEnabled = active && hasPending;
        SkipButton.IsEnabled = active;
        RetryButton.IsEnabled = active && hasPending;
        NextButton.IsEnabled = active;

        if (_run.IsComplete)
        {
            StatusText.Text = "Session terminée. Exportez le JSON pour l’analyse (local uniquement).";
            CandidatesText.Text = FormatCommittedSummary();
            return;
        }

        if (_run.Pending is { } pending)
        {
            StatusText.Text = pending.Decoded is { Length: > 0 } decoded
                ? $"Décodé : {decoded}  —  marquez OK, Échec, Passer, Réessayer ou Suivant."
                : "Aucun candidat. Réessayez, passez, ou Suivant.";
            CandidatesText.Text = FormatCandidates(pending);
            return;
        }

        StatusText.Text = "Glissez « " + _run.CurrentExpected + " » sur le clavier.";
        CandidatesText.Text = "En attente d’un swipe…";
    }

    private static string FormatCandidates(SwipeDiagnosticWord word)
    {
        if (word.Candidates.Count == 0)
        {
            string hits = word.HitKeys.Count == 0 ? "—" : string.Join(" ", word.HitKeys);
            return "Aucun candidat.\nHit-keys : " + hits;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < word.Candidates.Count; i++)
        {
            SwipeDiagnosticCandidate c = word.Candidates[i];
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1}. {c.Word}  {c.Score:F3}");
            if (c.Breakdown is { } b)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"  loc={b.Location:F3} dtw={b.Dtw:F3} spat={b.Spatial:F3} len={b.Length:F3} anc={b.Anchors:F3} hit={b.HitKeys:F3} lm={b.Language:F3}");
            }

            sb.AppendLine();
        }

        if (word.HitKeys.Count > 0)
        {
            sb.Append("Hit-keys : ");
            sb.Append(string.Join(" ", word.HitKeys));
        }

        return sb.ToString().TrimEnd();
    }

    private string FormatCommittedSummary()
    {
        int ok = 0, fail = 0, other = 0;
        foreach (SwipeDiagnosticWord word in _run.Committed)
        {
            if (word.Ok == true)
            {
                ok++;
            }
            else if (word.Ok == false)
            {
                fail++;
            }
            else
            {
                other++;
            }
        }

        return $"OK {ok} · Échec {fail} · Passé/non marqué {other}";
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _keyboard.AttachSwipeDiagnostic(null);
        _keyboard.RestoreAfterSettings();
        _open = null;
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
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
    private const int HeightDip = 780;

    private static SwipeDiagnosticWindow? _open;

    private readonly KeyboardWindow _keyboard;
    private readonly SwipeDiagnosticRun _run = new();
    private readonly Dictionary<int, SwipeGestureCapture> _inbox = [];
    private int _nextGestureOrdinal = 1;
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
            keyboard.KeepTopmost();
            return;
        }

        _open = new SwipeDiagnosticWindow(keyboard);
        _open.PlaceBeside(keyboard);
        _open.Activate();
        keyboard.KeepTopmost();
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
            int ordinal = capture.GestureOrdinal > 0 ? capture.GestureOrdinal : _nextGestureOrdinal;
            _inbox[ordinal] = capture;
            DrainInbox();
        });
    }

    private void DrainInbox()
    {
        while (_inbox.Remove(_nextGestureOrdinal, out SwipeGestureCapture? capture))
        {
            ApplyCapture(capture);
            _nextGestureOrdinal++;
        }

        RefreshUi();
    }

    private void ApplyCapture(SwipeGestureCapture capture)
    {
        string expected = _run.CurrentExpected ?? capture.Chosen ?? string.Empty;
        SwipeDiagnosticWord word = SwipeDiagnostic.FromGesture(expected, capture);
        ApplyNotes(word);
        if (capture.GestureAborted)
        {
            _run.TryRecordAbort(word);
        }
        else if (!_run.IsComplete)
        {
            _run.TryRecordCapture(word);
        }

        NotesBox.Text = string.Empty;
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
        _inbox.Clear();
        _nextGestureOrdinal = 1;
        _keyboard.AttachSwipeDiagnostic(OnSwipeCaptured);
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
            nint hwnd = NoActivateWindow.GetHwnd(this);
            bool copied = ClipboardText.TryCopy(path, hwnd);
            ExportStatusText.Text = SwipeDiagnostic.FormatExportStatus(path, copied);
            _keyboard.KeepTopmost();
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
        RenderPhrase();
        if (_run.IsComplete)
        {
            ProgressText.Text = $"Phrases {_run.PhraseCount} / {_run.PhraseCount} · {_run.Count} mots — session terminée";
            TargetText.Text = "—";
        }
        else
        {
            ProgressText.Text =
                $"Phrase {_run.CurrentPhraseNumber} / {_run.PhraseCount} · mot {_run.CurrentWordIndex + 1} / {_run.CurrentWordCount}"
                + $"  ·  swipe {_run.DisplayIndex} / {_run.Count}";
            TargetText.Text = _run.CurrentExpected ?? "—";
        }

        bool hasLast = _run.LastCommitted is not null;
        bool active = !_run.IsComplete;
        OkButton.IsEnabled = hasLast;
        FailButton.IsEnabled = hasLast;
        SkipButton.IsEnabled = active;
        RetryButton.IsEnabled = hasLast || _run.Pending is not null;
        NextButton.IsEnabled = active;

        if (_run.IsComplete)
        {
            StatusText.Text = "Session terminée. Exportez le JSON pour l’analyse (local uniquement).";
            ChainText.Text = _run.LastAbort is { } doneAbort
                ? SwipeDiagnostic.FormatChainLine(doneAbort)
                : string.Empty;
            CandidatesText.Text = FormatCommittedSummary();
            return;
        }

        if (_run.LastAbort is { } abort
            && abort.PhraseId == _run.CurrentPhraseId
            && abort.WordIndex == _run.CurrentWordIndex)
        {
            StatusText.Text = "Geste incomplet"
                + (abort.Reason is { Length: > 0 } ? " (" + abort.Reason + ")" : string.Empty)
                + " — réessayez « " + _run.CurrentExpected + " ». Le mot n’a pas avancé.";
            ChainText.Text = SwipeDiagnostic.FormatChainLine(abort);
            CandidatesText.Text = FormatCandidates(abort);
            return;
        }

        if (_run.LastCommitted is { } last
            && last.PhraseId == _run.CurrentPhraseId
            && last.WordIndex == _run.CurrentWordIndex - 1)
        {
            StatusText.Text = FormatLastStatus(last)
                + "  Glissez « " + _run.CurrentExpected + " » (enchaînez sans pause).";
            ChainText.Text = SwipeDiagnostic.FormatChainLine(last);
            CandidatesText.Text = FormatCandidates(last);
            return;
        }

        if (_run.LastCommitted is { } previous && _run.CurrentWordIndex == 0)
        {
            StatusText.Text = "Phrase suivante. Glissez « " + _run.CurrentExpected + " ».";
            ChainText.Text = SwipeDiagnostic.FormatChainLine(previous);
            CandidatesText.Text = FormatCandidates(previous);
            return;
        }

        StatusText.Text = "Glissez « " + _run.CurrentExpected + " » sur le clavier, puis enchaînez les mots de la phrase.";
        ChainText.Text = string.Empty;
        CandidatesText.Text = "En attente d’un swipe…";
    }

    private void RenderPhrase()
    {
        PhraseText.Inlines.Clear();
        string? phrase = _run.CurrentPhrase ?? _run.LastCommitted?.Phrase;
        if (string.IsNullOrEmpty(phrase))
        {
            PhraseText.Inlines.Add(new Run { Text = "—" });
            return;
        }

        string[] words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int highlight = _run.IsComplete
            ? -1
            : _run.CurrentWordIndex;
        for (int i = 0; i < words.Length; i++)
        {
            if (i > 0)
            {
                PhraseText.Inlines.Add(new Run { Text = " " });
            }

            var run = new Run { Text = words[i] };
            if (i == highlight)
            {
                run.FontWeight = FontWeights.Bold;
                run.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
            }

            PhraseText.Inlines.Add(run);
        }
    }

    private static string FormatLastStatus(SwipeDiagnosticWord last)
    {
        string decoded = last.Decoded ?? "—";
        string mark = last.Ok == true ? "OK" : last.Ok == false ? "Échec" : "non marqué";
        return $"Dernier : {last.Expected} → {decoded} ({mark}).";
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
        SwipeDiagnosticDocument doc = _run.ToDocument("preview", "AZERTY", 1);
        var sb = new StringBuilder();
        if (doc.Phrases is { Count: > 0 })
        {
            foreach (SwipeDiagnosticPhraseResult phrase in doc.Phrases)
            {
                string mark = phrase.Ok == true ? "OK" : phrase.Ok == false ? "Échec" : "partiel";
                sb.Append(CultureInfo.InvariantCulture, $"{mark}  {phrase.Text}\n");
                foreach (SwipeDiagnosticWord word in phrase.Words)
                {
                    string wmark = word.Ok == true ? "✓" : word.Ok == false ? "✗" : "·";
                    sb.Append(CultureInfo.InvariantCulture,
                        $"  {wmark} {word.Expected} → {word.Decoded ?? "—"}\n");
                }
            }
        }

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

        sb.Append(CultureInfo.InvariantCulture, $"Mots : OK {ok} · Échec {fail} · Passé/non marqué {other}");
        if (_run.AbortedGestures.Count > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\nGestes abortés : {_run.AbortedGestures.Count}");
            foreach (SwipeDiagnosticWord abort in _run.AbortedGestures)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"\n  ✗ {abort.Expected} ptr={abort.PointerId} {abort.Reason ?? "canceled"}");
            }
        }
        return sb.ToString().TrimEnd();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _keyboard.AttachSwipeDiagnostic(null);
        _keyboard.RestoreAfterSettings();
        _open = null;
    }
}

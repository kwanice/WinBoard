using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBoard.Core;

/// <summary>
/// One decoder candidate with the score used for ranking (lower is better).
/// </summary>
public sealed class ExplainedSwipe
{
    public required string Word { get; init; }

    public required double Score { get; init; }

    public SwipeScoreBreakdown? Breakdown { get; init; }
}

/// <summary>
/// Weighted score parts. <see cref="Spatial"/> is location + DTW; the other
/// fields are the remaining Search/LM terms. They are diagnostics only.
/// </summary>
public sealed class SwipeScoreBreakdown
{
    [JsonPropertyName("spatial")]
    public double Spatial { get; init; }

    [JsonPropertyName("dtw")]
    public double Dtw { get; init; }

    [JsonPropertyName("location")]
    public double Location { get; init; }

    [JsonPropertyName("length")]
    public double Length { get; init; }

    [JsonPropertyName("anchors")]
    public double Anchors { get; init; }

    [JsonPropertyName("hitKeys")]
    public double HitKeys { get; init; }

    [JsonPropertyName("language")]
    public double Language { get; init; }

    public SwipeScoreBreakdown WithLanguage(double language) => new()
    {
        Spatial = Spatial,
        Dtw = Dtw,
        Location = Location,
        Length = Length,
        Anchors = Anchors,
        HitKeys = HitKeys,
        Language = language,
    };
}

/// <summary>
/// Live swipe snapshot in layout DIP (RootGrid / letter-center space).
/// Converted to key-pitch coordinates when folded into a session word.
/// </summary>
public sealed class SwipeGestureCapture
{
    public required IReadOnlyList<Point2> PathDip { get; init; }

    public IReadOnlyList<long>? PathElapsedMs { get; init; }

    public required IReadOnlyList<char> HitKeys { get; init; }

    public required IReadOnlyList<ExplainedSwipe> Candidates { get; init; }

    public string? Chosen { get; init; }

    public required string Layout { get; init; }

    public required double KeyboardScale { get; init; }

    public required double PitchDip { get; init; }

    public required IReadOnlyDictionary<char, Point2> CentersDip { get; init; }

    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Decoder wall time in milliseconds (diag only; never sent anywhere).</summary>
    public double? DecodeMs { get; init; }

    /// <summary>Monotonic id assigned when the finger lifts / the gesture ends.</summary>
    public int GestureOrdinal { get; init; }

    public int CaptureLostCount { get; init; }

    public int TrailPointCount { get; init; }

    public bool GestureAborted { get; init; }

    public double? TimeSincePreviousSwipeMs { get; init; }

    public uint? PointerId { get; init; }

    public bool TrailClearedMidGesture { get; init; }
}

/// <summary>2D point written to diagnostic JSON (path samples or key centers).</summary>
public sealed class SwipeDiagPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("t")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? T { get; set; }
}

/// <summary>One decoder candidate in the exported session.</summary>
public sealed class SwipeDiagnosticCandidate
{
    [JsonPropertyName("word")]
    public string Word { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("breakdown")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SwipeScoreBreakdown? Breakdown { get; set; }
}

/// <summary>One guided-list trial (one expected word, one swipe or skip).</summary>
public sealed class SwipeDiagnosticWord
{
    [JsonPropertyName("expected")]
    public string Expected { get; set; } = string.Empty;

    [JsonPropertyName("decoded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Decoded { get; set; }

    /// <summary>True = OK, false = Fail, null = skipped / unmarked.</summary>
    [JsonPropertyName("ok")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? Ok { get; set; }

    [JsonPropertyName("hitKeys")]
    public List<string> HitKeys { get; set; } = [];

    [JsonPropertyName("path")]
    public List<SwipeDiagPoint> Path { get; set; } = [];

    [JsonPropertyName("candidates")]
    public List<SwipeDiagnosticCandidate> Candidates { get; set; } = [];

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }

    [JsonPropertyName("layout")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Layout { get; set; }

    [JsonPropertyName("keyboardScale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? KeyboardScale { get; set; }

    [JsonPropertyName("originDip")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SwipeDiagPoint? OriginDip { get; set; }

    [JsonPropertyName("pitchDip")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PitchDip { get; set; }

    [JsonPropertyName("centers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, SwipeDiagPoint>? Centers { get; set; }

    [JsonPropertyName("timestampUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TimestampUtc { get; set; }

    [JsonPropertyName("decodeMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DecodeMs { get; set; }

    [JsonPropertyName("phraseId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PhraseId { get; set; }

    [JsonPropertyName("phrase")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Phrase { get; set; }

    [JsonPropertyName("wordIndex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WordIndex { get; set; }

    [JsonPropertyName("wordCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WordCount { get; set; }

    [JsonPropertyName("gestureOrdinal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? GestureOrdinal { get; set; }

    [JsonPropertyName("captureLostCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CaptureLostCount { get; set; }

    [JsonPropertyName("trailPointCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TrailPointCount { get; set; }

    [JsonPropertyName("gestureAborted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? GestureAborted { get; set; }

    [JsonPropertyName("timeSincePreviousSwipeMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TimeSincePreviousSwipeMs { get; set; }

    [JsonPropertyName("pointerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? PointerId { get; set; }

    [JsonPropertyName("trailClearedMidGesture")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TrailClearedMidGesture { get; set; }
}

/// <summary>One guided phrase (several swipe words chained).</summary>
public sealed class SwipeDiagPhrase
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    public required IReadOnlyList<string> Words { get; init; }
}

/// <summary>Phrase roll-up in the exported session.</summary>
public sealed class SwipeDiagnosticPhraseResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>True only when every word is ok==true. False if any word failed. Null if any unmarked.</summary>
    [JsonPropertyName("ok")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? Ok { get; set; }

    [JsonPropertyName("words")]
    public List<SwipeDiagnosticWord> Words { get; set; } = [];
}

/// <summary>One word slot inside a guided phrase.</summary>
public sealed class SwipeDiagStep
{
    public required string PhraseId { get; init; }

    public required string Phrase { get; init; }

    public required int PhraseIndex { get; init; }

    public required int PhraseCount { get; init; }

    public required int WordIndex { get; init; }

    public required int WordCount { get; init; }

    public required string Expected { get; init; }
}

/// <summary>
/// Schema version 2 diagnostic dump (parser still accepts version 1). 100 %
/// local: written only when the user clicks Export. Coordinate space is
/// documented on <see cref="SwipeDiagnostic.CoordinateSpace"/>.
/// </summary>
public sealed class SwipeDiagnosticDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = SwipeDiagnostic.SchemaVersion;

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = string.Empty;

    [JsonPropertyName("layout")]
    public string Layout { get; set; } = string.Empty;

    [JsonPropertyName("keyboardScale")]
    public double KeyboardScale { get; set; }

    [JsonPropertyName("coordinateSpace")]
    public string CoordinateSpace { get; set; } = SwipeDiagnostic.CoordinateSpace;

    [JsonPropertyName("coordinateSpaceNote")]
    public string CoordinateSpaceNote { get; set; } = SwipeDiagnostic.CoordinateSpaceNote;

    [JsonPropertyName("exportedAtUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExportedAtUtc { get; set; }

    [JsonPropertyName("phrases")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SwipeDiagnosticPhraseResult>? Phrases { get; set; }

    [JsonPropertyName("words")]
    public List<SwipeDiagnosticWord> Words { get; set; } = [];
}

/// <summary>
/// Guided diagnostic session over phrases (each word is one swipe). Capture
/// is in-memory until the user exports.
/// </summary>
public sealed class SwipeDiagnosticRun
{
    private readonly List<SwipeDiagPhrase> _phrases;
    private readonly List<SwipeDiagStep> _steps;
    private readonly List<SwipeDiagnosticWord> _committed = [];
    private SwipeDiagnosticWord? _pending;
    private int _index;

    public SwipeDiagnosticRun(IReadOnlyList<string>? targets)
        : this(WrapAsPhrases(targets))
    {
    }

    public SwipeDiagnosticRun(IReadOnlyList<SwipeDiagPhrase>? phrases = null)
    {
        _phrases = [.. phrases is { Count: > 0 } ? phrases : SwipeDiagnostic.DefaultPhrases];
        _steps = SwipeDiagnostic.ExpandSteps(_phrases);
    }

    public IReadOnlyList<SwipeDiagPhrase> Phrases => _phrases;

    public IReadOnlyList<string> Targets => _steps.Select(s => s.Expected).ToList();

    public SwipeDiagStep? CurrentStep => _index < _steps.Count ? _steps[_index] : null;

    /// <summary>0-based index of the current word slot. Equals <see cref="Count"/> when complete.</summary>
    public int Index => _index;

    public int Count => _steps.Count;

    public int PhraseCount => _phrases.Count;

    public int DisplayIndex => _steps.Count == 0
        ? 0
        : Math.Min(_index + 1, _steps.Count);

    public string? CurrentExpected => CurrentStep?.Expected;

    public string? CurrentPhrase => CurrentStep?.Phrase;

    public string? CurrentPhraseId => CurrentStep?.PhraseId;

    public int CurrentWordIndex => CurrentStep?.WordIndex ?? 0;

    public int CurrentWordCount => CurrentStep?.WordCount ?? 0;

    public int CurrentPhraseNumber => CurrentStep is null ? _phrases.Count : CurrentStep.PhraseIndex + 1;

    public bool IsComplete => _index >= _steps.Count;

    public SwipeDiagnosticWord? Pending => _pending;

    public IReadOnlyList<SwipeDiagnosticWord> Committed => _committed;

    public SwipeDiagnosticWord? LastCommitted => _committed.Count == 0 ? null : _committed[^1];

    /// <summary>Overwrite the pending swipe for the current target.</summary>
    public void SetPending(SwipeDiagnosticWord word)
    {
        if (IsComplete)
        {
            return;
        }

        Annotate(word);
        _pending = word;
    }

    /// <summary>
    /// Record a finished (or aborted) swipe as the current word and advance.
    /// Auto-marks ok when decoded matches expected (folded) and the gesture was not aborted.
    /// </summary>
    public bool TryRecordCapture(SwipeDiagnosticWord word)
    {
        if (IsComplete)
        {
            return false;
        }

        Annotate(word);
        if (word.GestureAborted == true)
        {
            word.Ok = false;
        }
        else if (word.Ok is null)
        {
            word.Ok = SwipeDiagnostic.MatchesExpected(word.Expected, word.Decoded);
        }

        _committed.Add(word);
        _pending = null;
        _index++;
        return true;
    }

    public bool TryMarkLast(bool ok)
    {
        if (_committed.Count == 0)
        {
            return false;
        }

        _committed[^1].Ok = ok;
        return true;
    }

    public bool TryPass()
    {
        if (_pending is not null)
        {
            return CommitMarked(true);
        }

        return TryMarkLast(true);
    }

    public bool TryFail()
    {
        if (_pending is not null)
        {
            return CommitMarked(false);
        }

        return TryMarkLast(false);
    }

    /// <summary>Drop the pending swipe (if any) and record an unmarked skip.</summary>
    public bool TrySkip()
    {
        if (IsComplete)
        {
            return false;
        }

        SwipeDiagnosticWord empty = Empty(CurrentExpected!);
        Annotate(empty);
        _committed.Add(empty);
        _pending = null;
        _index++;
        return true;
    }

    /// <summary>Keep the pending capture unmarked (or skip if none) and advance.</summary>
    public bool TryNext()
    {
        if (IsComplete)
        {
            return false;
        }

        SwipeDiagnosticWord word = _pending ?? Empty(CurrentExpected!);
        Annotate(word);
        _committed.Add(word);
        _pending = null;
        _index++;
        return true;
    }

    public bool TryRetry()
    {
        if (_pending is not null)
        {
            _pending = null;
            return true;
        }

        if (_committed.Count > 0 && (_index > 0 || IsComplete))
        {
            _committed.RemoveAt(_committed.Count - 1);
            _index = Math.Max(0, _index - 1);
            return true;
        }

        return !IsComplete;
    }

    public void Restart()
    {
        _committed.Clear();
        _pending = null;
        _index = 0;
    }

    public SwipeDiagnosticDocument ToDocument(
        string appVersion,
        string layout,
        double keyboardScale,
        DateTimeOffset? exportedAtUtc = null)
    {
        var words = new List<SwipeDiagnosticWord>(_committed.Count + (_pending is null ? 0 : 1));
        words.AddRange(_committed);
        if (_pending is not null)
        {
            words.Add(_pending);
        }

        DateTimeOffset exported = exportedAtUtc ?? DateTimeOffset.UtcNow;
        return new SwipeDiagnosticDocument
        {
            SchemaVersion = SwipeDiagnostic.SchemaVersion,
            AppVersion = appVersion,
            Layout = layout,
            KeyboardScale = keyboardScale,
            CoordinateSpace = SwipeDiagnostic.CoordinateSpace,
            CoordinateSpaceNote = SwipeDiagnostic.CoordinateSpaceNote,
            ExportedAtUtc = exported.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            Phrases = SwipeDiagnostic.GroupPhrases(words),
            Words = words,
        };
    }

    private void Annotate(SwipeDiagnosticWord word)
    {
        SwipeDiagStep? step = CurrentStep;
        if (step is null)
        {
            return;
        }

        word.Expected = step.Expected;
        word.PhraseId = step.PhraseId;
        word.Phrase = step.Phrase;
        word.WordIndex = step.WordIndex;
        word.WordCount = step.WordCount;
    }

    private bool CommitMarked(bool ok)
    {
        if (IsComplete || _pending is null)
        {
            return false;
        }

        _pending.Ok = ok;
        _committed.Add(_pending);
        _pending = null;
        _index++;
        return true;
    }

    private static SwipeDiagnosticWord Empty(string expected) => new()
    {
        Expected = expected,
        Ok = null,
    };

    private static IReadOnlyList<SwipeDiagPhrase> WrapAsPhrases(IReadOnlyList<string>? targets)
    {
        if (targets is not { Count: > 0 })
        {
            return SwipeDiagnostic.DefaultPhrases;
        }

        var phrases = new List<SwipeDiagPhrase>(targets.Count);
        for (int i = 0; i < targets.Count; i++)
        {
            string word = targets[i];
            phrases.Add(new SwipeDiagPhrase
            {
                Id = "w" + i.ToString(CultureInfo.InvariantCulture),
                Text = word,
                Words = [word],
            });
        }

        return phrases;
    }
}

/// <summary>
/// Local-only swipe diagnostic schema helpers. No network, no automatic write.
/// </summary>
public static class SwipeDiagnostic
{
    /// <summary>Current export schema. Parser still accepts version 1 replay fixtures.</summary>
    public const int SchemaVersion = 2;

    public const int MinParseSchemaVersion = 1;

    public const string FolderName = "WinBoard";

    public const string DiagnosticsFolder = "diagnostics";

    /// <summary>
    /// Path coordinates are key-pitch units relative to the letter-center
    /// bounding-box origin in layout DIP (same space as the swipe trail /
    /// <c>RootGrid</c> pointer points). Replay:
    /// <c>dip = originDip + pitchDip * (x, y)</c>.
    /// </summary>
    public const string CoordinateSpace = "key-pitch";

    public const string CoordinateSpaceNote =
        "path.x/y are key-pitch units: (layoutDip - originDip) / pitchDip. "
        + "originDip is the min letter-center (x, y) in layout DIP (RootGrid / swipe trail). "
        + "pitchDip is the median letter-key spacing. "
        + "Replay: dip = originDip + pitchDip * (x, y). "
        + "centers lists letter key centers in the same key-pitch space. "
        + "path.t is milliseconds from the first sample (optional).";

    /// <summary>
    /// Default guided list (0.8.13): 4–5 word FR/EN phrases to expose chained
    /// swipe failures, plus two short warm-up tokens. Distinct from the 0.8.4
    /// retune fixture. Distractors like collent/content are not targets.
    /// </summary>
    public static readonly IReadOnlyList<SwipeDiagPhrase> DefaultPhrases =
    [
        ParsePhrase("je-vais-au-marche", "je vais au marché"),
        ParsePhrase("bonjour-comment-ca-va", "bonjour comment ça va"),
        ParsePhrase("the-quick-brown-fox", "the quick brown fox"),
        ParsePhrase("i-need-a-coffee", "I need a coffee"),
        ParsePhrase("oui", "oui"),
        ParsePhrase("keyboard", "keyboard"),
    ];

    /// <summary>Flattened expected words (each phrase word is one swipe).</summary>
    public static IReadOnlyList<string> DefaultTargets => ExpandSteps(DefaultPhrases)
        .Select(s => s.Expected)
        .ToList();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static string GetDirectory(string? localAppData = null)
    {
        string root = string.IsNullOrWhiteSpace(localAppData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppData;
        return Path.Combine(root, FolderName, DiagnosticsFolder);
    }

    public static string FileName(DateTime timestamp) =>
        string.Create(CultureInfo.InvariantCulture, $"swipe-{timestamp:yyyyMMdd-HHmmss}.json");

    public static string FormatExportStatus(string path, bool copied) =>
        copied
            ? "Chemin copié : " + path
            : "Écrit : " + path + " — copie presse-papiers impossible";

    public static string CombineExportPath(DateTime timestamp, string? localAppData = null) =>
        Path.Combine(GetDirectory(localAppData), FileName(timestamp));

    public static string Serialize(SwipeDiagnosticDocument document) =>
        JsonSerializer.Serialize(document, JsonOptions);

    public static SwipeDiagnosticDocument? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            SwipeDiagnosticDocument? doc = JsonSerializer.Deserialize<SwipeDiagnosticDocument>(json, JsonOptions);
            if (doc is null
                || doc.SchemaVersion < MinParseSchemaVersion
                || doc.SchemaVersion > SchemaVersion
                || doc.Words is null
                || string.IsNullOrWhiteSpace(doc.AppVersion))
            {
                return null;
            }

            foreach (SwipeDiagnosticWord word in doc.Words)
            {
                word.HitKeys ??= [];
                word.Path ??= [];
                word.Candidates ??= [];
            }

            doc.Phrases ??= [];
            return doc;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string WriteExport(
        SwipeDiagnosticDocument document,
        DateTime timestamp,
        string? localAppData = null)
    {
        string dir = GetDirectory(localAppData);
        Directory.CreateDirectory(dir);
        string path = UniquePath(dir, timestamp);
        File.WriteAllText(path, Serialize(document));
        return path;
    }

    public static Point2 OriginDip(IReadOnlyDictionary<char, Point2> centers)
    {
        if (centers.Count == 0)
        {
            return new Point2(0, 0);
        }

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        foreach (Point2 p in centers.Values)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
        }

        return new Point2(minX, minY);
    }

    public static Point2 ToPitch(Point2 dip, Point2 origin, double pitch)
    {
        double p = pitch > 1e-9 ? pitch : 1;
        return new Point2((dip.X - origin.X) / p, (dip.Y - origin.Y) / p);
    }

    public static Point2 FromPitch(Point2 pitchCoords, Point2 origin, double pitch)
    {
        double p = pitch > 1e-9 ? pitch : 1;
        return new Point2(origin.X + (pitchCoords.X * p), origin.Y + (pitchCoords.Y * p));
    }

    public static SwipeDiagnosticWord FromGesture(string expected, SwipeGestureCapture capture)
    {
        Point2 origin = OriginDip(capture.CentersDip);
        double pitch = capture.PitchDip > 1e-9 ? capture.PitchDip : 1;
        var path = new List<SwipeDiagPoint>(capture.PathDip.Count);
        for (int i = 0; i < capture.PathDip.Count; i++)
        {
            Point2 p = ToPitch(capture.PathDip[i], origin, pitch);
            long? t = null;
            if (capture.PathElapsedMs is not null && i < capture.PathElapsedMs.Count)
            {
                t = capture.PathElapsedMs[i];
            }

            path.Add(new SwipeDiagPoint { X = Round(p.X), Y = Round(p.Y), T = t });
        }

        var centers = new Dictionary<string, SwipeDiagPoint>();
        foreach ((char letter, Point2 dip) in capture.CentersDip.OrderBy(kv => kv.Key))
        {
            Point2 p = ToPitch(dip, origin, pitch);
            centers[letter.ToString()] = new SwipeDiagPoint { X = Round(p.X), Y = Round(p.Y) };
        }

        var candidates = new List<SwipeDiagnosticCandidate>(capture.Candidates.Count);
        foreach (ExplainedSwipe item in capture.Candidates)
        {
            candidates.Add(new SwipeDiagnosticCandidate
            {
                Word = item.Word,
                Score = Round(item.Score, 6),
                Breakdown = item.Breakdown is null ? null : RoundBreakdown(item.Breakdown),
            });
        }

        return new SwipeDiagnosticWord
        {
            Expected = expected,
            Decoded = capture.Chosen,
            Ok = null,
            HitKeys = [.. capture.HitKeys.Select(c => char.ToLowerInvariant(c).ToString())],
            Path = path,
            Candidates = candidates,
            Layout = capture.Layout,
            KeyboardScale = capture.KeyboardScale,
            OriginDip = new SwipeDiagPoint { X = Round(origin.X, 3), Y = Round(origin.Y, 3) },
            PitchDip = Round(pitch, 4),
            Centers = centers,
            TimestampUtc = capture.TimestampUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            DecodeMs = capture.DecodeMs is > 0 ? Round(capture.DecodeMs.Value, 1) : null,
            GestureOrdinal = capture.GestureOrdinal > 0 ? capture.GestureOrdinal : null,
            CaptureLostCount = capture.CaptureLostCount,
            TrailPointCount = capture.TrailPointCount > 0
                ? capture.TrailPointCount
                : capture.PathDip.Count,
            GestureAborted = capture.GestureAborted,
            TimeSincePreviousSwipeMs = capture.TimeSincePreviousSwipeMs is > 0
                ? Round(capture.TimeSincePreviousSwipeMs.Value, 1)
                : null,
            PointerId = capture.PointerId,
            TrailClearedMidGesture = capture.TrailClearedMidGesture,
        };
    }

    public static bool MatchesExpected(string expected, string? decoded)
    {
        if (string.IsNullOrEmpty(decoded))
        {
            return false;
        }

        string left = LanguageModel.FoldKey(expected);
        string right = LanguageModel.FoldKey(decoded);
        return left.Length > 0 && left == right;
    }

    public static bool? PhraseOk(IReadOnlyList<SwipeDiagnosticWord> words)
    {
        if (words.Count == 0)
        {
            return null;
        }

        bool anyFail = false;
        bool anyUnmarked = false;
        foreach (SwipeDiagnosticWord word in words)
        {
            if (word.Ok == false)
            {
                anyFail = true;
            }
            else if (word.Ok is null)
            {
                anyUnmarked = true;
            }
        }

        if (anyFail)
        {
            return false;
        }

        return anyUnmarked ? null : true;
    }

    public static List<SwipeDiagnosticPhraseResult> GroupPhrases(IReadOnlyList<SwipeDiagnosticWord> words)
    {
        var phrases = new List<SwipeDiagnosticPhraseResult>();
        foreach (SwipeDiagnosticWord word in words)
        {
            string id = string.IsNullOrEmpty(word.PhraseId) ? word.Expected : word.PhraseId;
            string text = string.IsNullOrEmpty(word.Phrase) ? word.Expected : word.Phrase;
            if (phrases.Count == 0 || phrases[^1].Id != id)
            {
                phrases.Add(new SwipeDiagnosticPhraseResult
                {
                    Id = id,
                    Text = text,
                    Words = [],
                });
            }

            phrases[^1].Words.Add(word);
        }

        foreach (SwipeDiagnosticPhraseResult phrase in phrases)
        {
            phrase.Ok = PhraseOk(phrase.Words);
        }

        return phrases;
    }

    public static List<SwipeDiagStep> ExpandSteps(IReadOnlyList<SwipeDiagPhrase> phrases)
    {
        var steps = new List<SwipeDiagStep>();
        for (int p = 0; p < phrases.Count; p++)
        {
            SwipeDiagPhrase phrase = phrases[p];
            for (int w = 0; w < phrase.Words.Count; w++)
            {
                steps.Add(new SwipeDiagStep
                {
                    PhraseId = phrase.Id,
                    Phrase = phrase.Text,
                    PhraseIndex = p,
                    PhraseCount = phrases.Count,
                    WordIndex = w,
                    WordCount = phrase.Words.Count,
                    Expected = phrase.Words[w],
                });
            }
        }

        return steps;
    }

    public static SwipeDiagPhrase ParsePhrase(string id, string text)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new SwipeDiagPhrase
        {
            Id = id,
            Text = text,
            Words = words,
        };
    }

    public static string FormatChainLine(SwipeDiagnosticWord word)
    {
        var parts = new List<string>();
        bool trouble = false;
        if (word.CaptureLostCount is > 0)
        {
            parts.Add("CaptureLost ×" + word.CaptureLostCount.Value.ToString(CultureInfo.InvariantCulture));
            trouble = true;
        }

        if (word.TrailPointCount is > 0)
        {
            parts.Add("pts " + word.TrailPointCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (word.TimeSincePreviousSwipeMs is > 0)
        {
            parts.Add("Δt " + word.TimeSincePreviousSwipeMs.Value.ToString("0", CultureInfo.InvariantCulture) + " ms");
        }

        if (word.GestureAborted == true)
        {
            parts.Add("geste aborté");
            trouble = true;
        }

        if (word.TrailClearedMidGesture == true)
        {
            parts.Add("tracé effacé en cours");
            trouble = true;
        }

        if (!trouble)
        {
            return parts.Count == 0
                ? "chaîne OK (pas de CaptureLost)"
                : "chaîne OK · " + string.Join(" · ", parts);
        }

        return string.Join(" · ", parts);
    }

    public static string LayoutLabel(string? layoutId)
    {
        if (string.Equals(layoutId, "en-qwerty", StringComparison.OrdinalIgnoreCase))
        {
            return "QWERTY";
        }

        if (string.Equals(layoutId, "symbols", StringComparison.OrdinalIgnoreCase))
        {
            return "symbols";
        }

        return "AZERTY";
    }

    private static string UniquePath(string directory, DateTime timestamp)
    {
        string path = Path.Combine(directory, FileName(timestamp));
        if (!File.Exists(path))
        {
            return path;
        }

        for (int n = 2; n < 100; n++)
        {
            string candidate = Path.Combine(
                directory,
                string.Create(CultureInfo.InvariantCulture, $"swipe-{timestamp:yyyyMMdd-HHmmss}-{n}.json"));
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(
            directory,
            string.Create(CultureInfo.InvariantCulture, $"swipe-{timestamp:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"));
    }

    private static SwipeScoreBreakdown RoundBreakdown(SwipeScoreBreakdown b) => new()
    {
        Spatial = Round(b.Spatial, 6),
        Dtw = Round(b.Dtw, 6),
        Location = Round(b.Location, 6),
        Length = Round(b.Length, 6),
        Anchors = Round(b.Anchors, 6),
        HitKeys = Round(b.HitKeys, 6),
        Language = Round(b.Language, 6),
    };

    private static double Round(double value, int digits = 5) =>
        Math.Round(value, digits, MidpointRounding.AwayFromZero);
}

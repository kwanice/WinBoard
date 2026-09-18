using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class SwipeDiagnosticTests
{
    [Fact]
    public void DefaultPhrases_AreFrEnChains_WithOptionalShortWarmups()
    {
        IReadOnlyList<SwipeDiagPhrase> phrases = SwipeDiagnostic.DefaultPhrases;
        Assert.Contains(phrases, p => p.Text == "je vais au marché" && p.Words.Count == 4);
        Assert.Contains(phrases, p => p.Text == "bonjour comment ça va" && p.Words.Count == 4);
        Assert.Contains(phrases, p => p.Text == "the quick brown fox" && p.Words.Count == 4);
        Assert.Contains(phrases, p => p.Text == "I need a coffee" && p.Words.Count == 4);
        Assert.Contains(phrases, p => p.Words.Count == 1 && p.Words[0] == "oui");
        Assert.Contains(phrases, p => p.Words.Count == 1 && p.Words[0] == "keyboard");
        Assert.DoesNotContain(phrases.SelectMany(p => p.Words), w => w is "collent" or "content");
        Assert.Equal(3, SwipeDiagnostic.SchemaVersion);
        Assert.Equal("key-pitch", SwipeDiagnostic.CoordinateSpace);

        var run = new SwipeDiagnosticRun();
        Assert.Equal("je", run.CurrentExpected);
        Assert.Equal("je vais au marché", run.CurrentPhrase);
        Assert.Equal(0, run.CurrentWordIndex);
        Assert.Equal(4, run.CurrentWordCount);
    }

    [Fact]
    public void MatchesExpected_FoldsAccentsAndCase()
    {
        Assert.True(SwipeDiagnostic.MatchesExpected("ça", "ca"));
        Assert.True(SwipeDiagnostic.MatchesExpected("I", "i"));
        Assert.True(SwipeDiagnostic.MatchesExpected("va", "va"));
        Assert.True(SwipeDiagnostic.MatchesExpected("a", "a"));
        Assert.True(SwipeDiagnostic.MatchesExpected("marché", "marche"));
        Assert.False(SwipeDiagnostic.MatchesExpected("hello", "jello"));
        Assert.False(SwipeDiagnostic.MatchesExpected("hello", null));
    }

    [Fact]
    public void PhraseOk_TrueOnlyWhenEveryWordPassed()
    {
        Assert.True(SwipeDiagnostic.PhraseOk(
        [
            new SwipeDiagnosticWord { Expected = "a", Ok = true },
            new SwipeDiagnosticWord { Expected = "b", Ok = true },
        ]));
        Assert.False(SwipeDiagnostic.PhraseOk(
        [
            new SwipeDiagnosticWord { Expected = "a", Ok = true },
            new SwipeDiagnosticWord { Expected = "b", Ok = false },
        ]));
        Assert.Null(SwipeDiagnostic.PhraseOk(
        [
            new SwipeDiagnosticWord { Expected = "a", Ok = true },
            new SwipeDiagnosticWord { Expected = "b", Ok = null },
        ]));
    }

    [Fact]
    public void SerializeParse_RoundtripsSchemaV1_IncludingNullOk()
    {
        var document = new SwipeDiagnosticDocument
        {
            SchemaVersion = 1,
            AppVersion = "0.8.4",
            Layout = "AZERTY",
            KeyboardScale = 1.25,
            CoordinateSpace = SwipeDiagnostic.CoordinateSpace,
            CoordinateSpaceNote = SwipeDiagnostic.CoordinateSpaceNote,
            Words =
            [
                new SwipeDiagnosticWord
                {
                    Expected = "comment",
                    Decoded = "comment",
                    Ok = true,
                    HitKeys = ["c", "o", "m", "e", "n", "t"],
                    Path =
                    [
                        new SwipeDiagPoint { X = 2.0, Y = 1.0, T = 0 },
                        new SwipeDiagPoint { X = 8.0, Y = 1.05, T = 40 },
                    ],
                    Candidates =
                    [
                        new SwipeDiagnosticCandidate
                        {
                            Word = "comment",
                            Score = 0.42,
                            Breakdown = new SwipeScoreBreakdown
                            {
                                Spatial = 0.31,
                                Dtw = 0.12,
                                Location = 0.19,
                                Length = 0,
                                Anchors = 0.02,
                                HitKeys = -0.22,
                                Language = 0.11,
                            },
                        },
                    ],
                    Notes = "clear M",
                    Layout = "AZERTY",
                    KeyboardScale = 1.25,
                    OriginDip = new SwipeDiagPoint { X = 12.5, Y = 80 },
                    PitchDip = 48.2,
                    Centers = new Dictionary<string, SwipeDiagPoint>
                    {
                        ["a"] = new() { X = 0, Y = 0 },
                        ["m"] = new() { X = 9, Y = 1 },
                    },
                    TimestampUtc = "2026-09-18T06:29:01.0000000Z",
                },
                new SwipeDiagnosticWord
                {
                    Expected = "hello",
                    Decoded = "jello",
                    Ok = false,
                    HitKeys = ["h", "e", "l", "o"],
                    Path = [new SwipeDiagPoint { X = 0.1, Y = 0.2 }],
                    Candidates = [new SwipeDiagnosticCandidate { Word = "jello", Score = 0.9 }],
                },
                new SwipeDiagnosticWord
                {
                    Expected = "merci",
                    Ok = null,
                },
            ],
        };

        string json = SwipeDiagnostic.Serialize(document);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"appVersion\": \"0.8.4\"", json);
        Assert.Contains("\"hitKeys\"", json);
        Assert.Contains("\"coordinateSpace\": \"key-pitch\"", json);
        Assert.Contains("\"ok\": true", json);
        Assert.Contains("\"ok\": false", json);
        Assert.Contains("\"ok\": null", json);
        Assert.Contains("\"breakdown\"", json);
        Assert.Contains("\"location\"", json);

        SwipeDiagnosticDocument? parsed = SwipeDiagnostic.Parse(json);
        Assert.NotNull(parsed);
        Assert.Equal(1, parsed.SchemaVersion);
        Assert.Equal("0.8.4", parsed.AppVersion);
        Assert.Equal("AZERTY", parsed.Layout);
        Assert.Equal(1.25, parsed.KeyboardScale);
        Assert.Equal(3, parsed.Words.Count);
        Assert.True(parsed.Words[0].Ok);
        Assert.False(parsed.Words[1].Ok);
        Assert.Null(parsed.Words[2].Ok);
        Assert.Equal("comment", parsed.Words[0].Expected);
        Assert.Equal("comment", parsed.Words[0].Decoded);
        Assert.Equal(new[] { "c", "o", "m", "e", "n", "t" }, parsed.Words[0].HitKeys);
        Assert.Equal(2, parsed.Words[0].Path.Count);
        Assert.Equal(0, parsed.Words[0].Path[0].T);
        Assert.Equal("comment", parsed.Words[0].Candidates[0].Word);
        Assert.NotNull(parsed.Words[0].Candidates[0].Breakdown);
        SwipeScoreBreakdown breakdown = parsed.Words[0].Candidates[0].Breakdown!;
        Assert.Equal(0.19, breakdown.Location);
        Assert.Equal(-0.22, breakdown.HitKeys);
        Assert.Equal("clear M", parsed.Words[0].Notes);
        Assert.Equal(9, parsed.Words[0].Centers!["m"].X);
    }

    [Fact]
    public void Parse_RejectsWrongSchemaOrMissingAppVersion()
    {
        Assert.Null(SwipeDiagnostic.Parse("""{ "schemaVersion": 99, "appVersion": "0.8.4", "words": [] }"""));
        Assert.Null(SwipeDiagnostic.Parse("""{ "schemaVersion": 1, "appVersion": "", "words": [] }"""));
        Assert.Null(SwipeDiagnostic.Parse("not json"));
        Assert.Null(SwipeDiagnostic.Parse("   "));
        Assert.Null(SwipeDiagnostic.Parse("""{ "schemaVersion": 1, "words": [] }"""));
    }

    [Fact]
    public void KeyPitchCoords_RoundtripAgainstLayoutOriginAndPitch()
    {
        var centers = new Dictionary<char, Point2>
        {
            ['a'] = new(10, 20),
            ['m'] = new(60, 70),
        };
        Point2 origin = SwipeDiagnostic.OriginDip(centers);
        Assert.Equal(new Point2(10, 20), origin);

        const double pitch = 50;
        Point2 dip = new(60, 70);
        Point2 pitchCoords = SwipeDiagnostic.ToPitch(dip, origin, pitch);
        Assert.Equal(1.0, pitchCoords.X, 6);
        Assert.Equal(1.0, pitchCoords.Y, 6);
        Point2 back = SwipeDiagnostic.FromPitch(pitchCoords, origin, pitch);
        Assert.Equal(dip.X, back.X, 6);
        Assert.Equal(dip.Y, back.Y, 6);
    }

    [Fact]
    public void FromGesture_StoresPathInKeyPitch_WithHitKeysAndCandidates()
    {
        var centers = new Dictionary<char, Point2>
        {
            ['c'] = new(0, 0),
            ['o'] = new(60, 0),
            ['m'] = new(120, 0),
        };
        var capture = new SwipeGestureCapture
        {
            PathDip = [new Point2(0, 0), new Point2(120, 0)],
            PathElapsedMs = [0, 32],
            HitKeys = ['c', 'o', 'm'],
            Candidates =
            [
                new ExplainedSwipe
                {
                    Word = "com",
                    Score = 0.5,
                    Breakdown = new SwipeScoreBreakdown { Location = 0.1, Dtw = 0.2, Spatial = 0.3 },
                },
            ],
            Chosen = "com",
            Layout = "AZERTY",
            KeyboardScale = 1,
            PitchDip = 60,
            CentersDip = centers,
            TimestampUtc = DateTimeOffset.Parse("2026-09-18T12:00:00Z"),
            CaptureLostCount = 2,
            TrailPointCount = 2,
            TimeSincePreviousSwipeMs = 180,
            PointerId = 7,
            GestureOrdinal = 3,
        };

        SwipeDiagnosticWord word = SwipeDiagnostic.FromGesture("comment", capture);
        Assert.Equal("comment", word.Expected);
        Assert.Equal("com", word.Decoded);
        Assert.Null(word.Ok);
        Assert.Equal(new[] { "c", "o", "m" }, word.HitKeys);
        Assert.Equal(2, word.Path.Count);
        Assert.Equal(0, word.Path[0].X);
        Assert.Equal(2, word.Path[1].X);
        Assert.Equal(32, word.Path[1].T);
        Assert.Equal(2, word.TrailPointCount);
        Assert.False(word.GestureAborted);
        Assert.False(word.TrailClearedMidGesture);
        Assert.Equal(2, word.CaptureLostCount);
        Assert.Equal(180, word.TimeSincePreviousSwipeMs);
        Assert.Equal(7u, word.PointerId);
        Assert.Equal(3, word.GestureOrdinal);
        Assert.Equal(0, word.Centers!["c"].X);
        Assert.Equal(2, word.Centers["m"].X);
        Assert.Equal(60, word.PitchDip);
        Assert.Equal("AZERTY", word.Layout);
        Assert.Single(word.Candidates);
        Assert.Equal(0.1, word.Candidates[0].Breakdown!.Location);
    }

    [Fact]
    public void Run_CaptureOverwrite_PassFailSkipRetryNext_AndExportPath()
    {
        var run = new SwipeDiagnosticRun(["comment", "bonjour", "hello"]);
        Assert.Equal("comment", run.CurrentExpected);
        Assert.Equal(1, run.DisplayIndex);

        run.SetPending(Word("comment", "collent"));
        run.SetPending(Word("comment", "comment"));
        Assert.Equal("comment", run.Pending!.Decoded);
        Assert.True(run.TryPass());
        Assert.True(run.Committed[0].Ok);
        Assert.Equal("comment", run.Committed[0].Decoded);
        Assert.Equal("bonjour", run.CurrentExpected);

        Assert.True(run.TryFail());
        Assert.False(run.Committed[0].Ok);
        Assert.True(run.TryMarkLast(true));
        Assert.True(run.Committed[0].Ok);
        run.SetPending(Word("bonjour", "bonjour"));
        Assert.True(run.TryRetry());
        Assert.Null(run.Pending);
        Assert.Equal("bonjour", run.CurrentExpected);
        Assert.True(run.TrySkip());
        Assert.Null(run.Committed[1].Ok);
        Assert.Null(run.Committed[1].Decoded);
        Assert.Equal("hello", run.CurrentExpected);

        run.SetPending(Word("hello", "hello"));
        Assert.True(run.TryNext());
        Assert.True(run.IsComplete);
        Assert.Null(run.Committed[2].Ok);
        Assert.Equal("hello", run.Committed[2].Decoded);

        run.Restart();
        Assert.False(run.IsComplete);
        Assert.Equal("comment", run.CurrentExpected);
        Assert.Empty(run.Committed);

        string root = Path.Combine(Path.GetTempPath(), "winboard-diag-" + Guid.NewGuid().ToString("N"));
        try
        {
            run.SetPending(Word("comment", "comment"));
            run.TryPass();
            SwipeDiagnosticDocument doc = run.ToDocument("0.8.4", "AZERTY", 1.0);
            var timestamp = new DateTime(2026, 9, 18, 14, 5, 7);
            string path = SwipeDiagnostic.WriteExport(doc, timestamp, root);
            Assert.Equal(
                Path.Combine(root, "WinBoard", "diagnostics", "swipe-20260918-140507.json"),
                path);
            Assert.True(File.Exists(path));
            SwipeDiagnosticDocument? parsed = SwipeDiagnostic.Parse(File.ReadAllText(path));
            Assert.NotNull(parsed);
            Assert.Equal("0.8.4", parsed.AppVersion);
            Assert.Equal("comment", parsed.Words[0].Expected);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FileName_UsesLocalTimestampPattern()
    {
        Assert.Equal(
            "swipe-20260918-140507.json",
            SwipeDiagnostic.FileName(new DateTime(2026, 9, 18, 14, 5, 7)));
        Assert.Equal("QWERTY", SwipeDiagnostic.LayoutLabel("en-qwerty"));
        Assert.Equal("AZERTY", SwipeDiagnostic.LayoutLabel("fr-azerty"));
        Assert.Equal(
            "Chemin copié : C:\\tmp\\swipe.json",
            SwipeDiagnostic.FormatExportStatus(@"C:\tmp\swipe.json", copied: true));
        Assert.Contains("impossible", SwipeDiagnostic.FormatExportStatus(@"C:\tmp\swipe.json", copied: false));
    }

    [Fact]
    public void Explain_MatchesDecodeRanking_AndExposesBreakdown()
    {
        Dictionary<char, Point2> centers = SwipeDecoderTests.AzertyCenters();
        WordList words = WordList.FromOrderedWords(["comment", "content", "collent", "comme"]);
        IReadOnlyList<Point2> path = SwipeDecoderTests.PathAlong("comment", centers);
        char[] hits = ['c', 'o', 'm', 'e', 'n', 't'];

        IReadOnlyList<string> decoded = SwipeDecoder.Decode(hits, path, centers, words, 60);
        IReadOnlyList<ExplainedSwipe> explained = SwipeDecoder.Explain(
            hits, path, centers, words, 60, includeBreakdown: true);

        Assert.Equal(decoded, explained.Select(e => e.Word).ToArray());
        Assert.NotEmpty(explained);
        Assert.Equal("comment", explained[0].Word);
        Assert.NotNull(explained[0].Breakdown);
        SwipeScoreBreakdown parts = explained[0].Breakdown!;
        Assert.True(parts.Location >= 0);
        Assert.True(parts.Dtw >= 0);
        Assert.True(parts.Spatial >= 0);
    }

    [Fact]
    public void PhraseRun_RecordsEachWordWithChainFields_AndPhraseOk()
    {
        var phrases = new[]
        {
            SwipeDiagnostic.ParsePhrase("p1", "je vais au marché"),
            SwipeDiagnostic.ParsePhrase("p2", "the quick brown fox"),
        };
        var run = new SwipeDiagnosticRun(phrases);
        Assert.Equal("je", run.CurrentExpected);
        Assert.Equal(1, run.CurrentPhraseNumber);

        Assert.True(run.TryRecordCapture(new SwipeDiagnosticWord
        {
            Decoded = "je",
            CaptureLostCount = 0,
            TrailPointCount = 12,
            GestureOrdinal = 1,
        }));
        Assert.Equal("vais", run.CurrentExpected);
        Assert.True(run.Committed[0].Ok);
        Assert.Equal("p1", run.Committed[0].PhraseId);
        Assert.Equal(0, run.Committed[0].WordIndex);
        Assert.Equal(4, run.Committed[0].WordCount);

        Assert.True(run.TryRecordCapture(new SwipeDiagnosticWord
        {
            Decoded = "vais",
            CaptureLostCount = 2,
            TrailPointCount = 4,
            GestureAborted = true,
            TrailClearedMidGesture = true,
            TimeSincePreviousSwipeMs = 90,
            PointerId = 3,
            GestureOrdinal = 2,
            Reason = SwipeAbortReason.CaptureLost,
        }));
        Assert.Equal("vais", run.CurrentExpected);
        Assert.Single(run.Committed);
        Assert.Single(run.AbortedGestures);
        Assert.False(run.AbortedGestures[0].Ok);
        Assert.Equal("vais", run.AbortedGestures[0].Expected);

        run.TryRecordCapture(new SwipeDiagnosticWord { Decoded = "vais", TrailPointCount = 20, GestureOrdinal = 3 });
        run.TryRecordCapture(new SwipeDiagnosticWord { Decoded = "au", TrailPointCount = 8, GestureOrdinal = 4 });
        run.TryRecordCapture(new SwipeDiagnosticWord { Decoded = "marche", TrailPointCount = 15, GestureOrdinal = 5 });
        Assert.Equal("the", run.CurrentExpected);
        Assert.Equal("the quick brown fox", run.CurrentPhrase);

        SwipeDiagnosticDocument doc = run.ToDocument("0.8.15", "AZERTY", 1);
        Assert.Equal(3, doc.SchemaVersion);
        Assert.NotNull(doc.Phrases);
        Assert.Equal("je vais au marché", doc.Phrases[0].Text);
        Assert.True(doc.Phrases[0].Ok);
        Assert.Equal(4, doc.Phrases[0].Words.Count);
        Assert.Equal("marche", doc.Phrases[0].Words[3].Decoded);
        Assert.True(SwipeDiagnostic.MatchesExpected("marché", "marche"));
        Assert.Single(doc.AbortedGestures);

        string json = SwipeDiagnostic.Serialize(doc);
        Assert.Contains("\"schemaVersion\": 3", json);
        Assert.Contains("\"phraseId\": \"p1\"", json);
        Assert.Contains("\"abortedGestures\"", json);
        Assert.Contains("\"captureLostCount\"", json);
        Assert.Contains("\"trailPointCount\"", json);
        SwipeDiagnosticDocument? parsed = SwipeDiagnostic.Parse(json);
        Assert.NotNull(parsed);
        Assert.Equal(3, parsed.SchemaVersion);
        Assert.Equal("p1", parsed.Words[0].PhraseId);
        Assert.True(parsed.Phrases![0].Ok);
        Assert.Single(parsed.AbortedGestures);
    }

    [Fact]
    public void AbortedCapture_ExportsChainMetadata_AndPhraseNotOk()
    {
        var run = new SwipeDiagnosticRun(
        [
            SwipeDiagnostic.ParsePhrase("p1", "je vais au marché"),
        ]);
        run.TryRecordCapture(new SwipeDiagnosticWord
        {
            Decoded = "je",
            TrailPointCount = 10,
            GestureOrdinal = 1,
            CaptureLostCount = 0,
            GestureAborted = false,
        });
        run.TryRecordCapture(new SwipeDiagnosticWord
        {
            Decoded = null,
            CaptureLostCount = 1,
            TrailPointCount = 3,
            GestureAborted = true,
            TrailClearedMidGesture = true,
            TimeSincePreviousSwipeMs = 70,
            PointerId = 4,
            GestureOrdinal = 2,
            Reason = SwipeAbortReason.CaptureLost,
        });

        Assert.Equal("vais", run.CurrentExpected);
        Assert.Single(run.Committed);
        SwipeDiagnosticDocument doc = run.ToDocument("0.8.15", "AZERTY", 1);
        Assert.Equal(3, doc.SchemaVersion);
        Assert.Single(doc.AbortedGestures);
        SwipeDiagnosticWord aborted = doc.AbortedGestures[0];
        Assert.Equal("vais", aborted.Expected);
        Assert.False(aborted.Ok);
        Assert.Equal(1, aborted.CaptureLostCount);
        Assert.Equal(3, aborted.TrailPointCount);
        Assert.True(aborted.GestureAborted);
        Assert.True(aborted.TrailClearedMidGesture);
        Assert.Equal(70, aborted.TimeSincePreviousSwipeMs);
        Assert.Equal(4u, aborted.PointerId);
        Assert.Equal(2, aborted.GestureOrdinal);
        Assert.Equal("p1", aborted.PhraseId);
        Assert.Equal(1, aborted.WordIndex);
        Assert.Equal(SwipeAbortReason.CaptureLost, aborted.Reason);
        Assert.Equal("chaîne OK · pts 10", SwipeDiagnostic.FormatChainLine(doc.Words[0]));
        Assert.Contains("CaptureLost ×1", SwipeDiagnostic.FormatChainLine(aborted));
        Assert.Contains("geste aborté", SwipeDiagnostic.FormatChainLine(aborted));
        Assert.Contains("tracé effacé en cours", SwipeDiagnostic.FormatChainLine(aborted));

        string json = SwipeDiagnostic.Serialize(doc);
        Assert.Contains("\"schemaVersion\": 3", json);
        Assert.Contains("\"abortedGestures\"", json);
        Assert.Contains("\"gestureAborted\": true", json);
        Assert.Contains("\"trailClearedMidGesture\": true", json);
        Assert.Contains("\"captureLostCount\": 1", json);
        Assert.Contains("\"timeSincePreviousSwipeMs\": 70", json);
        Assert.Contains("\"pointerId\": 4", json);
        Assert.Contains("\"reason\": \"captureLost\"", json);
        Assert.Contains("\"wordIndex\": 1", json);
    }

    [Fact]
    public void TryRecordCapture_MarksVaTrueWhenDecodedMatches()
    {
        var run = new SwipeDiagnosticRun(
        [
            SwipeDiagnostic.ParsePhrase("p", "bonjour comment ça va"),
        ]);
        run.TryRecordCapture(new SwipeDiagnosticWord { Decoded = "bonjour" });
        run.TryRecordCapture(new SwipeDiagnosticWord { Decoded = "comment" });
        run.TryRecordCapture(new SwipeDiagnosticWord { Decoded = "ça" });
        run.TryRecordCapture(new SwipeDiagnosticWord { Decoded = "va", Ok = false });
        Assert.True(run.Committed[3].Ok);
        Assert.Equal("va", run.Committed[3].Expected);
        Assert.True(run.IsComplete);
        Assert.True(run.ToDocument("0.8.15", "AZERTY", 1).Phrases![0].Ok);
    }

    private static SwipeDiagnosticWord Word(string expected, string decoded) => new()
    {
        Expected = expected,
        Decoded = decoded,
        HitKeys = ["x"],
        Path = [new SwipeDiagPoint { X = 0, Y = 0, T = 0 }],
    };
}

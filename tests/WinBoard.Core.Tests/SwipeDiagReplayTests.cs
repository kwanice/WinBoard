using System.Globalization;
using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

/// <summary>
/// Replays recorded AZERTY diagnostic sessions (local JSON fixtures).
/// Paths are key-pitch; converted back to layout DIP via origin+pitch.
/// </summary>
public sealed class SwipeDiagReplayTests
{
    private static readonly Lazy<(WordList Words, LanguageModel Language)> Lexicon = new(LoadLexicon);
    private static readonly Lazy<SwipeDiagnosticDocument> Doc084 = new(() => LoadDoc("diag-azerty-0.8.4.json"));
    private static readonly Lazy<SwipeDiagnosticDocument> Doc089 = new(() => LoadDoc("diag-azerty-0.8.9.json"));

    public static TheoryData<string, string> ExpectedWords
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (SwipeDiagnosticWord word in Doc084.Value.Words)
            {
                data.Add("diag-azerty-0.8.4.json", word.Expected);
            }

            foreach (SwipeDiagnosticWord word in Doc089.Value.Words)
            {
                data.Add("diag-azerty-0.8.9.json", word.Expected);
            }

            return data;
        }
    }

    [Fact]
    public void Fixture_HasThirteenAzertyTrials()
    {
        SwipeDiagnosticDocument doc = Doc084.Value;
        Assert.Equal(13, doc.Words.Count);
        Assert.Equal("AZERTY", doc.Layout);
        Assert.Contains("azerty", Lexicon.Value.Words.All.Select(e => e.Word), StringComparer.OrdinalIgnoreCase);
        Assert.True(Lexicon.Value.Words.Contains("swipe"));
        Assert.True(Lexicon.Value.Words.Contains("qwerty"));
        Assert.True(Lexicon.Value.Words.Contains("thanks"));
        Assert.True(Lexicon.Value.Words.Contains("windows"));
        Assert.True(Lexicon.Value.Words.Contains("hello"));
    }

    [Fact]
    public void Fixture089_HasFourteenAzertyTrials_IncludingFranceAndKeyboard()
    {
        SwipeDiagnosticDocument doc = Doc089.Value;
        Assert.Equal(14, doc.Words.Count);
        Assert.Equal("AZERTY", doc.Layout);
        Assert.Contains("france", doc.Words.Select(w => w.Expected), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("keyboard", doc.Words.Select(w => w.Expected), StringComparer.OrdinalIgnoreCase);
        Assert.True(Lexicon.Value.Words.Contains("france"));
        Assert.True(Lexicon.Value.Words.Contains("keyboard"));
        Assert.DoesNotContain("comment", doc.Words.Select(w => w.Expected), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(ExpectedWords))]
    public void RecordedPath_RanksExpectedFirst(string fixtureFile, string expected)
    {
        SwipeDiagnosticDocument doc = fixtureFile.Contains("0.8.9", StringComparison.Ordinal)
            ? Doc089.Value
            : Doc084.Value;
        (WordList words, LanguageModel language) = Lexicon.Value;
        SwipeDiagnosticWord trial = doc.Words.First(w =>
            string.Equals(w.Expected, expected, StringComparison.Ordinal));
        (List<Point2> path, Dictionary<char, Point2> centers, List<char> hits, double pitch) = ToDecoderInput(trial);

        IReadOnlyList<ExplainedSwipe> ranked = SwipeDecoder.Explain(
            hits,
            path,
            centers,
            words,
            pitch,
            maxResults: 40,
            language: language,
            includeBreakdown: true);

        string top = string.Join(", ", ranked.Take(10).Select(r => Format(r)));
        Assert.True(ranked.Count > 0, expected + " produced no candidates. Hits=" + string.Join("", hits));
        int at = -1;
        for (int i = 0; i < ranked.Count; i++)
        {
            if (string.Equals(ranked[i].Word, expected, StringComparison.OrdinalIgnoreCase))
            {
                at = i;
                break;
            }
        }

        string expectedLine = at >= 0 ? Format(ranked[at]) : "ABSENT";
        Assert.True(
            at == 0,
            fixtureFile + " " + expected + " rank=" + (at < 0 ? "none" : (at + 1).ToString())
            + " (" + expectedLine + ") | " + top);
    }

    private static (WordList Words, LanguageModel Language) LoadLexicon()
    {
        WordList words = WordList.LoadBilingual();
        LanguageModel language = LanguageModel.LoadBilingual(words);
        return (words, language);
    }

    private static SwipeDiagnosticDocument LoadDoc(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        SwipeDiagnosticDocument? doc = SwipeDiagnostic.Parse(File.ReadAllText(path));
        Assert.NotNull(doc);
        return doc;
    }

    internal static (List<Point2> Path, Dictionary<char, Point2> Centers, List<char> Hits, double Pitch)
        ToDecoderInput(SwipeDiagnosticWord trial)
    {
        Assert.NotNull(trial.OriginDip);
        Assert.NotNull(trial.Centers);
        double pitch = trial.PitchDip is > 1e-6 ? trial.PitchDip.Value : 1;
        var origin = new Point2(trial.OriginDip.X, trial.OriginDip.Y);
        var path = new List<Point2>(trial.Path.Count);
        foreach (SwipeDiagPoint p in trial.Path)
        {
            path.Add(SwipeDiagnostic.FromPitch(new Point2(p.X, p.Y), origin, pitch));
        }

        var centers = new Dictionary<char, Point2>();
        foreach ((string key, SwipeDiagPoint p) in trial.Centers)
        {
            if (key.Length == 0)
            {
                continue;
            }

            centers[char.ToLowerInvariant(key[0])] =
                SwipeDiagnostic.FromPitch(new Point2(p.X, p.Y), origin, pitch);
        }

        var hits = new List<char>(trial.HitKeys.Count);
        foreach (string h in trial.HitKeys)
        {
            if (h.Length > 0)
            {
                hits.Add(char.ToLowerInvariant(h[0]));
            }
        }

        return (path, centers, hits, pitch);
    }

    private static string Format(ExplainedSwipe r)
    {
        SwipeScoreBreakdown? b = r.Breakdown;
        if (b is null)
        {
            return r.Word + "=" + r.Score.ToString("F3", CultureInfo.InvariantCulture);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{r.Word}={r.Score:F3}[loc={b.Location:F2} dtw={b.Dtw:F2} len={b.Length:F2} anc={b.Anchors:F2} hit={b.HitKeys:F2} lm={b.Language:F2}]");
    }
}

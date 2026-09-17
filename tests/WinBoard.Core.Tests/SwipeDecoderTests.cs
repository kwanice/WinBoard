using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

/// <summary>
/// Geometric regressions for the perennial pipeline
/// (spatial → trie/dict beam + DTW → n-gram LM). Layout is a simplified
/// AZERTY/QWERTY grid (key size 60). No per-word blacklists.
/// <code>
/// y=0  a z e r t y u i o p     (x = 0,60,...,540)
/// y=60 q s d f g h j k l m
/// y=120      w x c v b n       (x starts at 90)
/// </code>
/// </summary>
public sealed class SwipeDecoderTests
{
    private const double KeySize = 60;

    [Fact]
    public void IdealBonjourPath_RanksBonjourAboveLongerDivergentAlternative()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            // Listed first so frequency would favor the long word if geometry lost.
            "bougainvillier",
            "bouillonne",
            "bonjour",
            "bonsoir",
            "bouton",
        ]);

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['b', 'o', 'n', 'j', 'o', 'u', 'r'],
            PathAlong("bonjour", centers),
            centers,
            words,
            KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("bonjour", ranked[0]);
        AssertOutranks(ranked, "bonjour", "bougainvillier");
        AssertOutranks(ranked, "bonjour", "bouillonne");
    }

    [Fact]
    public void FrenchLexicon_IdealBonjourPath_ReturnsBonjour()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.LoadLanguage("fr");
        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['b', 'o', 'n', 'j', 'o', 'u', 'r'],
            PathAlong("bonjour", centers),
            centers,
            words,
            KeySize,
            maxResults: 12);

        Assert.NotEmpty(ranked);
        Assert.Equal("bonjour", ranked[0]);
        AssertOutranks(ranked, "bonjour", "bougainvillier");
    }

    [Fact]
    public void IdealCommentPath_RanksCommentAboveLongerDivergentAlternative()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            "constitutionnellement",
            "content",
            "comment",
            "comme",
            "comte",
            "concert",
        ]);

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 'o', 'm', 'e', 'n', 't'],
            CommentShapedPath(centers),
            centers,
            words,
            KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "constitutionnellement");
        AssertOutranks(ranked, "comment", "content");
    }

    [Fact]
    public void FrenchLexicon_IdealCommentPath_RanksCommentAboveContentWhenBothReturned()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.LoadLanguage("fr");
        Assert.True(words.Contains("comment"));
        Assert.True(words.Contains("content"));

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 'o', 'm', 'e', 'n', 't'],
            CommentShapedPath(centers),
            centers,
            words,
            KeySize,
            maxResults: 40);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "content");
        AssertOutranks(ranked, "comment", "constitutionnellement");
    }

    [Fact]
    public void IdealCommentPath_RanksCommentAboveSameLengthNeighborWords()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            // Frequency would favor the neighbors if geometry lost.
            "collent",
            "colorent",
            "comment",
            "content",
        ]);

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 'o', 'm', 'e', 'n', 't'],
            PathAlong("comment", centers),
            centers,
            words,
            KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "collent");
        AssertOutranks(ranked, "comment", "colorent");
    }

    [Fact]
    public void IdealCommentPath_RanksCommentAboveNeighborsWithoutObservedSequence()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(["collent", "colorent", "comment"]);
        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 't'],
            PathAlong("comment", centers),
            centers,
            words,
            KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "collent");
        AssertOutranks(ranked, "comment", "colorent");
    }

    [Fact]
    public void FrenchLexicon_IdealCommentPath_RanksCommentAboveNeighborConfusions()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.LoadLanguage("fr");
        Assert.True(words.Contains("comment"));
        Assert.True(words.Contains("collent"));
        Assert.True(words.Contains("colorent"));

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 'o', 'm', 'e', 'n', 't'],
            PathAlong("comment", centers),
            centers,
            words,
            KeySize,
            maxResults: 20);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "collent");
        AssertOutranks(ranked, "comment", "colorent");
    }

    [Fact]
    public void HitKeysEnteringM_RanksCommentAboveLNeighborWords()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        IReadOnlyList<Point2> path = PathAlong("comment", centers);
        IReadOnlyList<char> hits = HitKeysAlong(path, centers, KeySize);
        Assert.Contains('m', hits);

        WordList words = WordList.FromOrderedWords(
        [
            "collent",
            "colorent",
            "comment",
            "content",
        ]);
        IReadOnlyList<string> ranked = SwipeDecoder.Decode(hits, path, centers, words, KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "collent");
        AssertOutranks(ranked, "comment", "colorent");
    }

    [Fact]
    public void BandedDtw_CommentTemplate_BeatsLNeighborOnMPath()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        double pitch = SwipeDecoder.ResolvePitch(centers, KeySize);
        Point2[] user = SwipeDecoder.Resample(PathAlong("comment", centers), SwipeDecoder.SampleCount);
        Point2[] comment = SwipeDecoder.Resample(PathAlong("comment", centers), SwipeDecoder.SampleCount);
        Point2[] collent = SwipeDecoder.Resample(PathAlong("collent", centers), SwipeDecoder.SampleCount);

        double intended = BandedDtw.Distance(user, comment, pitch, 1e9);
        double neighbor = BandedDtw.Distance(user, collent, pitch, 1e9);
        Assert.True(intended < neighbor,
            $"DTW comment {intended:F3} should beat collent {neighbor:F3} on an M path");
    }

    [Fact]
    public void SpatialEncoder_AtMLocus_ScoresMAboveL()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        GeometricSpatialEncoder.ScoreKeys(
            centers['m'], centers, KeySize, out char nearest, out double dist);
        Assert.Equal('m', nearest);
        Assert.True(dist < 0.05);

        LetterScore[] top = GeometricSpatialEncoder.ScoreKeys(
            centers['m'], centers, KeySize, out _, out _);
        double m = top.First(s => s.Letter == 'm').Score;
        double l = top.FirstOrDefault(s => s.Letter == 'l').Score;
        Assert.True(m > l, $"M {m:F3} vs L {l:F3} at M center");
    }

    [Fact]
    public void PathNearMWithoutEnteringHitRect_StillRanksCommentAboveLNeighbors()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            "collent",
            "colorent",
            "comment",
            "content",
        ]);

        // Hit-keys missed M (0.7.4 cliff would not fire). The path still
        // visits M, so the soft neighbor radius must keep comment ahead.
        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 'o', 'e', 'n', 't'],
            PathAlong("comment", centers),
            centers,
            words,
            KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "collent");
        AssertOutranks(ranked, "comment", "colorent");
    }

    [Fact]
    public void IdealCommentPath_RanksCommentAboveLongerSameStartEndWords()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            "commenceront",
            "conceptuellement",
            "consciemment",
            "comment",
        ]);

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 'o', 'm', 'e', 'n', 't'],
            PathAlong("comment", centers),
            centers,
            words,
            KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "commenceront");
        AssertOutranks(ranked, "comment", "conceptuellement");
        AssertOutranks(ranked, "comment", "consciemment");
    }

    [Fact]
    public void FrenchLexicon_IdealCommentPath_RanksCommentAboveLongerCtoTWords()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.LoadLanguage("fr");
        Assert.True(words.Contains("comment"));
        Assert.True(words.Contains("commenceront"));
        Assert.True(words.Contains("conceptuellement"));
        Assert.True(words.Contains("consciemment"));

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 'o', 'm', 'e', 'n', 't'],
            PathAlong("comment", centers),
            centers,
            words,
            KeySize,
            maxResults: 12);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "commenceront");
        AssertOutranks(ranked, "comment", "conceptuellement");
        AssertOutranks(ranked, "comment", "consciemment");
    }

    [Fact]
    public void LetterCountRejects_TwelveLettersOnSixHitKeys()
    {
        Assert.True(SwipeDecoder.LetterCountRejects(12, 6));
        Assert.True(SwipeDecoder.LetterCountRejects(16, 7));
        Assert.False(SwipeDecoder.LetterCountRejects(7, 6));
        Assert.False(SwipeDecoder.LetterCountRejects(12, 12));
        Assert.False(SwipeDecoder.LetterCountRejects(9, 6));
    }

    [Fact]
    public void LanguagePrior_CannotReviveLongWordRejectedByLength()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            "commenceront",
            "conceptuellement",
            "comment",
        ]);
        LanguageModel language = LanguageModel.FromTables(
            new Dictionary<string, double>
            {
                ["commenceront"] = 1.0,
                ["conceptuellement"] = 1.0,
                ["comment"] = 0.05,
            },
            new Dictionary<string, IReadOnlyDictionary<string, double>>
            {
                ["et"] = new Dictionary<string, double>
                {
                    ["commenceront"] = 999,
                    ["conceptuellement"] = 999,
                },
            });

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 'o', 'm', 'e', 'n', 't'],
            PathAlong("comment", centers),
            centers,
            words,
            KeySize,
            previousWord: "et",
            language: language);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "commenceront");
        AssertOutranks(ranked, "comment", "conceptuellement");
    }

    [Fact]
    public void LanguagePrior_AfterCommonLeftContext_RanksCommentAboveSpatialRival()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(["collent", "colorent", "comment"]);
        LanguageModel language = LanguageModel.FromTables(
            new Dictionary<string, double>
            {
                ["collent"] = 0.99,
                ["colorent"] = 0.99,
                ["comment"] = 0.10,
            },
            new Dictionary<string, IReadOnlyDictionary<string, double>>
            {
                ["mais"] = new Dictionary<string, double> { ["comment"] = 120 },
            });

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 'o', 'm', 'e', 'n', 't'],
            PathAlong("comment", centers),
            centers,
            words,
            KeySize,
            previousWord: "mais",
            language: language);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "collent");
    }

    [Fact]
    public void LengthRatioRejects_LongTemplateOnShortGesture()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        double pitch = SwipeDecoder.ResolvePitch(centers, KeySize);
        double user = SwipeDecoder.PolylineLength(PathAlong("comment", centers));
        double intended = SwipeDecoder.PolylineLength(PathAlong("comment", centers));
        double longer = SwipeDecoder.PolylineLength(PathAlong("constitutionnellement", centers));

        double self = SwipeDecoder.LengthRatioPenalty(intended, user, pitch);
        double extra = SwipeDecoder.LengthRatioPenalty(longer, user, pitch);
        Assert.True(extra > self, $"length self {self:F3} vs longer {extra:F3}");
        Assert.False(SwipeDecoder.LengthRatioRejects(intended, user, 7, pitch));
        Assert.True(SwipeDecoder.LengthRatioRejects(longer, user, 21, pitch));
    }

    [Fact]
    public void MBiasedPath_RanksCommentAboveContent()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            "content",
            "comment",
            "comme",
            "comte",
            "concert",
        ]);

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 'o', 'm', 'e', 'n', 't'],
            CommentShapedPath(centers),
            centers,
            words,
            KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        AssertOutranks(ranked, "comment", "content");
    }

    [Fact]
    public void NBiasedPath_CanStillRankContent()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(["comment", "content"]);
        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['c', 'o', 'n', 't'],
            ContentShapedPath(centers),
            centers,
            words,
            KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("content", ranked[0]);
    }

    [Fact]
    public void QwertyHelloShapedPath_RanksHelloAboveDistantSameLengthWords()
    {
        Dictionary<char, Point2> centers = QwertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            "ventilo",
            "yellow",
            "hello",
            "helot",
            "jello",
        ]);

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['h', 'e', 'l', 'o'],
            PathAlong("hello", centers),
            centers,
            words,
            KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("hello", ranked[0]);
        AssertOutranks(ranked, "hello", "ventilo");
    }

    [Fact]
    public void EnglishLexicon_HelloShapedPath_ReturnsHello()
    {
        Dictionary<char, Point2> centers = QwertyCenters();
        WordList words = WordList.LoadLanguage("en");
        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['h', 'e', 'l', 'o'],
            PathAlong("hello", centers),
            centers,
            words,
            KeySize,
            maxResults: 12);

        Assert.NotEmpty(ranked);
        Assert.Equal("hello", ranked[0]);
        AssertOutranks(ranked, "hello", "ventilo");
    }

    [Fact]
    public void Ranking_IsInvariantToUniformLayoutScale()
    {
        Dictionary<char, Point2> baseCenters = QwertyCenters();
        IReadOnlyList<Point2> basePath = PathAlong("hello", baseCenters);
        WordList words = WordList.FromOrderedWords(["ventilo", "hello", "yellow", "jello"]);
        char[] hits = ['h', 'e', 'l', 'o'];

        IReadOnlyList<string> at1 = SwipeDecoder.Decode(hits, basePath, baseCenters, words, KeySize);
        const double scale = 1.8;
        Dictionary<char, Point2> scaledCenters = ScaleCenters(baseCenters, scale);
        IReadOnlyList<Point2> scaledPath = ScalePath(basePath, scale);
        IReadOnlyList<string> atScale = SwipeDecoder.Decode(hits, scaledPath, scaledCenters, words, KeySize * scale);

        Assert.Equal(at1[0], atScale[0]);
        Assert.Equal("hello", at1[0]);
    }

    [Fact]
    public void BandedDtw_IdenticalPolylines_IsNearZero()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        double pitch = SwipeDecoder.ResolvePitch(centers, KeySize);
        Point2[] path = SwipeDecoder.Resample(PathAlong("bonjour", centers), SwipeDecoder.SampleCount);
        double distance = BandedDtw.Distance(path, path, pitch, 1e9);
        Assert.True(distance < 1e-6, $"Self DTW {distance}");
    }

    [Fact]
    public void BandedDtw_LongDivergentTemplate_CostsMoreThanIntended()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        double pitch = SwipeDecoder.ResolvePitch(centers, KeySize);
        Point2[] user = SwipeDecoder.Resample(PathAlong("bonjour", centers), SwipeDecoder.SampleCount);
        Point2[] intended = SwipeDecoder.Resample(PathAlong("bonjour", centers), SwipeDecoder.SampleCount);
        Point2[] divergent = SwipeDecoder.Resample(PathAlong("bougainvillier", centers), SwipeDecoder.SampleCount);

        double self = BandedDtw.Distance(user, intended, pitch, 1e9);
        double other = BandedDtw.Distance(user, divergent, pitch, 1e9);
        Assert.True(other > self + 0.15, $"DTW self {self:F3} vs long {other:F3}");
    }

    [Fact]
    public void LbKeogh_DoesNotExceedTrueDtw()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        double pitch = SwipeDecoder.ResolvePitch(centers, KeySize);
        Point2[] user = SwipeDecoder.Resample(PathAlong("comment", centers), SwipeDecoder.SampleCount);
        Point2[] other = SwipeDecoder.Resample(PathAlong("bonjour", centers), SwipeDecoder.SampleCount);
        int band = BandedDtw.BandWidth(user.Length);
        double lb = BandedDtw.LowerBound(user, other, pitch, band);
        double dtw = BandedDtw.Distance(user, other, pitch, 1e9);
        Assert.True(lb <= dtw + 1e-6, $"LB {lb:F3} vs DTW {dtw:F3}");
    }

    [Fact]
    public void Resample_PreservesEndpointsAndCount()
    {
        var path = new List<Point2> { new(0, 0), new(30, 0), new(90, 40) };
        Point2[] sampled = SwipeDecoder.Resample(path, SwipeDecoder.SampleCount);
        Assert.Equal(SwipeDecoder.SampleCount, sampled.Length);
        Assert.Equal(path[0], sampled[0]);
        Assert.Equal(path[^1], sampled[^1]);
    }

    [Fact]
    public void ObservedKeysOnMBiasedPath_IncludeMNotNAfterO()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        Point2[] samples = SwipeDecoder.Resample(CommentShapedPath(centers), SwipeDecoder.SampleCount);
        IReadOnlyList<char> observed = SwipeDecoder.BuildObservedKeys(samples, centers, KeySize);

        Assert.Contains('m', observed);
        int o = observed.ToList().IndexOf('o');
        int m = observed.ToList().IndexOf('m');
        int n = observed.ToList().IndexOf('n');
        Assert.True(o >= 0 && m > o, $"O then M in {string.Join("", observed)}");
        Assert.True(n < 0 || n > m, $"N must not appear between O and M in {string.Join("", observed)}");
    }

    [Fact]
    public void KeyBounds_UseTransformedCorners_NotUnscaledWidth()
    {
        var origin = new Point2(100, 40);
        var scaledCorner = new Point2(100 + (46 * 1.8), 40 + (46 * 1.8));
        Rect2 visual = Rect2.FromCorners(origin, scaledCorner);
        Assert.Equal(46 * 1.8, visual.Width, 3);
        Assert.Equal(46 * 1.8, visual.Height, 3);

        var wrong = new Rect2(origin.X, origin.Y, 46, 46);
        Assert.True(visual.Width > wrong.Width * 1.5);
        Assert.True(visual.Contains(new Point2(origin.X + 60, origin.Y + 60)));
        Assert.False(wrong.Contains(new Point2(origin.X + 60, origin.Y + 60)));
    }

    [Fact]
    public void ObservedKeys_DoNotRegisterFarFlyovers()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        IReadOnlyList<Point2> path = PathAlong("bonjour", centers);
        Point2[] samples = SwipeDecoder.Resample(path, SwipeDecoder.SampleCount);
        IReadOnlyList<char> observed = SwipeDecoder.BuildObservedKeys(samples, centers, KeySize);

        Assert.Contains('b', observed);
        Assert.Contains('j', observed);
        Assert.Contains('r', observed);
        Assert.DoesNotContain('e', observed);
    }

    /// <summary>C → O → M (right) → E → N → T. Does not dip to N after O.</summary>
    public static IReadOnlyList<Point2> CommentShapedPath(IReadOnlyDictionary<char, Point2> c)
    {
        return Concat(
            Segment(c['c'], c['o'], 12),
            Segment(c['o'], c['m'], 10),
            Segment(c['m'], c['m'], 4),
            Segment(c['m'], c['e'], 10),
            Segment(c['e'], c['n'], 8),
            Segment(c['n'], c['t'], 8));
    }

    /// <summary>C → O → N (down) → T → E → N → T.</summary>
    public static IReadOnlyList<Point2> ContentShapedPath(IReadOnlyDictionary<char, Point2> c)
    {
        return Concat(
            Segment(c['c'], c['o'], 12),
            Segment(c['o'], c['n'], 10),
            Segment(c['n'], c['t'], 8),
            Segment(c['t'], c['e'], 8),
            Segment(c['e'], c['n'], 8),
            Segment(c['n'], c['t'], 8));
    }

    public static Dictionary<char, Point2> AzertyCenters()
    {
        var map = new Dictionary<char, Point2>();
        Place(map, "azertyuiop", y: 0, x0: 0);
        Place(map, "qsdfghjklm", y: 60, x0: 0);
        Place(map, "wxcvbn", y: 120, x0: 90);
        return map;
    }

    public static Dictionary<char, Point2> QwertyCenters()
    {
        var map = new Dictionary<char, Point2>();
        Place(map, "qwertyuiop", y: 0, x0: 0);
        Place(map, "asdfghjkl", y: 60, x0: 0);
        Place(map, "zxcvbnm", y: 120, x0: 30);
        return map;
    }

    public static IReadOnlyList<Point2> PathAlong(string word, IReadOnlyDictionary<char, Point2> c)
    {
        var parts = new List<List<Point2>>();
        char[] letters = TextFolding.ToLetters(word);
        for (int i = 0; i < letters.Length - 1; i++)
        {
            parts.Add(Segment(c[letters[i]], c[letters[i + 1]], 10));
        }

        return Concat(parts.ToArray());
    }

    private static Dictionary<char, Point2> ScaleCenters(Dictionary<char, Point2> centers, double scale)
    {
        var scaled = new Dictionary<char, Point2>();
        foreach ((char letter, Point2 p) in centers)
        {
            scaled[letter] = new Point2(p.X * scale, p.Y * scale);
        }

        return scaled;
    }

    private static IReadOnlyList<Point2> ScalePath(IReadOnlyList<Point2> path, double scale) =>
        path.Select(p => new Point2(p.X * scale, p.Y * scale)).ToList();

    private static void Place(Dictionary<char, Point2> map, string row, double y, double x0)
    {
        for (int i = 0; i < row.Length; i++)
        {
            map[row[i]] = new Point2(x0 + (i * KeySize), y);
        }
    }

    private static List<Point2> Segment(Point2 a, Point2 b, int steps)
    {
        var points = new List<Point2>(steps);
        for (int i = 0; i < steps; i++)
        {
            double t = (double)i / Math.Max(1, steps - 1);
            points.Add(new Point2(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t)));
        }

        return points;
    }

    private static List<Point2> Concat(params List<Point2>[] parts)
    {
        var all = new List<Point2>();
        foreach (List<Point2> part in parts)
        {
            all.AddRange(part);
        }

        return all;
    }

    /// <summary>
    /// Same geometry as the keyboard: scaled axis-aligned key faces around
    /// centers, slight inset, first-entered letter wins. Matches the trail.
    /// </summary>
    public static IReadOnlyList<char> HitKeysAlong(
        IReadOnlyList<Point2> path,
        IReadOnlyDictionary<char, Point2> centers,
        double keySize)
    {
        var keys = new List<(char Letter, Rect2 Bounds)>(centers.Count);
        foreach ((char letter, Point2 center) in centers)
        {
            var visual = new Rect2(center.X - (keySize / 2), center.Y - (keySize / 2), keySize, keySize);
            keys.Add((letter, visual.InsetFraction(0.08)));
        }

        var hits = new List<char>();
        foreach (Point2 p in path)
        {
            char c = SwipeGeometry.HitTest(p, keys);
            if (c != '\0' && (hits.Count == 0 || hits[^1] != c))
            {
                hits.Add(c);
            }
        }

        return hits;
    }

    private static void AssertOutranks(IReadOnlyList<string> ranked, string winner, string other)
    {
        int winAt = IndexOf(ranked, winner);
        int otherAt = IndexOf(ranked, other);
        Assert.True(otherAt < 0 || (winAt >= 0 && winAt < otherAt),
            $"Expected {winner} above {other}, got: {string.Join(", ", ranked)}");
    }

    private static int IndexOf(IReadOnlyList<string> ranked, string word)
    {
        for (int i = 0; i < ranked.Count; i++)
        {
            if (ranked[i] == word)
            {
                return i;
            }
        }

        return -1;
    }
}

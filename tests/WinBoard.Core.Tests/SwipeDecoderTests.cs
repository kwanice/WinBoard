using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

/// <summary>
/// Decoder test vectors. Geometry is a simplified AZERTY grid (key size 60):
/// <code>
/// y=0  a z e r t y u i o p     (x = 0,60,...,540)
/// y=60 q s d f g h j k l m
/// y=120      w x c v b n       (x starts at 90)
/// </code>
/// M is top-right of row 2 (540, 60). N is bottom row (390, 120).
/// A swipe that goes O → M (right) must NOT be decoded as a word that wants
/// O → N (down-left).
/// </summary>
public sealed class SwipeDecoderTests
{
    private const double KeySize = 60;

    [Fact]
    public void MBiasedPath_RanksCommentAboveContent()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            // content is listed first so frequency would FAVOR it if geometry
            // were ignored — the test fails unless the spatial score wins.
            "content",
            "comment",
            "comme",
            "comte",
            "concert",
        ]);

        IReadOnlyList<Point2> path = CommentShapedPath(centers);
        IReadOnlyList<char> hits = ['c', 'o', 'm', 'e', 'n', 't'];

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(hits, path, centers, words, KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        int commentAt = IndexOf(ranked, "comment");
        int contentAt = IndexOf(ranked, "content");
        Assert.True(contentAt < 0 || commentAt < contentAt,
            $"Expected comment above content, got: {string.Join(", ", ranked)}");
    }

    [Fact]
    public void RealFrenchLexicon_ContainsCommentAndRanksItAboveContentWhenBothReturned()
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
        int commentAt = IndexOf(ranked, "comment");
        int contentAt = IndexOf(ranked, "content");
        Assert.True(contentAt < 0 || (commentAt >= 0 && commentAt < contentAt),
            $"Expected comment above content when both appear, got: {string.Join(", ", ranked)}");
    }

    [Fact]
    public void ObservedKeysOnMBiasedPath_IncludeMNotNAfterO()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        Point2[] samples = SwipeDecoder.Resample(CommentShapedPath(centers), 64);
        IReadOnlyList<char> observed = SwipeDecoder.BuildObservedKeys(samples, centers, KeySize);

        Assert.Contains('m', observed);
        int o = observed.ToList().IndexOf('o');
        int m = observed.ToList().IndexOf('m');
        int n = observed.ToList().IndexOf('n');
        Assert.True(o >= 0 && m > o, $"O then M in {string.Join("", observed)}");
        Assert.True(n < 0 || n > m, $"N must not appear between O and M in {string.Join("", observed)}");
    }

    [Fact]
    public void SpatialLevenshtein_MVersusN_IsExpensive()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        char[] comment = ['c', 'o', 'm', 'm', 'e', 'n', 't'];
        char[] content = ['c', 'o', 'n', 't', 'e', 'n', 't'];
        char[] observed = ['c', 'o', 'm', 'e', 'n', 't'];

        double commentCost = SwipeDecoder.SpatialLevenshtein(observed, comment, centers, KeySize);
        double contentCost = SwipeDecoder.SpatialLevenshtein(observed, content, centers, KeySize);

        Assert.True(commentCost < contentCost,
            $"comment {commentCost:F3} should beat content {contentCost:F3}");
    }

    [Fact]
    public void NBiasedPath_CanStillRankContent()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(["comment", "content"]);
        IReadOnlyList<Point2> path = ContentShapedPath(centers);

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(['c', 'o', 'n', 't'], path, centers, words, KeySize);

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
        IReadOnlyList<Point2> path = PathAlong("hello", centers);
        IReadOnlyList<char> hits = ['h', 'e', 'l', 'o'];

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(hits, path, centers, words, KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("hello", ranked[0]);
        int helloAt = IndexOf(ranked, "hello");
        int ventiloAt = IndexOf(ranked, "ventilo");
        Assert.True(ventiloAt < 0 || helloAt < ventiloAt,
            $"hello must outrank a distant path match, got: {string.Join(", ", ranked)}");
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
        Assert.True(IndexOf(ranked, "ventilo") < 0 || IndexOf(ranked, "hello") < IndexOf(ranked, "ventilo"));
    }

    [Fact]
    public void AzertyBonjourShapedPath_RanksBonjourAboveLongerDivergentWord()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            "bouillonne",
            "bonjour",
            "bonsoir",
            "bouton",
        ]);
        IReadOnlyList<Point2> path = PathAlong("bonjour", centers);
        IReadOnlyList<char> hits = ['b', 'o', 'n', 'j', 'o', 'u', 'r'];

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(hits, path, centers, words, KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("bonjour", ranked[0]);
        int bonjourAt = IndexOf(ranked, "bonjour");
        int otherAt = IndexOf(ranked, "bouillonne");
        Assert.True(otherAt < 0 || bonjourAt < otherAt,
            $"bonjour must outrank a divergent path match, got: {string.Join(", ", ranked)}");
    }

    [Fact]
    public void FrenchLexicon_BonjourShapedPath_ReturnsBonjour()
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
        int otherAt = IndexOf(ranked, "bouillonne");
        Assert.True(otherAt < 0 || IndexOf(ranked, "bonjour") < otherAt);
    }

    [Fact]
    public void ShortBonjourPath_DoesNotExpandIntoLongPlainLetterCandidate()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            // Listed first (highest frequency) to prove length/geometry wins.
            "bonheurdujour",
            "bonjour",
            "bonsoir",
        ]);

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(
            ['b', 'o', 'n', 'j', 'o', 'u', 'r'],
            PathAlong("bonjour", centers),
            centers,
            words,
            KeySize,
            maxResults: 10);

        Assert.NotEmpty(ranked);
        Assert.Equal("bonjour", ranked[0]);
        int longAt = IndexOf(ranked, "bonheurdujour");
        Assert.True(longAt < 0 || IndexOf(ranked, "bonjour") < longAt,
            $"Short glide expanded into a long candidate: {string.Join(", ", ranked)}");
    }

    [Fact]
    public void CandidateRouteLongerThanGlide_GetsQuadraticRatioPenalty()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        IReadOnlyList<Point2> shortPath = PathAlong("bonjour", centers);
        IReadOnlyList<Point2> intended = TextFolding.ToLetters("bonjour").Select(c => centers[c]).ToList();
        IReadOnlyList<Point2> longRoute = TextFolding.ToLetters("bonheurdujour").Select(c => centers[c]).ToList();

        double intendedPenalty = SwipeDecoder.LongPathRatioPenalty(intended, shortPath, KeySize);
        double longPenalty = SwipeDecoder.LongPathRatioPenalty(longRoute, shortPath, KeySize);

        Assert.True(longPenalty > intendedPenalty + 1,
            $"Expected long route penalty ({longPenalty:F2}) well above intended ({intendedPenalty:F2})");
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
        Point2[] samples = SwipeDecoder.Resample(path, 64);
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

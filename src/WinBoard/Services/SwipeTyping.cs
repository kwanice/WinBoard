using System.Globalization;
using System.Reflection;
using System.Text;
using Windows.Foundation;

namespace WinBoard.Services;

/// <summary>A dictionary word with its accent-folded letters and frequency weight.</summary>
public sealed class WordEntry
{
    public required string Word { get; init; }

    /// <summary>Lowercased a–z letters (accents stripped, ligatures expanded).</summary>
    public required char[] Folded { get; init; }

    /// <summary>0..1, higher = more common (used as a light tie-breaker).</summary>
    public required double Frequency { get; init; }
}

/// <summary>Words for one language, bucketed by first folded letter for fast filtering.</summary>
public sealed class WordList
{
    public required IReadOnlyDictionary<char, List<WordEntry>> ByFirstLetter { get; init; }
}

/// <summary>
/// Loads the embedded frequency-ordered word lists (FR/EN) used by the swipe decoder.
/// </summary>
public sealed class WordListService
{
    private readonly Dictionary<string, WordList> _cache = new();

    public WordList ForLayout(string layoutId)
    {
        string language = layoutId == Layouts.LayoutCatalog.QwertyId ? "en" : "fr";
        if (!_cache.TryGetValue(language, out WordList? list))
        {
            list = Load(language);
            _cache[language] = list;
        }

        return list;
    }

    private static WordList Load(string language)
    {
        var buckets = new Dictionary<char, List<WordEntry>>();
        string resourceName = $"WinBoard.Assets.words_{language}.txt";

        Assembly assembly = typeof(WordListService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new WordList { ByFirstLetter = buckets };
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                lines.Add(trimmed);
            }
        }

        for (int i = 0; i < lines.Count; i++)
        {
            char[] folded = TextFolding.ToLetters(lines[i]);
            if (folded.Length < 2)
            {
                continue;
            }

            var entry = new WordEntry
            {
                Word = lines[i],
                Folded = folded,
                Frequency = 1.0 - ((double)i / lines.Count),
            };

            if (!buckets.TryGetValue(folded[0], out List<WordEntry>? bucket))
            {
                bucket = new List<WordEntry>();
                buckets[folded[0]] = bucket;
            }

            bucket.Add(entry);
        }

        return new WordList { ByFirstLetter = buckets };
    }
}

/// <summary>Accent folding shared by the word lists and decoder.</summary>
public static class TextFolding
{
    public static char[] ToLetters(string value)
    {
        // Expand ligatures first so they map onto real keys, then strip accents.
        string expanded = value
            .Replace("œ", "oe", StringComparison.OrdinalIgnoreCase)
            .Replace("æ", "ae", StringComparison.OrdinalIgnoreCase)
            .Replace("ß", "ss", StringComparison.OrdinalIgnoreCase);

        string normalized = expanded.Normalize(NormalizationForm.FormD);
        var result = new List<char>(normalized.Length);
        foreach (char ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z')
            {
                result.Add(lower);
            }
        }

        return result.ToArray();
    }
}

/// <summary>
/// Geometric swipe decoder (no ML). Filters candidates by first/last crossed key,
/// then scores how well each word's letter-center polyline matches the traced path
/// via arc-length resampling. Frequency is a light tie-breaker.
/// </summary>
public static class SwipeDecoder
{
    private const int SampleCount = 48;

    public static IReadOnlyList<string> Decode(
        IReadOnlyList<char> anchors,
        IReadOnlyList<Point> path,
        IReadOnlyDictionary<char, Point> centers,
        WordList words,
        double keySize,
        int maxResults = 5)
    {
        if (anchors.Count < 2 || path.Count < 2 || keySize <= 0)
        {
            return [];
        }

        char first = anchors[0];
        char last = anchors[^1];
        if (!words.ByFirstLetter.TryGetValue(first, out List<WordEntry>? bucket))
        {
            return [];
        }

        Point[] userResampled = Resample(path, SampleCount);
        var scored = new List<(WordEntry Entry, double Score)>();

        foreach (WordEntry entry in bucket)
        {
            if (entry.Folded[^1] != last)
            {
                continue;
            }

            var ideal = new List<Point>(entry.Folded.Length);
            bool mappable = true;
            foreach (char c in entry.Folded)
            {
                if (centers.TryGetValue(c, out Point center))
                {
                    ideal.Add(center);
                }
                else
                {
                    mappable = false;
                    break;
                }
            }

            if (!mappable || ideal.Count < 2)
            {
                continue;
            }

            Point[] idealResampled = Resample(ideal, SampleCount);

            double cost = 0;
            for (int i = 0; i < SampleCount; i++)
            {
                cost += Distance(userResampled[i], idealResampled[i]);
            }

            cost = cost / SampleCount / keySize;
            double score = cost - (0.12 * entry.Frequency);
            scored.Add((entry, score));
        }

        // Collapse duplicates that fold to the same letters (e.g. "être"/"etre"),
        // keeping the best-scoring spelling.
        var seenFolded = new HashSet<string>();
        var results = new List<string>();
        foreach ((WordEntry entry, double _) in scored.OrderBy(s => s.Score))
        {
            if (seenFolded.Add(new string(entry.Folded)))
            {
                results.Add(entry.Word);
                if (results.Count >= maxResults)
                {
                    break;
                }
            }
        }

        return results;
    }

    private static Point[] Resample(IReadOnlyList<Point> points, int count)
    {
        var result = new Point[count];
        if (points.Count == 0)
        {
            return result;
        }

        if (points.Count == 1)
        {
            Array.Fill(result, points[0]);
            return result;
        }

        double total = 0;
        for (int i = 1; i < points.Count; i++)
        {
            total += Distance(points[i - 1], points[i]);
        }

        if (total <= 0)
        {
            Array.Fill(result, points[0]);
            return result;
        }

        double step = total / (count - 1);
        result[0] = points[0];
        result[count - 1] = points[^1];

        int segment = 0;
        double segmentStart = 0;
        double segmentLength = Distance(points[0], points[1]);

        for (int i = 1; i < count - 1; i++)
        {
            double target = i * step;
            while (segment < points.Count - 2 && segmentStart + segmentLength < target)
            {
                segmentStart += segmentLength;
                segment++;
                segmentLength = Distance(points[segment], points[segment + 1]);
            }

            double t = segmentLength <= 0 ? 0 : (target - segmentStart) / segmentLength;
            Point a = points[segment];
            Point b = points[segment + 1];
            result[i] = new Point(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
        }

        return result;
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

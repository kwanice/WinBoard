using System.Reflection;
using System.Text;
using WinBoard.Core;
using WinBoard.Layouts;

namespace WinBoard.Services;

/// <summary>
/// Loads the embedded frequency-ordered FR/EN word lists for the swipe decoder.
/// </summary>
public sealed class WordListService
{
    private readonly Dictionary<string, WordList> _cache = new();

    public WordList ForLayout(string layoutId)
    {
        string language = layoutId == LayoutCatalog.QwertyId ? "en" : "fr";
        if (!_cache.TryGetValue(language, out WordList? list))
        {
            list = Load(language);
            _cache[language] = list;
        }

        return list;
    }

    private static WordList Load(string language)
    {
        string resourceName = $"WinBoard.Assets.words_{language}.txt";
        Assembly assembly = typeof(WordListService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return WordList.FromOrderedWords([]);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return WordList.FromLines(lines);
    }
}

using WinBoard.Core;
using WinBoard.Layouts;

namespace WinBoard.Services;

/// <summary>
/// Loads the embedded frequency-ordered FR/EN word lists for the swipe decoder.
/// Lists ship inside WinBoard.Core (local files, no network).
/// </summary>
public sealed class WordListService
{
    private readonly Dictionary<string, WordList> _cache = new();

    public WordList ForLayout(string layoutId)
    {
        string language = layoutId == LayoutCatalog.QwertyId ? "en" : "fr";
        if (!_cache.TryGetValue(language, out WordList? list))
        {
            list = WordList.LoadLanguage(language);
            _cache[language] = list;
        }

        return list;
    }
}

using WinBoard.Core;
using WinBoard.Layouts;

namespace WinBoard.Services;

/// <summary>
/// Loads the embedded frequency-ordered FR/EN word lists and compact
/// bigram tables for the swipe decoder. Local files, no network.
/// </summary>
public sealed class WordListService
{
    private readonly Dictionary<string, WordList> _words = new();
    private readonly Dictionary<string, LanguageModel> _language = new();

    public WordList ForLayout(string layoutId)
    {
        string language = LanguageId(layoutId);
        if (!_words.TryGetValue(language, out WordList? list))
        {
            list = WordList.LoadLanguage(language);
            _words[language] = list;
        }

        return list;
    }

    public LanguageModel LanguageForLayout(string layoutId)
    {
        string language = LanguageId(layoutId);
        if (!_language.TryGetValue(language, out LanguageModel? model))
        {
            model = LanguageModel.LoadLanguage(language, ForLayout(layoutId));
            _language[language] = model;
        }

        return model;
    }

    private static string LanguageId(string layoutId) =>
        layoutId == LayoutCatalog.QwertyId ? "en" : "fr";
}

using WinBoard.Core;

namespace WinBoard.Services;

/// <summary>
/// Loads the embedded frequency-ordered FR/EN word lists and compact
/// bigram tables for the swipe decoder. Local files, no network.
/// Swipe uses a bilingual union so EN targets on AZERTY (and FR on QWERTY)
/// still decode.
/// </summary>
public sealed class WordListService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WordList> _words = new();
    private readonly Dictionary<string, LanguageModel> _language = new();

    public WordList ForLayout(string layoutId) => ForSwipe();

    public LanguageModel LanguageForLayout(string layoutId) => LanguageForSwipe();

    public WordList ForSwipe()
    {
        lock (_gate)
        {
            if (!_words.TryGetValue("bilingual", out WordList? list))
            {
                list = WordList.LoadBilingual();
                _words["bilingual"] = list;
            }

            return list;
        }
    }

    public LanguageModel LanguageForSwipe()
    {
        lock (_gate)
        {
            if (!_language.TryGetValue("bilingual", out LanguageModel? model))
            {
                model = LanguageModel.LoadBilingual(ForSwipeUnlocked());
                _language["bilingual"] = model;
            }

            return model;
        }
    }

    /// <summary>
    /// Load FR∪EN lists, build the trie, and parse bigrams on a worker so the
    /// first swipe does not stall the UI thread.
    /// </summary>
    public void Warm()
    {
        WordList list = ForSwipe();
        _ = list.EnsureReady();
        _ = LanguageForSwipe();
    }

    private WordList ForSwipeUnlocked()
    {
        if (!_words.TryGetValue("bilingual", out WordList? list))
        {
            list = WordList.LoadBilingual();
            _words["bilingual"] = list;
        }

        return list;
    }
}

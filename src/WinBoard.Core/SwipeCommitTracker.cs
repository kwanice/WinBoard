namespace WinBoard.Core;

/// <summary>
/// Gboard-like swipe commit: the swipe injects the word (no trailing space);
/// the next swipe prefixes a space when needed; Backspace undoes the last
/// swipe chunk (word + that leading space) until Space, a letter, or another
/// non-swipe commit clears the unit.
/// </summary>
public sealed class SwipeCommitTracker
{
    public int PendingCharCount { get; private set; }

    public bool HasPending => PendingCharCount > 0;

    /// <summary>True when the pending unit starts with the auto-inserted space.</summary>
    public bool PendingHasLeadingSpace { get; private set; }

    /// <summary>True when the next swipe should insert a leading space.</summary>
    public bool NeedsPrefixSpace { get; private set; }

    /// <summary>Text to inject for a decoded swipe word (optional leading space).</summary>
    public string PlanSwipeInject(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return string.Empty;
        }

        return NeedsPrefixSpace ? " " + word : word;
    }

    public void CommitSwipe(string injected)
    {
        if (string.IsNullOrEmpty(injected))
        {
            ClearPending();
            return;
        }

        PendingCharCount = injected.Length;
        PendingHasLeadingSpace = injected[0] == ' ';
        NeedsPrefixSpace = true;
    }

    /// <summary>Replacement text for a swipe suggestion chip (keeps the unit's leading space).</summary>
    public string PlanSuggestionReplace(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return string.Empty;
        }

        bool prefix = HasPending ? PendingHasLeadingSpace : NeedsPrefixSpace;
        return prefix ? " " + word : word;
    }

    public void CommitSuggestionReplace(string injected) => CommitSwipe(injected);

    /// <summary>
    /// Consumes the last swipe unit for whole-word Backspace.
    /// After a prefixed unit, the next swipe still prefixes a space
    /// (caret sits after the previous word). After a first-word unit,
    /// the next swipe has no prefix. No-ops when nothing is pending.
    /// </summary>
    public int ConsumeUndo()
    {
        if (!HasPending)
        {
            return 0;
        }

        int count = PendingCharCount;
        bool hadLeadingSpace = PendingHasLeadingSpace;
        ClearPending();
        NeedsPrefixSpace = hadLeadingSpace;
        return count;
    }

    public bool TryConsumeUndo(out int charCount)
    {
        if (!HasPending)
        {
            charCount = 0;
            return false;
        }

        charCount = ConsumeUndo();
        return charCount > 0;
    }

    /// <summary>Space tap: commit past the swipe unit; next swipe does not prefix.</summary>
    public void OnSpace()
    {
        ClearPending();
        NeedsPrefixSpace = false;
    }

    /// <summary>Letter / punctuation tap: commit past the unit; next swipe prefixes if not a space.</summary>
    public void OnTyped(char character)
    {
        ClearPending();
        NeedsPrefixSpace = character is not (' ' or '\n' or '\r');
    }

    public void OnEnter()
    {
        ClearPending();
        NeedsPrefixSpace = false;
    }

    /// <summary>
    /// Clip / emoji / long-press glyph / suggestion that is not a swipe chip.
    /// Clears whole-word undo; next swipe prefixes a space.
    /// </summary>
    public void OnOtherCommit()
    {
        ClearPending();
        NeedsPrefixSpace = true;
    }

    /// <summary>Clears whole-word undo without changing the prefix-space flag (caret move, failed decode).</summary>
    public void DismissPending() => ClearPending();

    public void Reset()
    {
        ClearPending();
        NeedsPrefixSpace = false;
    }

    private void ClearPending()
    {
        PendingCharCount = 0;
        PendingHasLeadingSpace = false;
    }
}

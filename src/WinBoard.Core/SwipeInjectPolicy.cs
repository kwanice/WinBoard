namespace WinBoard.Core;

/// <summary>
/// Letter taps and key-repeat must not run while a swipe is latched, while a
/// decoded word is still waiting to SendInput, or on a press that only exists
/// because the previous glide was commit-then-begun. Otherwise the key under
/// the finger (often the last hit-key) repeats into the target and the async
/// decode is skipped.
/// </summary>
public static class SwipeInjectPolicy
{
    /// <summary>
    /// Letter taps must not run while a swipe is latched, while a decoded word
    /// is waiting to SendInput, or on a press that only exists because the
    /// previous glide was commit-then-begun.
    /// </summary>
    public static bool BlockLetterInject(
        bool swiping,
        bool injectPending,
        bool pressIsSwipeContinuation) =>
        swiping || injectPending || pressIsSwipeContinuation;

    public static bool AllowLetterTapOrRepeat(
        bool swiping,
        bool injectPending,
        bool pressIsSwipeContinuation) =>
        !BlockLetterInject(swiping, injectPending, pressIsSwipeContinuation);

    /// <summary>
    /// Swipe letters must never key-repeat. Holding <c>s</c> after a glide
    /// produced <c>ssss</c> in the target while diag still showed the word.
    /// Digits / Backspace still repeat.
    /// </summary>
    public static bool AllowLetterKeyRepeat(bool isSwipeableLetter) => !isSwipeableLetter;
}

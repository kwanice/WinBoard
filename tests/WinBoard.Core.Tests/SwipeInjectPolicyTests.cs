using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class SwipeInjectPolicyTests
{
    [Fact]
    public void LetterTap_AllowedWhenIdle()
    {
        Assert.True(SwipeInjectPolicy.AllowLetterTapOrRepeat(
            swiping: false, injectPending: false, pressIsSwipeContinuation: false));
    }

    [Fact]
    public void LetterTap_BlockedWhileSwiping()
    {
        Assert.True(SwipeInjectPolicy.BlockLetterInject(
            swiping: true, injectPending: false, pressIsSwipeContinuation: false));
    }

    [Fact]
    public void LetterTap_BlockedWhileDecodedWordAwaitingSendInput()
    {
        Assert.True(SwipeInjectPolicy.BlockLetterInject(
            swiping: false, injectPending: true, pressIsSwipeContinuation: false));
    }

    [Fact]
    public void LetterTap_BlockedOnCommitThenBeginContinuationPress()
    {
        Assert.False(SwipeInjectPolicy.AllowLetterTapOrRepeat(
            swiping: false, injectPending: false, pressIsSwipeContinuation: true));
    }

    [Fact]
    public void LetterKeyRepeat_DisabledForSwipeableLetters()
    {
        Assert.False(SwipeInjectPolicy.AllowLetterKeyRepeat(isSwipeableLetter: true));
        Assert.True(SwipeInjectPolicy.AllowLetterKeyRepeat(isSwipeableLetter: false));
    }

    [Fact]
    public void InjectPending_DoesNotHaveToBlockTheNextGesturesRepeatGate_WhenIdle()
    {
        // After the previous word has been injected, a new press is a clean
        // gesture: tap/repeat policy is independent of the last serial.
        Assert.True(SwipeInjectPolicy.AllowLetterTapOrRepeat(
            swiping: false, injectPending: false, pressIsSwipeContinuation: false));
    }
}

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
}

using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class SwipeContactPolicyTests
{
    [Fact]
    public void Release_CommitsActiveSession()
    {
        Assert.Equal(
            SwipeContactAction.EndCommit,
            SwipeContactPolicy.OnEndSignal(SwipeContactSignal.Released, sessionActive: true, contactDown: false));
    }

    [Fact]
    public void Cancel_EndsWithoutCommit()
    {
        Assert.Equal(
            SwipeContactAction.EndCancel,
            SwipeContactPolicy.OnEndSignal(SwipeContactSignal.Canceled, sessionActive: true, contactDown: true));
    }

    [Fact]
    public void CaptureLost_WhileFingerDown_KeepsTrail()
    {
        Assert.Equal(
            SwipeContactAction.Continue,
            SwipeContactPolicy.OnEndSignal(SwipeContactSignal.CaptureLost, sessionActive: true, contactDown: true));
    }

    [Fact]
    public void CaptureLost_WhenContactAlreadyUp_Cancels()
    {
        Assert.Equal(
            SwipeContactAction.EndCancel,
            SwipeContactPolicy.OnEndSignal(SwipeContactSignal.CaptureLost, sessionActive: true, contactDown: false));
    }

    [Fact]
    public void SignalsWithoutSession_AreIgnored()
    {
        Assert.Equal(
            SwipeContactAction.Ignore,
            SwipeContactPolicy.OnEndSignal(SwipeContactSignal.CaptureLost, sessionActive: false, contactDown: true));
        Assert.Equal(
            SwipeContactAction.Ignore,
            SwipeContactPolicy.OnEndSignal(SwipeContactSignal.Released, sessionActive: false, contactDown: false));
    }

    [Fact]
    public void OverlayMutation_BlockedDuringPointerSession()
    {
        Assert.False(SwipeContactPolicy.AllowOverlayMutation(sessionActive: true));
        Assert.True(SwipeContactPolicy.AllowOverlayMutation(sessionActive: false));
    }

    [Fact]
    public void ChainedSwipe_CaptureLostFromPreviousInject_DoesNotEnd()
    {
        // Swipe 1 ended; swipe 2 is down and already latched. Inject of word 1
        // drops capture. The session must continue so the trail stays live.
        Assert.Equal(
            SwipeContactAction.Continue,
            SwipeContactPolicy.OnEndSignal(SwipeContactSignal.CaptureLost, sessionActive: true, contactDown: true));
        Assert.False(SwipeContactPolicy.AllowOverlayMutation(sessionActive: true));
    }
}

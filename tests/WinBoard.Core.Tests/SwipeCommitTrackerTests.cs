using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class SwipeCommitTrackerTests
{
    [Fact]
    public void FirstSwipe_InjectsWordWithoutTrailingOrLeadingSpace()
    {
        var tracker = new SwipeCommitTracker();
        string injected = tracker.PlanSwipeInject("hello");
        Assert.Equal("hello", injected);

        tracker.CommitSwipe(injected);
        Assert.True(tracker.HasPending);
        Assert.Equal(5, tracker.PendingCharCount);
        Assert.False(tracker.PendingHasLeadingSpace);
        Assert.True(tracker.NeedsPrefixSpace);
    }

    [Fact]
    public void ConsecutiveSwipes_PrefixSpaceOnLaterWords()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));

        string second = tracker.PlanSwipeInject("world");
        Assert.Equal(" world", second);
        tracker.CommitSwipe(second);
        Assert.Equal(6, tracker.PendingCharCount);
        Assert.True(tracker.PendingHasLeadingSpace);
    }

    [Fact]
    public void Backspace_DeletesEntireLastSwipeUnitIncludingPrefixSpace()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));
        tracker.CommitSwipe(tracker.PlanSwipeInject("world"));

        Assert.True(tracker.TryConsumeUndo(out int count));
        Assert.Equal(6, count);
        Assert.False(tracker.HasPending);
        Assert.True(tracker.NeedsPrefixSpace);

        Assert.Equal(" yes", tracker.PlanSwipeInject("yes"));
    }

    [Fact]
    public void Backspace_FirstSwipe_DeletesWordOnly_NextSwipeHasNoPrefix()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));

        Assert.Equal(5, tracker.ConsumeUndo());
        Assert.False(tracker.HasPending);
        Assert.False(tracker.NeedsPrefixSpace);
        Assert.Equal("oui", tracker.PlanSwipeInject("oui"));
    }

    [Fact]
    public void SpaceTap_ClearsWholeWordUndo_AndStopsPrefixing()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));
        Assert.True(tracker.HasPending);

        tracker.OnSpace();
        Assert.False(tracker.HasPending);
        Assert.False(tracker.NeedsPrefixSpace);
        Assert.Equal("world", tracker.PlanSwipeInject("world"));
    }

    [Fact]
    public void LetterTap_ClearsWholeWordUndo_NextSwipePrefixes()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));

        tracker.OnTyped('s');
        Assert.False(tracker.HasPending);
        Assert.True(tracker.NeedsPrefixSpace);
        Assert.Equal(" world", tracker.PlanSwipeInject("world"));
    }

    [Fact]
    public void AfterWholeWordUndo_FurtherBackspaceIsNotAnotherSwipeUnit()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));
        Assert.Equal(5, tracker.ConsumeUndo());
        Assert.False(tracker.TryConsumeUndo(out int count));
        Assert.Equal(0, count);
        Assert.False(tracker.NeedsPrefixSpace);
    }

    [Fact]
    public void ExtraUndo_DoesNotClearPrefixAfterPrefixedUnit()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));
        tracker.CommitSwipe(tracker.PlanSwipeInject("world"));
        tracker.ConsumeUndo();
        Assert.True(tracker.NeedsPrefixSpace);
        Assert.False(tracker.TryConsumeUndo(out _));
        Assert.True(tracker.NeedsPrefixSpace);
    }

    [Fact]
    public void TypedThenBackspace_KeepsPrefixForNextSwipe()
    {
        var tracker = new SwipeCommitTracker();
        tracker.OnTyped('h');
        tracker.OnTyped('i');
        Assert.False(tracker.TryConsumeUndo(out _));
        Assert.True(tracker.NeedsPrefixSpace);
        Assert.Equal(" world", tracker.PlanSwipeInject("world"));
    }

    [Fact]
    public void SuggestionReplace_KeepsLeadingSpaceOfPendingUnit()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));
        tracker.CommitSwipe(tracker.PlanSwipeInject("world"));

        string replacement = tracker.PlanSuggestionReplace("word");
        Assert.Equal(" word", replacement);
        Assert.Equal(5, replacement.Length);
        int undo = tracker.PendingCharCount;
        Assert.Equal(6, undo);
        tracker.CommitSuggestionReplace(replacement);
        Assert.Equal(5, tracker.PendingCharCount);
        Assert.True(tracker.PendingHasLeadingSpace);
    }

    [Fact]
    public void CommitSwipe_CountsLeadingSpaceInInjectedText()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(" world");
        Assert.Equal(6, tracker.PendingCharCount);
        Assert.True(tracker.PendingHasLeadingSpace);
    }

    [Fact]
    public void SuggestionReplace_FirstWord_HasNoLeadingSpace()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));

        string replacement = tracker.PlanSuggestionReplace("help");
        Assert.Equal("help", replacement);
        tracker.CommitSuggestionReplace(replacement);
        Assert.Equal(4, tracker.PendingCharCount);
        Assert.False(tracker.PendingHasLeadingSpace);
    }

    [Fact]
    public void Enter_ClearsPending_AndDoesNotPrefixNextSwipe()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));
        tracker.OnEnter();
        Assert.False(tracker.HasPending);
        Assert.Equal("world", tracker.PlanSwipeInject("world"));
    }

    [Fact]
    public void NonSwipeCommit_ClearsPending_NextSwipePrefixes()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));
        tracker.OnOtherCommit();
        Assert.False(tracker.HasPending);
        Assert.Equal(" world", tracker.PlanSwipeInject("world"));
    }

    [Fact]
    public void CaretMove_DismissesPending_KeepsPrefixFlag()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));
        Assert.True(tracker.NeedsPrefixSpace);

        tracker.DismissPending();
        Assert.False(tracker.HasPending);
        Assert.True(tracker.NeedsPrefixSpace);
        Assert.Equal(" world", tracker.PlanSwipeInject("world"));
    }

    [Fact]
    public void PunctuationTap_ClearsPendingLikeAKey()
    {
        var tracker = new SwipeCommitTracker();
        tracker.CommitSwipe(tracker.PlanSwipeInject("hello"));
        tracker.OnTyped('.');
        Assert.False(tracker.HasPending);
        Assert.True(tracker.NeedsPrefixSpace);
        Assert.Equal(" world", tracker.PlanSwipeInject("world"));
    }

    [Fact]
    public void EmptyWord_DoesNotCreatePendingUnit()
    {
        var tracker = new SwipeCommitTracker();
        Assert.Equal(string.Empty, tracker.PlanSwipeInject(""));
        tracker.CommitSwipe("");
        Assert.False(tracker.HasPending);
        Assert.False(tracker.NeedsPrefixSpace);
    }
}

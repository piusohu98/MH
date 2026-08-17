using MH.Collector;

namespace MH.Tests;

public sealed class CollectorRunStateMachineTests
{
    [Fact]
    public void CompletesNormalCaptureRecognitionPathAndReturnsToObserving()
    {
        var machine = new CollectorRunStateMachine();

        AssertTransition(machine, CollectorRunEvent.StartObserving, CollectorRunState.Idle, CollectorRunState.Observing, CollectorTransitionReasons.StartedObserving);
        AssertTransition(machine, CollectorRunEvent.CaptureStarted, CollectorRunState.Observing, CollectorRunState.Capturing, CollectorTransitionReasons.CaptureStarted);
        AssertTransition(machine, CollectorRunEvent.RecognitionStarted, CollectorRunState.Capturing, CollectorRunState.Recognizing, CollectorTransitionReasons.RecognitionStarted);
        AssertTransition(machine, CollectorRunEvent.Accepted, CollectorRunState.Recognizing, CollectorRunState.Observing, CollectorTransitionReasons.Accepted);
    }

    [Fact]
    public void ReviewRequiredNeedsExplicitResumeBeforeObserving()
    {
        var machine = new CollectorRunStateMachine();
        StartRecognition(machine);

        AssertTransition(machine, CollectorRunEvent.ReviewRequired, CollectorRunState.Recognizing, CollectorRunState.ReviewRequired, CollectorTransitionReasons.ReviewRequired);
        AssertDenied(machine, CollectorRunEvent.CaptureStarted, CollectorRunState.ReviewRequired);
        AssertDenied(machine, CollectorRunEvent.RecognitionStarted, CollectorRunState.ReviewRequired);
        AssertTransition(machine, CollectorRunEvent.Resume, CollectorRunState.ReviewRequired, CollectorRunState.Observing, CollectorTransitionReasons.ManualResume);
    }

    [Theory]
    [InlineData(CollectorRunEvent.LoginDetected, CollectorRunState.PausedForLogin, CollectorTransitionReasons.LoginDetected)]
    [InlineData(CollectorRunEvent.UpdateDetected, CollectorRunState.PausedForUpdate, CollectorTransitionReasons.UpdateDetected)]
    [InlineData(CollectorRunEvent.CaptchaDetected, CollectorRunState.PausedForCaptcha, CollectorTransitionReasons.CaptchaDetected)]
    [InlineData(CollectorRunEvent.Disconnected, CollectorRunState.Disconnected, CollectorTransitionReasons.Disconnected)]
    [InlineData(CollectorRunEvent.UnknownPage, CollectorRunState.UnknownPage, CollectorTransitionReasons.UnknownPage)]
    public void DetectionEventsEnterTheirManualStopState(
        CollectorRunEvent @event,
        CollectorRunState expectedState,
        string expectedReason)
    {
        var machine = new CollectorRunStateMachine();
        AssertTransition(machine, CollectorRunEvent.StartObserving, CollectorRunState.Idle, CollectorRunState.Observing, CollectorTransitionReasons.StartedObserving);

        AssertTransition(machine, @event, CollectorRunState.Observing, expectedState, expectedReason);
        AssertDenied(machine, CollectorRunEvent.CaptureStarted, expectedState);
        AssertDenied(machine, CollectorRunEvent.RecognitionStarted, expectedState);
        AssertTransition(machine, CollectorRunEvent.Resume, expectedState, CollectorRunState.Observing, CollectorTransitionReasons.ManualResume);
    }

    [Fact]
    public void CaptchaCanOnlyResumeToObservingOrStop()
    {
        var machine = new CollectorRunStateMachine();
        AssertTransition(machine, CollectorRunEvent.CaptchaDetected, CollectorRunState.Idle, CollectorRunState.PausedForCaptcha, CollectorTransitionReasons.CaptchaDetected);

        AssertTransition(machine, CollectorRunEvent.Resume, CollectorRunState.PausedForCaptcha, CollectorRunState.Observing, CollectorTransitionReasons.ManualResume);
        AssertTransition(machine, CollectorRunEvent.CaptchaDetected, CollectorRunState.Observing, CollectorRunState.PausedForCaptcha, CollectorTransitionReasons.CaptchaDetected);
        AssertTransition(machine, CollectorRunEvent.StopRequested, CollectorRunState.PausedForCaptcha, CollectorRunState.Stopped, CollectorTransitionReasons.StopRequested);
        AssertDenied(machine, CollectorRunEvent.CaptureStarted, CollectorRunState.Stopped);
        AssertDenied(machine, CollectorRunEvent.RecognitionStarted, CollectorRunState.Stopped);
        AssertDenied(machine, CollectorRunEvent.Resume, CollectorRunState.Stopped);
        AssertTransition(machine, CollectorRunEvent.Reset, CollectorRunState.Stopped, CollectorRunState.Idle, CollectorTransitionReasons.ManualReset);
        AssertTransition(machine, CollectorRunEvent.StartObserving, CollectorRunState.Idle, CollectorRunState.Observing, CollectorTransitionReasons.StartedObserving);
    }

    [Theory]
    [InlineData(CollectorRunState.Idle)]
    [InlineData(CollectorRunState.Observing)]
    [InlineData(CollectorRunState.Capturing)]
    [InlineData(CollectorRunState.Recognizing)]
    [InlineData(CollectorRunState.ReviewRequired)]
    [InlineData(CollectorRunState.PausedForLogin)]
    [InlineData(CollectorRunState.PausedForUpdate)]
    [InlineData(CollectorRunState.PausedForCaptcha)]
    [InlineData(CollectorRunState.Disconnected)]
    [InlineData(CollectorRunState.UnknownPage)]
    public void StopRequestedEntersStoppedFromEveryNonStoppedState(CollectorRunState initialState)
    {
        var machine = new CollectorRunStateMachine(initialState);

        AssertTransition(machine, CollectorRunEvent.StopRequested, initialState, CollectorRunState.Stopped, CollectorTransitionReasons.StopRequested);
    }

    [Fact]
    public void ResetRequiresExplicitActionAndReturnsToIdleWithoutContinuingWork()
    {
        var machine = new CollectorRunStateMachine(CollectorRunState.PausedForUpdate);

        var transition = machine.Transition(CollectorRunEvent.Reset);

        Assert.Equal(CollectorRunState.PausedForUpdate, transition.OldState);
        Assert.Equal(CollectorRunState.Idle, transition.NewState);
        Assert.True(transition.Allowed);
        Assert.Equal(CollectorTransitionReasons.ManualReset, transition.Reason);
        Assert.Equal(CollectorRunState.Idle, machine.CurrentState);
        AssertDenied(machine, CollectorRunEvent.CaptureStarted, CollectorRunState.Idle);
    }

    [Fact]
    public void IllegalTransitionsAreRejectedWithoutChangingState()
    {
        var machine = new CollectorRunStateMachine();

        AssertDenied(machine, CollectorRunEvent.CaptureStarted, CollectorRunState.Idle);
        AssertDenied(machine, CollectorRunEvent.RecognitionStarted, CollectorRunState.Idle);
        AssertDenied(machine, CollectorRunEvent.Accepted, CollectorRunState.Idle);
        AssertDenied(machine, CollectorRunEvent.Resume, CollectorRunState.Idle);
        AssertDenied(machine, CollectorRunEvent.Reset, CollectorRunState.Idle);
        Assert.Equal(CollectorRunState.Idle, machine.CurrentState);
    }

    [Fact]
    public void UnknownEventFailsClosedWithDeterministicReason()
    {
        var machine = new CollectorRunStateMachine(CollectorRunState.Observing);
        var unknownEvent = (CollectorRunEvent)999;

        var first = machine.Transition(unknownEvent);
        var second = machine.Transition(unknownEvent);

        Assert.False(first.Allowed);
        Assert.Equal(CollectorRunState.Observing, first.OldState);
        Assert.Equal(CollectorRunState.Observing, first.NewState);
        Assert.Equal(CollectorTransitionReasons.UnknownEvent, first.Reason);
        Assert.Equal(first, second);
        Assert.Equal(CollectorRunState.Observing, machine.CurrentState);
    }

    [Fact]
    public void IllegalReasonIsStableForRepeatedEvent()
    {
        var machine = new CollectorRunStateMachine(CollectorRunState.Observing);

        var first = machine.Transition(CollectorRunEvent.RecognitionStarted);
        var second = machine.Transition(CollectorRunEvent.RecognitionStarted);

        Assert.False(first.Allowed);
        Assert.Equal(CollectorTransitionReasons.InvalidTransition, first.Reason);
        Assert.Equal(first, second);
        Assert.Equal(CollectorRunState.Observing, machine.CurrentState);
    }

    private static void StartRecognition(CollectorRunStateMachine machine)
    {
        AssertTransition(machine, CollectorRunEvent.StartObserving, CollectorRunState.Idle, CollectorRunState.Observing, CollectorTransitionReasons.StartedObserving);
        AssertTransition(machine, CollectorRunEvent.CaptureStarted, CollectorRunState.Observing, CollectorRunState.Capturing, CollectorTransitionReasons.CaptureStarted);
        AssertTransition(machine, CollectorRunEvent.RecognitionStarted, CollectorRunState.Capturing, CollectorRunState.Recognizing, CollectorTransitionReasons.RecognitionStarted);
    }

    private static void AssertTransition(
        CollectorRunStateMachine machine,
        CollectorRunEvent @event,
        CollectorRunState oldState,
        CollectorRunState newState,
        string reason)
    {
        var transition = machine.Transition(@event);

        Assert.Equal(oldState, transition.OldState);
        Assert.Equal(newState, transition.NewState);
        Assert.True(transition.Allowed);
        Assert.Equal(reason, transition.Reason);
        Assert.Equal(newState, machine.CurrentState);
    }

    private static void AssertDenied(
        CollectorRunStateMachine machine,
        CollectorRunEvent @event,
        CollectorRunState state)
    {
        var transition = machine.Transition(@event);

        Assert.Equal(state, transition.OldState);
        Assert.Equal(state, transition.NewState);
        Assert.False(transition.Allowed);
        Assert.Equal(CollectorTransitionReasons.InvalidTransition, transition.Reason);
        Assert.Equal(state, machine.CurrentState);
    }
}

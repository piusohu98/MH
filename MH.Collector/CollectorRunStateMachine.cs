namespace MH.Collector;

public enum CollectorRunState
{
    Idle = 0,
    Observing = 1,
    Capturing = 2,
    Recognizing = 3,
    ReviewRequired = 4,
    PausedForLogin = 5,
    PausedForUpdate = 6,
    PausedForCaptcha = 7,
    Disconnected = 8,
    UnknownPage = 9,
    Stopped = 10
}

public enum CollectorRunEvent
{
    StartObserving = 0,
    CaptureStarted = 1,
    RecognitionStarted = 2,
    Accepted = 3,
    ReviewRequired = 4,
    LoginDetected = 5,
    UpdateDetected = 6,
    CaptchaDetected = 7,
    Disconnected = 8,
    UnknownPage = 9,
    StopRequested = 10,
    Reset = 11,
    Resume = 12
}

public static class CollectorTransitionReasons
{
    public const string StartedObserving = "started-observing";
    public const string CaptureStarted = "capture-started";
    public const string RecognitionStarted = "recognition-started";
    public const string Accepted = "accepted";
    public const string ReviewRequired = "review-required";
    public const string LoginDetected = "login-detected";
    public const string UpdateDetected = "update-detected";
    public const string CaptchaDetected = "captcha-detected";
    public const string Disconnected = "disconnected";
    public const string UnknownPage = "unknown-page";
    public const string StopRequested = "stop-requested";
    public const string ManualReset = "manual-reset";
    public const string ManualResume = "manual-resume";
    public const string InvalidTransition = "invalid-transition";
    public const string UnknownEvent = "unknown-event";
}

public sealed record CollectorTransition(
    CollectorRunState OldState,
    CollectorRunState NewState,
    bool Allowed,
    string Reason,
    CollectorRunEvent Event);

public sealed class CollectorRunStateMachine
{
    public CollectorRunStateMachine(CollectorRunState initialState = CollectorRunState.Idle)
    {
        if (!Enum.IsDefined(initialState))
        {
            throw new ArgumentOutOfRangeException(nameof(initialState), initialState, "初始采集状态无效。");
        }

        CurrentState = initialState;
    }

    public CollectorRunState CurrentState { get; private set; }

    public CollectorTransition Transition(CollectorRunEvent @event)
    {
        var oldState = CurrentState;
        if (!Enum.IsDefined(@event))
        {
            return Denied(oldState, @event, CollectorTransitionReasons.UnknownEvent);
        }

        if (!TryTransition(oldState, @event, out var newState, out var reason))
        {
            return Denied(oldState, @event, CollectorTransitionReasons.InvalidTransition);
        }

        CurrentState = newState;
        return new CollectorTransition(oldState, newState, true, reason, @event);
    }

    private static bool TryTransition(
        CollectorRunState state,
        CollectorRunEvent @event,
        out CollectorRunState newState,
        out string reason)
    {
        newState = state;
        reason = CollectorTransitionReasons.InvalidTransition;

        switch (@event)
        {
            case CollectorRunEvent.StartObserving when state == CollectorRunState.Idle:
                newState = CollectorRunState.Observing;
                reason = CollectorTransitionReasons.StartedObserving;
                return true;

            case CollectorRunEvent.CaptureStarted when state == CollectorRunState.Observing:
                newState = CollectorRunState.Capturing;
                reason = CollectorTransitionReasons.CaptureStarted;
                return true;

            case CollectorRunEvent.RecognitionStarted when state == CollectorRunState.Capturing:
                newState = CollectorRunState.Recognizing;
                reason = CollectorTransitionReasons.RecognitionStarted;
                return true;

            case CollectorRunEvent.Accepted when state == CollectorRunState.Recognizing:
                newState = CollectorRunState.Observing;
                reason = CollectorTransitionReasons.Accepted;
                return true;

            case CollectorRunEvent.ReviewRequired when state == CollectorRunState.Recognizing:
                newState = CollectorRunState.ReviewRequired;
                reason = CollectorTransitionReasons.ReviewRequired;
                return true;

            case CollectorRunEvent.LoginDetected when CanObserveOrProcess(state):
                newState = CollectorRunState.PausedForLogin;
                reason = CollectorTransitionReasons.LoginDetected;
                return true;

            case CollectorRunEvent.UpdateDetected when CanObserveOrProcess(state):
                newState = CollectorRunState.PausedForUpdate;
                reason = CollectorTransitionReasons.UpdateDetected;
                return true;

            case CollectorRunEvent.CaptchaDetected when CanObserveOrProcess(state):
                newState = CollectorRunState.PausedForCaptcha;
                reason = CollectorTransitionReasons.CaptchaDetected;
                return true;

            case CollectorRunEvent.Disconnected when CanObserveOrProcess(state):
                newState = CollectorRunState.Disconnected;
                reason = CollectorTransitionReasons.Disconnected;
                return true;

            case CollectorRunEvent.UnknownPage when CanObserveOrProcess(state):
                newState = CollectorRunState.UnknownPage;
                reason = CollectorTransitionReasons.UnknownPage;
                return true;

            case CollectorRunEvent.StopRequested when state != CollectorRunState.Stopped:
                newState = CollectorRunState.Stopped;
                reason = CollectorTransitionReasons.StopRequested;
                return true;

            case CollectorRunEvent.Reset when state != CollectorRunState.Idle:
                newState = CollectorRunState.Idle;
                reason = CollectorTransitionReasons.ManualReset;
                return true;

            case CollectorRunEvent.Resume when RequiresManualRecovery(state):
                newState = CollectorRunState.Observing;
                reason = CollectorTransitionReasons.ManualResume;
                return true;

            default:
                return false;
        }
    }

    private static bool CanObserveOrProcess(CollectorRunState state)
        => state is CollectorRunState.Idle
            or CollectorRunState.Observing
            or CollectorRunState.Capturing
            or CollectorRunState.Recognizing;

    private static bool RequiresManualRecovery(CollectorRunState state)
        => state is CollectorRunState.ReviewRequired
            or CollectorRunState.PausedForLogin
            or CollectorRunState.PausedForUpdate
            or CollectorRunState.PausedForCaptcha
            or CollectorRunState.Disconnected
            or CollectorRunState.UnknownPage;

    private static CollectorTransition Denied(
        CollectorRunState state,
        CollectorRunEvent @event,
        string reason)
        => new(state, state, false, reason, @event);
}

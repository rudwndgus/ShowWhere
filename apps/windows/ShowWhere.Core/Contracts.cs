using System.Text.Json.Serialization;

namespace ShowWhere.Core;

public sealed record UiBounds(double X, double Y, double Width, double Height);

public sealed record UiCandidate(
    string Id,
    string? Label,
    string? Description,
    string Role,
    bool Enabled,
    bool Visible,
    bool Clickable,
    UiBounds Bounds,
    IReadOnlyDictionary<string, object?>? Attributes = null);

public sealed record ApplicationContext(
    string Platform,
    string ApplicationName,
    string? WindowTitle = null,
    string? Url = null,
    string? Locale = null);

public sealed record TaskSession(
    string SessionId,
    string OriginalUserMessage,
    string? Goal,
    string Mode,
    string Status,
    IReadOnlyList<string> CompletedSteps,
    IReadOnlyList<string> KnownFacts,
    int FailureCount,
    string? CurrentStep = null,
    string? ExpectedChange = null);

public sealed record GuideRequest(
    TaskSession Session,
    ApplicationContext Context,
    IReadOnlyList<UiCandidate> Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Screenshot = null);

public sealed record GuideDecision(
    string Status,
    string Action,
    string Message,
    double Confidence,
    string? TargetId = null,
    string? ExpectedChange = null,
    string? SafeToolId = null,
    IReadOnlyList<string>? AlternativeTargetIds = null);

public static class Platforms
{
    public const string Browser = "browser";
    public const string Windows = "windows";
    public const string Android = "android";
}

public static class GuideActions
{
    public const string Highlight = "highlight";
    public const string AskUser = "ask_user";
    public const string Explain = "explain";
    public const string RequestNewObservation = "request_new_observation";
    public const string RequestVision = "request_vision";
    public const string RequestSafeTool = "request_safe_tool";
}

public static class GuideStatuses
{
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string NeedsClarification = "needs_clarification";
    public const string Blocked = "blocked";
}

public static class TaskStatuses
{
    public const string Idle = "idle";
    public const string Observing = "observing";
    public const string WaitingForAi = "waiting_for_ai";
    public const string Guiding = "guiding";
    public const string WaitingForUser = "waiting_for_user";
    public const string Completed = "completed";
    public const string Blocked = "blocked";
    public const string Cancelled = "cancelled";
}

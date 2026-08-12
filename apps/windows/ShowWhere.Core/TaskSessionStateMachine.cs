namespace ShowWhere.Core;

public static class TaskSessionStateMachine
{
    public static TaskSession Create(string userMessage, Func<string>? createId = null)
    {
        var cleaned = userMessage.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            throw new ArgumentException("A task message is required.", nameof(userMessage));
        return new TaskSession(
            (createId ?? (() => Guid.NewGuid().ToString("D")))(),
            cleaned,
            cleaned,
            InferMode(cleaned),
            TaskStatuses.Idle,
            [],
            [],
            0);
    }

    public static string InferMode(string message)
    {
        var normalized = message.ToLowerInvariant();
        string[] troubleshootingTerms = ["안 돼", "안됨", "오류", "에러", "문제", "못 ", "보이지", "실패", "stuck", "error", "problem", "not showing"];
        return troubleshootingTerms.Any(normalized.Contains) ? "troubleshooting" : "guidance";
    }

    public static TaskSession ObservationStarted(TaskSession session) => session with { Status = TaskStatuses.Observing };
    public static TaskSession AiRequested(TaskSession session) => session with { Status = TaskStatuses.WaitingForAi };
    public static TaskSession GuidanceReady(TaskSession session, string message, string? expectedChange) => session with
    {
        Status = TaskStatuses.Guiding,
        CurrentStep = message,
        ExpectedChange = expectedChange,
    };
    public static TaskSession WaitingForUser(TaskSession session, string? message = null) => session with
    {
        Status = TaskStatuses.WaitingForUser,
        CurrentStep = message ?? session.CurrentStep,
    };
    public static TaskSession StepCompleted(TaskSession session, string? fact = null)
    {
        var completed = session.CompletedSteps.ToList();
        if (!string.IsNullOrWhiteSpace(session.CurrentStep) && !completed.Contains(session.CurrentStep))
            completed.Add(session.CurrentStep);
        var facts = session.KnownFacts.ToList();
        if (!string.IsNullOrWhiteSpace(fact) && !facts.Contains(fact)) facts.Add(fact);
        return session with
        {
            Status = TaskStatuses.Observing,
            CurrentStep = null,
            ExpectedChange = null,
            CompletedSteps = completed,
            KnownFacts = facts,
        };
    }
    public static TaskSession Failed(TaskSession session, string? message = null) => session with
    {
        Status = TaskStatuses.Observing,
        CurrentStep = message ?? session.CurrentStep,
        FailureCount = session.FailureCount + 1,
    };
    public static TaskSession Completed(TaskSession session, string? message = null) => session with
    {
        Status = TaskStatuses.Completed,
        CurrentStep = message ?? session.CurrentStep,
        ExpectedChange = null,
    };
    public static TaskSession Cancelled(TaskSession session) => session with
    {
        Status = TaskStatuses.Cancelled,
        ExpectedChange = null,
    };
}

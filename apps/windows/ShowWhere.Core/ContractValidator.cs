namespace ShowWhere.Core;

public sealed class ContractValidationException : Exception
{
    public ContractValidationException(string message) : base(message) { }
}

public static class ContractValidator
{
    public const double DefaultConfidenceThreshold = 0.65;

    private static readonly HashSet<string> AllowedActions =
    [
        GuideActions.Highlight,
        GuideActions.HighlightVisual,
        GuideActions.AskUser,
        GuideActions.Explain,
        GuideActions.RequestNewObservation,
        GuideActions.RequestVision,
        GuideActions.RequestSafeTool,
    ];

    private static readonly HashSet<string> AllowedDecisionStatuses =
    [
        GuideStatuses.InProgress,
        GuideStatuses.Completed,
        GuideStatuses.NeedsClarification,
        GuideStatuses.Blocked,
    ];

    public static void Validate(GuideRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Context.Platform != Platforms.Windows)
            throw new ContractValidationException("Unsupported application platform.");
        if (string.IsNullOrWhiteSpace(request.Context.ApplicationName) || request.Context.ApplicationName.Length > 300)
            throw new ContractValidationException("Application name is invalid.");
        if (string.IsNullOrWhiteSpace(request.Session.SessionId) || request.Session.SessionId.Length > 160)
            throw new ContractValidationException("Session ID is invalid.");
        if (string.IsNullOrWhiteSpace(request.Session.OriginalUserMessage) || request.Session.OriginalUserMessage.Length > 4_000)
            throw new ContractValidationException("User message is invalid.");
        if (request.Candidates.Count > 250)
            throw new ContractValidationException("Too many UI candidates.");
        if (request.Screenshot?.Length > 12_000_000)
            throw new ContractValidationException("Screenshot payload is too large.");
        if ((request.Screenshot is null) != (request.ScreenshotBounds is null))
            throw new ContractValidationException("Screenshot data and bounds must be supplied together.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in request.Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Id) || candidate.Id.Length > 160 || !ids.Add(candidate.Id))
                throw new ContractValidationException("Candidate ID is invalid or duplicated.");
            if (string.IsNullOrWhiteSpace(candidate.Role) || candidate.Role.Length > 100)
                throw new ContractValidationException("Candidate role is invalid.");
            if (!double.IsFinite(candidate.Bounds.X) || !double.IsFinite(candidate.Bounds.Y)
                || !double.IsFinite(candidate.Bounds.Width) || !double.IsFinite(candidate.Bounds.Height)
                || candidate.Bounds.Width < 0 || candidate.Bounds.Height < 0)
                throw new ContractValidationException("Candidate bounds are invalid.");
        }
    }

    public static GuideDecision ValidateDecision(
        GuideDecision decision,
        GuideRequest request,
        double confidenceThreshold = DefaultConfidenceThreshold)
    {
        ArgumentNullException.ThrowIfNull(decision);
        Validate(request);
        if (!AllowedDecisionStatuses.Contains(decision.Status) || !AllowedActions.Contains(decision.Action))
            throw new ContractValidationException("Guide decision status or action is invalid.");
        if (string.IsNullOrWhiteSpace(decision.Message) || decision.Message.Length > 500)
            throw new ContractValidationException("Guide decision message is invalid.");
        if (!double.IsFinite(decision.Confidence) || decision.Confidence is < 0 or > 1)
            throw new ContractValidationException("Guide decision confidence is invalid.");
        if (decision.Action == GuideActions.Highlight)
        {
            if (string.IsNullOrWhiteSpace(decision.TargetId)
                || request.Candidates.All(candidate => candidate.Id != decision.TargetId))
                throw new ContractValidationException("Guide decision referenced an unknown target ID.");
            if (decision.Confidence < confidenceThreshold)
            {
                return new GuideDecision(
                    GuideStatuses.NeedsClarification,
                    GuideActions.AskUser,
                    "어느 항목인지 확실하지 않아요. 화면에 보이는 이름을 조금 더 알려주세요.",
                    0);
            }
        }
        if (decision.Action == GuideActions.HighlightVisual)
        {
            var target = decision.VisualTarget;
            if (request.Screenshot is null || request.ScreenshotBounds is null || target is null
                || !double.IsFinite(target.X) || !double.IsFinite(target.Y)
                || !double.IsFinite(target.Width) || !double.IsFinite(target.Height)
                || target.X < 0 || target.Y < 0 || target.Width < 0.005 || target.Height < 0.005
                || target.X + target.Width > 1 || target.Y + target.Height > 1
                || string.IsNullOrWhiteSpace(target.Label))
                throw new ContractValidationException("Visual target is invalid or has no matching screenshot.");
            if (decision.Confidence < confidenceThreshold)
                return new GuideDecision(
                    GuideStatuses.NeedsClarification,
                    GuideActions.AskUser,
                    "화면에서 정확한 위치를 확신할 수 없어요. 원하는 항목의 이름을 조금 더 알려주세요.",
                    0);
        }
        if (decision.Action == GuideActions.RequestSafeTool && string.IsNullOrWhiteSpace(decision.SafeToolId))
            throw new ContractValidationException("Safe tool decision did not include a tool ID.");
        if (decision.AlternativeTargetIds is not null)
        {
            if (decision.Action != GuideActions.AskUser
                || decision.AlternativeTargetIds.Count is < 2 or > 4
                || decision.AlternativeTargetIds.Distinct(StringComparer.Ordinal).Count() != decision.AlternativeTargetIds.Count
                || decision.AlternativeTargetIds.Any(id => request.Candidates.All(candidate => candidate.Id != id)))
                throw new ContractValidationException("Guide decision alternatives are invalid.");
        }
        return decision;
    }
}

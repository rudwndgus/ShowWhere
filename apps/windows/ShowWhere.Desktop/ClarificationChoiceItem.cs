namespace ShowWhere.Desktop;

public sealed record ClarificationChoiceItem(
    string Label,
    string? ResolvedGoal = null,
    string? TargetId = null);

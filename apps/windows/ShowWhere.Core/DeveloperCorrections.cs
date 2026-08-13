using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ShowWhere.Core;

public sealed record CorrectionTargetSignature(
    string? Label,
    string? Description,
    string Role,
    string? AutomationId,
    string? ClassName,
    string? ControlType,
    string? ProcessName,
    string? SourceScope,
    string? ContainerLabel);

public sealed record DeveloperLearningLabels(
    string TaskId,
    string StateId,
    string TargetConcept,
    string ExpectedNextState,
    IReadOnlyList<string> ExpectedEvidence,
    string OutcomeLabel,
    string Authority = "human_gold");

public sealed record DeveloperCompletionRecord(
    int SchemaVersion,
    string Id,
    DateTimeOffset CreatedAtUtc,
    string OriginalGoal,
    string EffectiveGoal,
    ApplicationContext Context,
    string? SnapshotHash,
    IReadOnlyList<string> VisibleEvidence,
    DeveloperLearningLabels LearningLabels,
    bool DeveloperVerified = true,
    string? FeedbackId = null,
    string? DeveloperComment = null);

public sealed record DeveloperCorrectionRecord(
    int SchemaVersion,
    string Id,
    DateTimeOffset CreatedAtUtc,
    string OriginalGoal,
    string EffectiveGoal,
    string? CorrectedIntent,
    ApplicationContext Context,
    string? SnapshotHash,
    string? PreviousAction,
    string? PreviousTargetId,
    string? PreviousTargetLabel,
    UiBounds? PreviousBounds,
    UiBounds? SelectedBounds,
    VisualTarget? NormalizedVisualTarget,
    CorrectionTargetSignature? CorrectTarget,
    string? ScreenshotPath,
    bool DeveloperVerified = true,
    string? FeedbackId = null,
    string? DeveloperComment = null,
    string? RefinedComment = null,
    IReadOnlyList<string>? IssueTags = null,
    DeveloperLearningLabels? LearningLabels = null);

public sealed record AnswerFeedbackRecord(
    int SchemaVersion,
    string Id,
    DateTimeOffset CreatedAtUtc,
    string Rating,
    string AnswerId,
    string AnswerText,
    string? OriginalGoal,
    string? EffectiveGoal,
    ApplicationContext? Context,
    string? SnapshotHash,
    string? Action,
    string? TargetId,
    string? TargetLabel,
    UiBounds? TargetBounds);

public sealed record DeveloperLearningStatusRecord(
    int SchemaVersion,
    string Id,
    DateTimeOffset CreatedAtUtc,
    string FeedbackId,
    bool Active,
    string? Reason = null);

public sealed record DeveloperLearningEditRecord(
    int SchemaVersion,
    string Id,
    DateTimeOffset CreatedAtUtc,
    string FeedbackId,
    string Rating,
    string Goal,
    string Answer,
    string? TargetLabel,
    string? Comment);

public sealed record DeveloperLearningHistoryRecord(
    string FeedbackId,
    DateTimeOffset CreatedAtUtc,
    string Rating,
    string Goal,
    string Answer,
    string? TargetLabel,
    string? Comment,
    bool Active,
    bool HasEdits = false);

public sealed record RefinedDeveloperComment(
    string Raw,
    string Normalized,
    IReadOnlyList<string> IssueTags);

public static partial class DeveloperLearningPrivacy
{
    public static string? Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var result = EmailPattern().Replace(value, "[email]");
        result = OrderIdPattern().Replace(result, "[order-id]");
        result = PaymentPattern().Replace(result, "[payment]");
        result = PhonePattern().Replace(result, "[phone]");
        return result;
    }

    public static ApplicationContext Redact(ApplicationContext context) => context with
    {
        WindowTitle = Redact(context.WindowTitle),
        Url = RedactUrl(context.Url),
    };

    private static string? RedactUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return Redact(value);
        try
        {
            var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty, Query = string.Empty, Fragment = string.Empty };
            return builder.Uri.ToString();
        }
        catch (UriFormatException) { return Redact(value); }
    }

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b\d{3}-\d{7}-\d{7}\b", RegexOptions.CultureInvariant)]
    private static partial Regex OrderIdPattern();

    [GeneratedRegex(@"\b(?:ending\s+in|last\s+four|끝자리)\s*\d{4}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PaymentPattern();

    [GeneratedRegex(@"(?:\+?\d[\s().-]*){9,}", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}

public static class DeveloperCompletionEvidence
{
    private static readonly HashSet<string> GenericValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "설정", "settings", "홈", "home", "뒤로", "back", "닫기", "close", "최소화", "최대화",
        "windows", "systemsettings", "chrome", "edge", "button", "window",
    };

    public static IReadOnlyList<string> Build(
        ApplicationContext context,
        IReadOnlyList<UiCandidate> candidates)
    {
        var values = new List<string>();
        Add(values, context.Url);
        Add(values, context.WindowTitle);
        foreach (var candidate in candidates.Where(item => item.Visible)
                     .OrderBy(item => item.Bounds.Y).ThenBy(item => item.Bounds.X))
        {
            Add(values, candidate.Label);
            if (values.Count >= 24) break;
        }
        if (values.Count == 0) Add(values, context.ApplicationName, allowGeneric: true);
        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsPresent(
        string evidence,
        ApplicationContext context,
        IReadOnlyList<UiCandidate> candidates)
    {
        if (!IsStrong(evidence)) return false;
        var state = string.Join(' ', new[] { context.ApplicationName, context.WindowTitle, context.Url }
            .Concat(candidates.Where(item => item.Visible).SelectMany(item => new[] { item.Label, item.Description }))
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return state.Contains(evidence.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static void Add(List<string> values, string? value, bool allowGeneric = false)
    {
        var trimmed = DeveloperLearningPrivacy.Redact(value)?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length < 2) return;
        if (!allowGeneric && !IsStrong(trimmed)) return;
        values.Add(trimmed);
    }

    private static bool IsStrong(string value) => value.Trim().Length >= 3
        && !GenericValues.Contains(value.Trim());
}

public static class DeveloperPositiveFeedback
{
    public static DeveloperCorrectionRecord? Create(
        AnswerFeedbackRecord feedback,
        CorrectionTargetSignature? targetSignature,
        VisualTarget? normalizedVisualTarget = null)
    {
        if (!string.Equals(feedback.Rating, "correct", StringComparison.OrdinalIgnoreCase)
            || (feedback.Action != GuideActions.Highlight && feedback.Action != GuideActions.HighlightVisual)
            || (targetSignature is null && normalizedVisualTarget is null)
            || feedback.Context is null
            || string.IsNullOrWhiteSpace(feedback.OriginalGoal)) return null;

        var targetConcept = targetSignature?.Label
            ?? targetSignature?.Description
            ?? targetSignature?.AutomationId
            ?? normalizedVisualTarget?.Label
            ?? targetSignature?.Role
            ?? "visual target";
        return new DeveloperCorrectionRecord(
            1,
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow,
            feedback.OriginalGoal,
            string.IsNullOrWhiteSpace(feedback.EffectiveGoal) ? feedback.OriginalGoal : feedback.EffectiveGoal,
            null,
            feedback.Context,
            feedback.SnapshotHash,
            feedback.Action,
            feedback.TargetId,
            feedback.TargetLabel,
            feedback.TargetBounds,
            feedback.TargetBounds,
            normalizedVisualTarget,
            targetSignature,
            null,
            true,
            feedback.Id,
            null,
            "Developer explicitly marked this answer correct.",
            ["positive_feedback", "human_gold"],
            DeveloperLabeling.CreatePositiveLabels(feedback, targetConcept));
    }
}

public static class DeveloperReplayPolicy
{
    public static bool CanReuseImmediately(
        string? targetId,
        bool snapshotMatches,
        bool liveTargetResolved,
        bool developerVerified = true) =>
        string.IsNullOrWhiteSpace(targetId)
            ? snapshotMatches
            : liveTargetResolved && (developerVerified || snapshotMatches);

    public static bool CanReuseSafeReply(
        string action,
        string status,
        bool snapshotMatches) =>
        snapshotMatches
        && action is GuideActions.AskUser or GuideActions.Explain
        && status is not GuideStatuses.Completed and not GuideStatuses.Blocked;
}

public static class DeveloperLabeling
{
    public static readonly string[] OutcomeLabels =
    [
        "correct_target",
        "wrong_intent",
        "wrong_application",
        "wrong_scope",
        "wrong_target",
        "overlay_missing",
        "missing_detail",
        "ambiguous_request",
        "visual_grounding_error",
        "task_completed",
    ];

    public static DeveloperLearningLabels CreatePositiveLabels(
        AnswerFeedbackRecord feedback,
        string targetConcept) => new(
            StableLabel("task", feedback.EffectiveGoal ?? feedback.OriginalGoal ?? "unknown"),
            StableLabel("state", $"{feedback.Context?.ApplicationName}.{feedback.Context?.WindowTitle}"),
            NormalizeConcept(targetConcept),
            StableLabel("state", $"after.{targetConcept}"),
            [targetConcept],
            "correct_target");

    public static DeveloperLearningLabels CreateCorrectionLabels(
        string originalGoal,
        ApplicationContext context,
        string targetConcept,
        string? taskId,
        string? stateId,
        string? expectedNextState,
        string? expectedEvidence,
        string? outcomeLabel)
    {
        var evidence = (expectedEvidence ?? string.Empty)
            .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (evidence.Length == 0 && !string.IsNullOrWhiteSpace(targetConcept)) evidence = [targetConcept];
        return new DeveloperLearningLabels(
            NormalizeProvided(taskId) ?? StableLabel("task", originalGoal),
            NormalizeProvided(stateId) ?? StableLabel("state", $"{context.ApplicationName}.{context.WindowTitle}"),
            NormalizeProvided(targetConcept) ?? "unknown.target",
            NormalizeProvided(expectedNextState) ?? StableLabel("state", $"after.{targetConcept}"),
            evidence,
            OutcomeLabels.Contains(outcomeLabel, StringComparer.Ordinal) ? outcomeLabel! : "wrong_target");
    }

    private static string NormalizeConcept(string value) =>
        NormalizeProvided(value) ?? "unknown.target";

    private static string StableLabel(string prefix, string value)
    {
        var normalized = NormalizeProvided(value) ?? "unknown";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..10];
        return $"{prefix}.{normalized[..Math.Min(normalized.Length, 48)]}.{hash}";
    }

    private static string? NormalizeProvided(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var output = new string(value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '.')
            .ToArray());
        while (output.Contains("..", StringComparison.Ordinal)) output = output.Replace("..", ".", StringComparison.Ordinal);
        return output.Trim('.');
    }
}

public interface IDeveloperCorrectionStore
{
    string DataDirectory { get; }
    string ResolveIntent(string originalGoal);
    bool TryResolveTarget(
        string goal,
        ApplicationContext context,
        IReadOnlyList<UiCandidate> candidates,
        out UiCandidate target,
        out DeveloperCorrectionRecord correction);
    bool TryResolveVisualTarget(
        string goal,
        ApplicationContext context,
        string snapshotHash,
        out VisualTarget target,
        out DeveloperCorrectionRecord correction);
    bool TryResolveCompletion(
        string goal,
        ApplicationContext context,
        IReadOnlyList<UiCandidate> candidates,
        out DeveloperCompletionRecord completion);
    IReadOnlyList<UiCandidate> FilterRejectedCandidates(
        string goal,
        ApplicationContext context,
        IReadOnlyList<UiCandidate> candidates);
    IReadOnlyList<DeveloperLearningHistoryRecord> GetHistory();
    Task SetFeedbackActiveAsync(
        string feedbackId,
        bool active,
        string? reason = null,
        CancellationToken cancellationToken = default);
    Task<DeveloperLearningEditRecord> SaveHistoryEditAsync(
        DeveloperLearningEditRecord edit,
        CancellationToken cancellationToken = default);
    Task<DeveloperCorrectionRecord> SaveAsync(
        DeveloperCorrectionRecord correction,
        string? screenshotDataUrl,
        CancellationToken cancellationToken = default);
    Task<AnswerFeedbackRecord> SaveFeedbackAsync(
        AnswerFeedbackRecord feedback,
        CancellationToken cancellationToken = default);
    Task<DeveloperCompletionRecord> SaveCompletionAsync(
        DeveloperCompletionRecord completion,
        CancellationToken cancellationToken = default);
}

public sealed class JsonlDeveloperCorrectionStore : IDeveloperCorrectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
    private readonly object _gate = new();
    private readonly string _recordsPath;
    private readonly string _feedbackPath;
    private readonly string _completionsPath;
    private readonly string _statusPath;
    private readonly string _editsPath;
    private List<DeveloperCorrectionRecord> _records;
    private List<DeveloperCompletionRecord> _completions;
    private List<AnswerFeedbackRecord> _feedback;
    private List<DeveloperLearningStatusRecord> _statusChanges;
    private List<DeveloperLearningEditRecord> _edits;

    public JsonlDeveloperCorrectionStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? ResolveDefaultDataDirectory();
        _recordsPath = Path.Combine(DataDirectory, "corrections.jsonl");
        _feedbackPath = Path.Combine(DataDirectory, "answer-feedback.jsonl");
        _completionsPath = Path.Combine(DataDirectory, "completions.jsonl");
        _statusPath = Path.Combine(DataDirectory, "learning-status.jsonl");
        _editsPath = Path.Combine(DataDirectory, "learning-edits.jsonl");
        Directory.CreateDirectory(DataDirectory);
        _records = LoadRecords(_recordsPath);
        _completions = LoadCompletionRecords(_completionsPath);
        _feedback = LoadFeedbackRecords(_feedbackPath);
        _statusChanges = LoadStatusRecords(_statusPath);
        _edits = LoadEditRecords(_editsPath);
    }

    public string DataDirectory { get; }

    public string ResolveIntent(string originalGoal)
    {
        if (string.IsNullOrWhiteSpace(originalGoal)) return originalGoal.Trim();
        lock (_gate)
        {
            return _records
                .Where(IsCorrectionUsable)
                .Where(record => RecordMatchesGoal(originalGoal, record.FeedbackId, record.OriginalGoal, record.EffectiveGoal, record.CorrectedIntent))
                .OrderByDescending(record => record.CreatedAtUtc)
                .Select(record => record.CorrectedIntent)
                .FirstOrDefault(intent => !string.IsNullOrWhiteSpace(intent))
                ?.Trim() ?? originalGoal.Trim();
        }
    }

    public bool TryResolveTarget(
        string goal,
        ApplicationContext context,
        IReadOnlyList<UiCandidate> candidates,
        out UiCandidate target,
        out DeveloperCorrectionRecord correction)
    {
        target = null!;
        correction = null!;
        List<DeveloperCorrectionRecord> matchingRecords;
        lock (_gate)
        {
            matchingRecords = _records
                .Where(IsCorrectionUsable)
                .Where(record => record.CorrectTarget is not null)
                .Where(record => RecordMatchesGoal(goal, record.FeedbackId, record.OriginalGoal, record.EffectiveGoal, record.CorrectedIntent))
                .OrderByDescending(record => record.CreatedAtUtc)
                .ToList();
        }

        foreach (var record in matchingRecords)
        {
            var ranked = candidates
                .Where(candidate => candidate.Visible && candidate.Enabled && candidate.Clickable)
                .Select(candidate => new { Candidate = candidate, Score = ScoreTarget(candidate, record, context) })
                .Where(item => item.Score >= 300)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Candidate.Bounds.Y)
                .ThenBy(item => item.Candidate.Bounds.X)
                .FirstOrDefault();
            if (ranked is null) continue;
            target = ranked.Candidate;
            correction = record;
            return true;
        }
        return false;
    }

    public bool TryResolveVisualTarget(
        string goal,
        ApplicationContext context,
        string snapshotHash,
        out VisualTarget target,
        out DeveloperCorrectionRecord correction)
    {
        target = null!;
        correction = null!;
        lock (_gate)
        {
            var match = _records
                .Where(IsCorrectionUsable)
                .Where(record => record.NormalizedVisualTarget is not null)
                .Where(record => RecordMatchesGoal(goal, record.FeedbackId, record.OriginalGoal, record.EffectiveGoal, record.CorrectedIntent))
                .Where(record => string.Equals(record.SnapshotHash, snapshotHash, StringComparison.Ordinal)
                    && string.Equals(record.Context.ApplicationName, context.ApplicationName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.Context.WindowTitle, context.WindowTitle, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(record => record.CreatedAtUtc)
                .FirstOrDefault();
            if (match is null) return false;
            target = match.NormalizedVisualTarget!;
            correction = match;
            return true;
        }
    }

    public bool TryResolveCompletion(
        string goal,
        ApplicationContext context,
        IReadOnlyList<UiCandidate> candidates,
        out DeveloperCompletionRecord completion)
    {
        completion = null!;
        lock (_gate)
        {
            var match = _completions
                .Where(record => IsFeedbackActive(record.FeedbackId))
                .Where(record => string.Equals(GetEffectiveRating(record.FeedbackId, "completed"), "completed", StringComparison.OrdinalIgnoreCase))
                .Where(record => RecordMatchesGoal(goal, record.FeedbackId, record.OriginalGoal, record.EffectiveGoal, null))
                .Where(record => CompletionContextMatches(record.Context, context))
                .Where(record => record.LearningLabels.ExpectedEvidence.Any(evidence =>
                    DeveloperCompletionEvidence.IsPresent(evidence, context, candidates)))
                .OrderByDescending(record => record.CreatedAtUtc)
                .FirstOrDefault();
            if (match is null) return false;
            completion = match;
            return true;
        }
    }

    public async Task<DeveloperCorrectionRecord> SaveAsync(
        DeveloperCorrectionRecord correction,
        string? screenshotDataUrl,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(DataDirectory);
        var saved = Sanitize(correction);
        if (!string.IsNullOrWhiteSpace(screenshotDataUrl))
        {
            var separator = screenshotDataUrl.IndexOf(',');
            if (separator > 0 && screenshotDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var extension = screenshotDataUrl[..separator].Contains("png", StringComparison.OrdinalIgnoreCase)
                    ? ".png"
                    : ".jpg";
                var relativePath = Path.Combine("screenshots", correction.Id + extension);
                var absolutePath = Path.Combine(DataDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
                var bytes = Convert.FromBase64String(screenshotDataUrl[(separator + 1)..]);
                await File.WriteAllBytesAsync(absolutePath, bytes, cancellationToken).ConfigureAwait(false);
                saved = saved with { ScreenshotPath = relativePath.Replace('\\', '/') };
            }
        }

        var line = JsonSerializer.Serialize(saved, JsonOptions) + Environment.NewLine;
        lock (_gate)
        {
            File.AppendAllText(_recordsPath, line);
            _records.Add(saved);
        }
        return saved;
    }

    public Task<AnswerFeedbackRecord> SaveFeedbackAsync(
        AnswerFeedbackRecord feedback,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(DataDirectory);
        var saved = Sanitize(feedback);
        var line = JsonSerializer.Serialize(saved, JsonOptions) + Environment.NewLine;
        lock (_gate)
        {
            File.AppendAllText(_feedbackPath, line);
            _feedback.Add(saved);
        }
        return Task.FromResult(saved);
    }

    public IReadOnlyList<UiCandidate> FilterRejectedCandidates(
        string goal,
        ApplicationContext context,
        IReadOnlyList<UiCandidate> candidates)
    {
        HashSet<string> rejectedIds;
        lock (_gate)
        {
            rejectedIds = _feedback
                .Where(item => IsFeedbackActive(item.Id))
                .Where(item => !string.IsNullOrWhiteSpace(item.TargetId)
                    && item.Context is not null
                    && DeveloperIntentMatcher.IsSameIntent(goal, GetEffectiveGoal(item.Id, item.OriginalGoal ?? item.EffectiveGoal))
                    && string.Equals(item.Context.ApplicationName, context.ApplicationName, StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => item.TargetId!, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(item => item.CreatedAtUtc).First())
                .Where(item => string.Equals(GetEffectiveRating(item.Id, item.Rating), "incorrect", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.TargetId!)
                .ToHashSet(StringComparer.Ordinal);
        }
        if (rejectedIds.Count == 0) return candidates;
        var filtered = candidates.Where(candidate => !rejectedIds.Contains(candidate.Id)).ToArray();
        return filtered.Length == 0 ? candidates : filtered;
    }

    public IReadOnlyList<DeveloperLearningHistoryRecord> GetHistory()
    {
        lock (_gate)
        {
            return _feedback.OrderByDescending(item => item.CreatedAtUtc).Select(item =>
            {
                var correction = _records.LastOrDefault(record =>
                    string.Equals(record.FeedbackId, item.Id, StringComparison.Ordinal));
                var completion = _completions.LastOrDefault(record =>
                    string.Equals(record.FeedbackId, item.Id, StringComparison.Ordinal));
                var edit = GetLatestEdit(item.Id);
                return new DeveloperLearningHistoryRecord(
                    item.Id,
                    item.CreatedAtUtc,
                    edit?.Rating ?? item.Rating,
                    edit?.Goal ?? item.OriginalGoal ?? item.EffectiveGoal ?? "질문 정보 없음",
                    edit?.Answer ?? item.AnswerText,
                    edit?.TargetLabel ?? correction?.CorrectTarget?.Label ?? item.TargetLabel,
                    edit?.Comment ?? correction?.DeveloperComment ?? correction?.RefinedComment ?? completion?.DeveloperComment,
                    IsFeedbackActive(item.Id),
                    edit is not null);
            }).ToArray();
        }
    }

    public Task SetFeedbackActiveAsync(
        string feedbackId,
        bool active,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_feedback.Any(item => string.Equals(item.Id, feedbackId, StringComparison.Ordinal)))
                throw new InvalidOperationException("학습 기록을 찾을 수 없습니다.");
            var record = new DeveloperLearningStatusRecord(
                1, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow,
                feedbackId, active, string.IsNullOrWhiteSpace(reason) ? null : DeveloperLearningPrivacy.Redact(reason.Trim()));
            File.AppendAllText(_statusPath, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
            _statusChanges.Add(record);
        }
        return Task.CompletedTask;
    }

    public Task<DeveloperLearningEditRecord> SaveHistoryEditAsync(
        DeveloperLearningEditRecord edit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(edit.Goal)) throw new ArgumentException("질문은 비워둘 수 없습니다.", nameof(edit));
        if (string.IsNullOrWhiteSpace(edit.Answer)) throw new ArgumentException("답안은 비워둘 수 없습니다.", nameof(edit));
        if (!new[] { "correct", "incorrect", "completed" }.Contains(edit.Rating, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("판정은 O, X 또는 끝이어야 합니다.", nameof(edit));

        var normalized = edit with
        {
            SchemaVersion = 1,
            Id = string.IsNullOrWhiteSpace(edit.Id) ? Guid.NewGuid().ToString("D") : edit.Id,
            CreatedAtUtc = edit.CreatedAtUtc == default ? DateTimeOffset.UtcNow : edit.CreatedAtUtc,
            Rating = edit.Rating.Trim().ToLowerInvariant(),
            Goal = DeveloperLearningPrivacy.Redact(edit.Goal.Trim())!,
            Answer = DeveloperLearningPrivacy.Redact(edit.Answer.Trim())!,
            TargetLabel = string.IsNullOrWhiteSpace(edit.TargetLabel) ? null : DeveloperLearningPrivacy.Redact(edit.TargetLabel.Trim()),
            Comment = string.IsNullOrWhiteSpace(edit.Comment) ? null : DeveloperLearningPrivacy.Redact(edit.Comment.Trim()),
        };
        lock (_gate)
        {
            if (!_feedback.Any(item => string.Equals(item.Id, normalized.FeedbackId, StringComparison.Ordinal)))
                throw new InvalidOperationException("학습 기록을 찾을 수 없습니다.");
            File.AppendAllText(_editsPath, JsonSerializer.Serialize(normalized, JsonOptions) + Environment.NewLine);
            _edits.Add(normalized);
        }
        return Task.FromResult(normalized);
    }

    private bool IsFeedbackActive(string? feedbackId)
    {
        if (string.IsNullOrWhiteSpace(feedbackId)) return true;
        return _statusChanges.LastOrDefault(item =>
            string.Equals(item.FeedbackId, feedbackId, StringComparison.Ordinal))?.Active ?? true;
    }

    private DeveloperLearningEditRecord? GetLatestEdit(string? feedbackId) =>
        string.IsNullOrWhiteSpace(feedbackId) ? null : _edits.LastOrDefault(item =>
            string.Equals(item.FeedbackId, feedbackId, StringComparison.Ordinal));

    private string GetEffectiveRating(string? feedbackId, string fallback) =>
        GetLatestEdit(feedbackId)?.Rating ?? fallback;

    private string GetEffectiveGoal(string? feedbackId, string? fallback) =>
        GetLatestEdit(feedbackId)?.Goal ?? fallback ?? string.Empty;

    private bool IsCorrectionUsable(DeveloperCorrectionRecord record)
    {
        if (!IsFeedbackActive(record.FeedbackId)) return false;
        if (string.IsNullOrWhiteSpace(record.FeedbackId)) return true;
        var feedback = _feedback.LastOrDefault(item => string.Equals(item.Id, record.FeedbackId, StringComparison.Ordinal));
        if (feedback is null) return true;
        var requiredRating = record.IssueTags?.Contains("positive_feedback", StringComparer.OrdinalIgnoreCase) == true
            ? "correct"
            : "incorrect";
        return string.Equals(GetEffectiveRating(record.FeedbackId, feedback.Rating), requiredRating, StringComparison.OrdinalIgnoreCase);
    }

    private bool RecordMatchesGoal(
        string goal,
        string? feedbackId,
        string? originalGoal,
        string? effectiveGoal,
        string? correctedIntent)
    {
        var edit = GetLatestEdit(feedbackId);
        if (edit is not null)
            return DeveloperIntentMatcher.IsSameIntent(goal, edit.Goal)
                || DeveloperIntentMatcher.IsSameIntent(goal, correctedIntent);
        return DeveloperIntentMatcher.IsSameIntent(goal, originalGoal)
            || DeveloperIntentMatcher.IsSameIntent(goal, effectiveGoal)
            || DeveloperIntentMatcher.IsSameIntent(goal, correctedIntent);
    }

    public Task<DeveloperCompletionRecord> SaveCompletionAsync(
        DeveloperCompletionRecord completion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(DataDirectory);
        var saved = Sanitize(completion);
        var line = JsonSerializer.Serialize(saved, JsonOptions) + Environment.NewLine;
        lock (_gate)
        {
            File.AppendAllText(_completionsPath, line);
            _completions.Add(saved);
        }
        return Task.FromResult(saved);
    }

    private static DeveloperCorrectionRecord Sanitize(DeveloperCorrectionRecord value) => value with
    {
        OriginalGoal = DeveloperLearningPrivacy.Redact(value.OriginalGoal)!,
        EffectiveGoal = DeveloperLearningPrivacy.Redact(value.EffectiveGoal)!,
        CorrectedIntent = DeveloperLearningPrivacy.Redact(value.CorrectedIntent),
        Context = DeveloperLearningPrivacy.Redact(value.Context),
        PreviousTargetLabel = DeveloperLearningPrivacy.Redact(value.PreviousTargetLabel),
        CorrectTarget = value.CorrectTarget is null ? null : value.CorrectTarget with
        {
            Label = DeveloperLearningPrivacy.Redact(value.CorrectTarget.Label),
            Description = DeveloperLearningPrivacy.Redact(value.CorrectTarget.Description),
            AutomationId = DeveloperLearningPrivacy.Redact(value.CorrectTarget.AutomationId),
            ContainerLabel = DeveloperLearningPrivacy.Redact(value.CorrectTarget.ContainerLabel),
        },
        DeveloperComment = DeveloperLearningPrivacy.Redact(value.DeveloperComment),
        RefinedComment = DeveloperLearningPrivacy.Redact(value.RefinedComment),
        LearningLabels = value.LearningLabels is null ? null : Sanitize(value.LearningLabels),
    };

    private static AnswerFeedbackRecord Sanitize(AnswerFeedbackRecord value) => value with
    {
        AnswerText = DeveloperLearningPrivacy.Redact(value.AnswerText)!,
        OriginalGoal = DeveloperLearningPrivacy.Redact(value.OriginalGoal),
        EffectiveGoal = DeveloperLearningPrivacy.Redact(value.EffectiveGoal),
        Context = value.Context is null ? null : DeveloperLearningPrivacy.Redact(value.Context),
        TargetLabel = DeveloperLearningPrivacy.Redact(value.TargetLabel),
    };

    private static DeveloperCompletionRecord Sanitize(DeveloperCompletionRecord value) => value with
    {
        OriginalGoal = DeveloperLearningPrivacy.Redact(value.OriginalGoal)!,
        EffectiveGoal = DeveloperLearningPrivacy.Redact(value.EffectiveGoal)!,
        Context = DeveloperLearningPrivacy.Redact(value.Context),
        VisibleEvidence = value.VisibleEvidence.Select(item => DeveloperLearningPrivacy.Redact(item)!).ToArray(),
        LearningLabels = Sanitize(value.LearningLabels),
        DeveloperComment = DeveloperLearningPrivacy.Redact(value.DeveloperComment),
    };

    private static DeveloperLearningLabels Sanitize(DeveloperLearningLabels value) => value with
    {
        TargetConcept = DeveloperLearningPrivacy.Redact(value.TargetConcept)!,
        ExpectedNextState = DeveloperLearningPrivacy.Redact(value.ExpectedNextState)!,
        ExpectedEvidence = value.ExpectedEvidence.Select(item => DeveloperLearningPrivacy.Redact(item)!).ToArray(),
    };

    private static List<DeveloperCorrectionRecord> LoadRecords(string path)
    {
        if (!File.Exists(path)) return [];
        var records = new List<DeveloperCorrectionRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<DeveloperCorrectionRecord>(line, JsonOptions);
                if (record is not null && record.SchemaVersion == 1) records.Add(record);
            }
            catch (JsonException) { }
        }
        return records;
    }

    private static List<DeveloperCompletionRecord> LoadCompletionRecords(string path)
    {
        if (!File.Exists(path)) return [];
        var records = new List<DeveloperCompletionRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<DeveloperCompletionRecord>(line, JsonOptions);
                if (record is not null && record.SchemaVersion == 1 && record.DeveloperVerified) records.Add(record);
            }
            catch (JsonException) { }
        }
        return records;
    }

    private static List<AnswerFeedbackRecord> LoadFeedbackRecords(string path)
    {
        if (!File.Exists(path)) return [];
        var records = new List<AnswerFeedbackRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<AnswerFeedbackRecord>(line, JsonOptions);
                if (record is not null && record.SchemaVersion == 1) records.Add(record);
            }
            catch (JsonException) { }
        }
        return records;
    }

    private static List<DeveloperLearningStatusRecord> LoadStatusRecords(string path)
    {
        if (!File.Exists(path)) return [];
        var records = new List<DeveloperLearningStatusRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<DeveloperLearningStatusRecord>(line, JsonOptions);
                if (record is not null && record.SchemaVersion == 1) records.Add(record);
            }
            catch (JsonException) { }
        }
        return records;
    }

    private static List<DeveloperLearningEditRecord> LoadEditRecords(string path)
    {
        if (!File.Exists(path)) return [];
        var records = new List<DeveloperLearningEditRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<DeveloperLearningEditRecord>(line, JsonOptions);
                if (record is not null && record.SchemaVersion == 1) records.Add(record);
            }
            catch (JsonException) { }
        }
        return records;
    }

    public static string ResolveDefaultDataDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("SHOWWHERE_TRAINING_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured.Trim());

        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "package.json"))
                    && Directory.Exists(Path.Combine(directory.FullName, "apps", "windows")))
                    return Path.Combine(directory.FullName, "training");
                directory = directory.Parent;
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShowWhere",
            "training");
    }

    private int ScoreTarget(
        UiCandidate candidate,
        DeveloperCorrectionRecord record,
        ApplicationContext context)
    {
        var signature = record.CorrectTarget!;
        var editedTargetLabel = GetLatestEdit(record.FeedbackId)?.TargetLabel?.Trim();
        var targetLabel = string.IsNullOrWhiteSpace(editedTargetLabel) ? signature.Label : editedTargetLabel;
        var targetLabelWasEdited = !string.IsNullOrWhiteSpace(editedTargetLabel);
        var candidateProcess = Attribute(candidate, "processName");
        var candidateScope = Attribute(candidate, "sourceScope");
        if (!string.IsNullOrWhiteSpace(signature.ProcessName)
            && !EqualsText(candidateProcess, signature.ProcessName)) return 0;
        if (!string.IsNullOrWhiteSpace(signature.SourceScope)
            && !EqualsText(candidateScope, signature.SourceScope)) return 0;
        if (string.Equals(signature.SourceScope, "browser_content", StringComparison.OrdinalIgnoreCase)
            && !MatchesBrowserSite(record.Context, context, signature, candidate)) return 0;
        if (targetLabelWasEdited && !EqualsText(candidate.Label, targetLabel)) return 0;
        if (!targetLabelWasEdited
            && !EqualsText(candidate.Label, targetLabel)
            && !EqualsText(Attribute(candidate, "automationId"), signature.AutomationId)) return 0;

        var score = 0;
        if (EqualsText(candidate.Label, targetLabel)) score += targetLabelWasEdited ? 600 : 220;
        if (!targetLabelWasEdited && EqualsText(Attribute(candidate, "automationId"), signature.AutomationId)) score += 360;
        if (EqualsText(candidateProcess, signature.ProcessName)) score += 140;
        if (EqualsText(candidateScope, signature.SourceScope)) score += 130;
        if (EqualsText(candidate.Role, signature.Role)) score += 50;
        if (EqualsText(Attribute(candidate, "className"), signature.ClassName)) score += 60;
        if (EqualsText(Attribute(candidate, "controlType"), signature.ControlType)) score += 60;
        if (EqualsText(Attribute(candidate, "containerLabel"), signature.ContainerLabel)) score += 50;
        if (EqualsText(context.ApplicationName, record.Context.ApplicationName)) score += 50;
        return score;
    }

    private static string? Attribute(UiCandidate candidate, string key) =>
        candidate.Attributes?.TryGetValue(key, out var value) == true ? Convert.ToString(value) : null;

    private static bool MatchesBrowserSite(
        ApplicationContext learnedContext,
        ApplicationContext currentContext,
        CorrectionTargetSignature signature,
        UiCandidate candidate)
    {
        if (Uri.TryCreate(learnedContext.Url, UriKind.Absolute, out var learnedUrl)
            && Uri.TryCreate(currentContext.Url, UriKind.Absolute, out var currentUrl))
            return string.Equals(learnedUrl.Host, currentUrl.Host, StringComparison.OrdinalIgnoreCase);

        var learnedSite = signature.ContainerLabel ?? learnedContext.WindowTitle;
        var currentSite = Attribute(candidate, "containerLabel") ?? currentContext.WindowTitle;
        if (string.IsNullOrWhiteSpace(learnedSite) || string.IsNullOrWhiteSpace(currentSite)) return true;
        var learnedTokens = SiteTokens(learnedSite);
        var currentTokens = SiteTokens(currentSite);
        return learnedTokens.Count == 0 || currentTokens.Count == 0
            || learnedTokens.Overlaps(currentTokens);
    }

    private static bool CompletionContextMatches(
        ApplicationContext learnedContext,
        ApplicationContext currentContext)
    {
        if (!string.Equals(
                learnedContext.ApplicationName,
                currentContext.ApplicationName,
                StringComparison.OrdinalIgnoreCase)) return false;

        if (!IsBrowserApplication(currentContext.ApplicationName)) return true;

        // A browser window title or a site-wide navigation label is not proof that
        // a web task is complete. Browser completion replay is allowed only when
        // both observations identify the same concrete URL path. This prevents a
        // home page accidentally marked "complete" from stopping every later run.
        if (!Uri.TryCreate(learnedContext.Url, UriKind.Absolute, out var learnedUrl)
            || !Uri.TryCreate(currentContext.Url, UriKind.Absolute, out var currentUrl)) return false;
        return string.Equals(learnedUrl.Host, currentUrl.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                learnedUrl.AbsolutePath.TrimEnd('/'),
                currentUrl.AbsolutePath.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBrowserApplication(string applicationName) =>
        applicationName.Contains("chrome", StringComparison.OrdinalIgnoreCase)
        || applicationName.Contains("msedge", StringComparison.OrdinalIgnoreCase)
        || applicationName.Equals("edge", StringComparison.OrdinalIgnoreCase)
        || applicationName.Contains("firefox", StringComparison.OrdinalIgnoreCase)
        || applicationName.Contains("brave", StringComparison.OrdinalIgnoreCase)
        || applicationName.Contains("opera", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> SiteTokens(string value) => value
        .ToLowerInvariant()
        .Split([' ', '-', '|', '—', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(token => token.Length >= 3 && token is not ("chrome" or "edge" or "firefox" or "www" or "com"))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool EqualsText(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

}

public static class DeveloperIntentMatcher
{
    public static string CreateIntentKey(string? value) => Normalize(value).Replace(' ', '_');

    public static bool IsSameIntent(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;

        var leftAction = ExtractAction(a);
        var rightAction = ExtractAction(b);
        if (leftAction is not null && rightAction is not null
            && !string.Equals(leftAction, rightAction, StringComparison.Ordinal)) return false;

        var leftConcepts = ExtractConcepts(a);
        var rightConcepts = ExtractConcepts(b);
        if (leftConcepts.Count == 0 || rightConcepts.Count == 0) return false;
        var overlap = leftConcepts.Intersect(rightConcepts, StringComparer.Ordinal).Count();
        var coverage = (double)overlap / Math.Min(leftConcepts.Count, rightConcepts.Count);
        return overlap > 0 && coverage >= 0.6;
    }

    private static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Normalize();
        foreach (var (source, target) in PhraseAliases)
            normalized = normalized.Replace(source, target, StringComparison.Ordinal);
        return string.Join(' ', normalized.Split(
            [' ', '\t', '\r', '\n', '?', '!', '.', ',', '/', '\\'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string? ExtractAction(string value)
    {
        if (ContainsAny(value, ["검색", "찾아", "찾기", "search"])) return "search";
        if (ContainsAny(value, ["삭제", "제거", "지워", "remove", "delete", "uninstall"])) return "remove";
        if (ContainsAny(value, ["확인", "상태", "됐는지", "작동", "check", "status"])) return "check";
        if (ContainsAny(value, ["추가", "등록", "연결", "add", "connect", "pair"])) return "add";
        if (ContainsAny(value, ["재생", "노래 틀", "음악 틀", "play"])) return "play";
        if (value.Contains("유튜브뮤직", StringComparison.Ordinal)
            && value.Contains("틀어", StringComparison.Ordinal)) return "open";
        if (ContainsAny(value, ["설정", "변경", "바꿔", "configure", "setting", "change"])) return "configure";
        if (ContainsAny(value, ["열어", "실행", "켜줘", "접속", "open", "launch"])) return "open";
        return null;
    }

    private static HashSet<string> ExtractConcepts(string value)
    {
        var concepts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = TrimKoreanParticle(raw);
            if (token.Length < 2 || StopWords.Contains(token) || ActionWords.Any(token.Contains)) continue;
            concepts.Add(token);
        }
        return concepts;
    }

    private static string TrimKoreanParticle(string token)
    {
        foreach (var suffix in KoreanParticles)
            if (token.Length > suffix.Length + 1 && token.EndsWith(suffix, StringComparison.Ordinal))
                return token[..^suffix.Length];
        return token;
    }

    private static bool ContainsAny(string value, string[] terms) => terms.Any(value.Contains);

    private static readonly (string Source, string Target)[] PhraseAliases =
    [
        ("youtube music", "유튜브뮤직"), ("유튜브 뮤직", "유튜브뮤직"),
        ("유튜브 음악", "유튜브뮤직"), ("음악", "노래"), ("music", "노래"), ("song", "노래"),
        ("인쇄 장치", "프린터"), ("인쇄장치", "프린터"), ("printer", "프린터"),
        ("와이 파이", "와이파이"), ("wi-fi", "와이파이"), ("wifi", "와이파이"),
        ("블루투스", "bluetooth"),
    ];

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "어디", "어디서", "어떻게", "해줘", "해주세요", "하고", "싶어", "싶어요", "보여줘", "알려줘",
        "where", "how", "please", "show", "the", "and", "with", "from", "크롬", "windows", "윈도우",
    };

    private static readonly string[] ActionWords =
    [
        "검색", "찾아", "찾기", "삭제", "제거", "지워", "추가", "등록", "연결", "재생", "틀어", "열어",
        "실행", "켜줘", "접속", "확인", "상태", "됐는지", "작동", "설정", "변경", "바꿔", "search",
        "remove", "delete", "add", "connect", "pair", "play", "open", "launch", "check", "status", "configure", "change",
    ];

    private static readonly string[] KoreanParticles =
    ["에서", "에게", "으로", "부터", "까지", "처럼", "하고", "이랑", "랑", "을", "를", "이", "가", "은", "는", "의", "에", "로"];
}

public static class DeveloperCommentRefiner
{
    public static RefinedDeveloperComment Refine(string? comment)
    {
        var raw = comment?.Trim() ?? string.Empty;
        var normalized = string.Join(' ', raw.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var lowered = normalized.ToLowerInvariant();
        var tags = new List<string>();
        AddTag(tags, lowered, "wrong_intent", ["뜻", "의도", "이해", "질문"]);
        AddTag(tags, lowered, "wrong_application", ["다른 앱", "앱이", "프로그램", "유튜브 뮤직"]);
        AddTag(tags, lowered, "wrong_scope", ["주소창", "검색창", "웹페이지", "브라우저 안", "앱 안"]);
        AddTag(tags, lowered, "wrong_target", ["다른 곳", "다른곳", "위치", "버튼", "아이콘"]);
        AddTag(tags, lowered, "overlay_missing", ["표시가 안", "오버레이", "안 보여", "안보여"]);
        AddTag(tags, lowered, "missing_detail", ["자세", "디테일", "단계", "설명 부족"]);
        if (normalized.Length > 0 && tags.Count == 0) tags.Add("developer_comment");
        return new RefinedDeveloperComment(raw, normalized, tags);
    }

    private static void AddTag(List<string> tags, string text, string tag, string[] terms)
    {
        if (terms.Any(text.Contains)) tags.Add(tag);
    }
}

public static class DeveloperCorrectionMatcher
{
    public static UiCandidate? FindSelectedCandidate(UiBounds selection, IReadOnlyList<UiCandidate> candidates)
    {
        var centerX = selection.X + selection.Width / 2;
        var centerY = selection.Y + selection.Height / 2;
        return candidates
            .Where(candidate => candidate.Visible && candidate.Enabled && candidate.Clickable)
            .Select(candidate => new
            {
                Candidate = candidate,
                ContainsCenter = Contains(candidate.Bounds, centerX, centerY),
                Overlap = IntersectionArea(candidate.Bounds, selection),
                Area = candidate.Bounds.Width * candidate.Bounds.Height,
            })
            .Where(item => item.ContainsCenter || item.Overlap > 0)
            .OrderByDescending(item => item.ContainsCenter)
            .ThenByDescending(item => item.Overlap / Math.Max(1, selection.Width * selection.Height))
            .ThenBy(item => item.Area)
            .Select(item => item.Candidate)
            .FirstOrDefault();
    }

    public static CorrectionTargetSignature CreateSignature(UiCandidate candidate) => new(
        candidate.Label,
        candidate.Description,
        candidate.Role,
        Attribute(candidate, "automationId"),
        Attribute(candidate, "className"),
        Attribute(candidate, "controlType"),
        Attribute(candidate, "processName"),
        Attribute(candidate, "sourceScope"),
        Attribute(candidate, "containerLabel"));

    public static VisualTarget NormalizeSelection(UiBounds screen, UiBounds selection, string label)
        => ScreenCoordinateMapper.NormalizeSelection(screen, selection, label);

    private static bool Contains(UiBounds bounds, double x, double y) =>
        x >= bounds.X && x <= bounds.X + bounds.Width
        && y >= bounds.Y && y <= bounds.Y + bounds.Height;

    private static double IntersectionArea(UiBounds left, UiBounds right)
    {
        var width = Math.Max(0, Math.Min(left.X + left.Width, right.X + right.Width) - Math.Max(left.X, right.X));
        var height = Math.Max(0, Math.Min(left.Y + left.Height, right.Y + right.Height) - Math.Max(left.Y, right.Y));
        return width * height;
    }

    private static string? Attribute(UiCandidate candidate, string key) =>
        candidate.Attributes?.TryGetValue(key, out var value) == true ? Convert.ToString(value) : null;
}

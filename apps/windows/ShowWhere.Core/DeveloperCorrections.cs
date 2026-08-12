using System.Text.Json;
using System.Text.Json.Serialization;

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

public sealed record RefinedDeveloperComment(
    string Raw,
    string Normalized,
    IReadOnlyList<string> IssueTags);

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
    private List<DeveloperCorrectionRecord> _records;
    private List<DeveloperCompletionRecord> _completions;

    public JsonlDeveloperCorrectionStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? ResolveDefaultDataDirectory();
        _recordsPath = Path.Combine(DataDirectory, "corrections.jsonl");
        _feedbackPath = Path.Combine(DataDirectory, "answer-feedback.jsonl");
        _completionsPath = Path.Combine(DataDirectory, "completions.jsonl");
        Directory.CreateDirectory(DataDirectory);
        _records = LoadRecords(_recordsPath);
        _completions = LoadCompletionRecords(_completionsPath);
    }

    public string DataDirectory { get; }

    public string ResolveIntent(string originalGoal)
    {
        if (string.IsNullOrWhiteSpace(originalGoal)) return originalGoal.Trim();
        lock (_gate)
        {
            return _records
                .Where(record => DeveloperIntentMatcher.IsSameIntent(originalGoal, record.OriginalGoal)
                    || DeveloperIntentMatcher.IsSameIntent(originalGoal, record.EffectiveGoal)
                    || DeveloperIntentMatcher.IsSameIntent(originalGoal, record.CorrectedIntent))
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
                .Where(record => record.CorrectTarget is not null)
                .Where(record => DeveloperIntentMatcher.IsSameIntent(goal, record.OriginalGoal)
                    || DeveloperIntentMatcher.IsSameIntent(goal, record.EffectiveGoal)
                    || DeveloperIntentMatcher.IsSameIntent(goal, record.CorrectedIntent))
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
                .Where(record => record.NormalizedVisualTarget is not null)
                .Where(record => DeveloperIntentMatcher.IsSameIntent(goal, record.OriginalGoal)
                    || DeveloperIntentMatcher.IsSameIntent(goal, record.EffectiveGoal)
                    || DeveloperIntentMatcher.IsSameIntent(goal, record.CorrectedIntent))
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
        var state = $"{context.ApplicationName} {context.WindowTitle} {context.Url}".ToLowerInvariant();
        lock (_gate)
        {
            var match = _completions
                .Where(record => DeveloperIntentMatcher.IsSameIntent(goal, record.OriginalGoal)
                    || DeveloperIntentMatcher.IsSameIntent(goal, record.EffectiveGoal))
                .Where(record => record.LearningLabels.ExpectedEvidence.Any(evidence =>
                    !string.IsNullOrWhiteSpace(evidence)
                    && state.Contains(evidence.Trim(), StringComparison.OrdinalIgnoreCase)))
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
        var saved = correction;
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
        var line = JsonSerializer.Serialize(feedback, JsonOptions) + Environment.NewLine;
        lock (_gate) File.AppendAllText(_feedbackPath, line);
        return Task.FromResult(feedback);
    }

    public Task<DeveloperCompletionRecord> SaveCompletionAsync(
        DeveloperCompletionRecord completion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(DataDirectory);
        var line = JsonSerializer.Serialize(completion, JsonOptions) + Environment.NewLine;
        lock (_gate)
        {
            File.AppendAllText(_completionsPath, line);
            _completions.Add(completion);
        }
        return Task.FromResult(completion);
    }

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

    private static string ResolveDefaultDataDirectory()
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

    private static int ScoreTarget(
        UiCandidate candidate,
        DeveloperCorrectionRecord record,
        ApplicationContext context)
    {
        var signature = record.CorrectTarget!;
        var candidateProcess = Attribute(candidate, "processName");
        var candidateScope = Attribute(candidate, "sourceScope");
        if (!string.IsNullOrWhiteSpace(signature.ProcessName)
            && !EqualsText(candidateProcess, signature.ProcessName)) return 0;
        if (!string.IsNullOrWhiteSpace(signature.SourceScope)
            && !EqualsText(candidateScope, signature.SourceScope)) return 0;
        if (!EqualsText(candidate.Label, signature.Label)
            && !EqualsText(Attribute(candidate, "automationId"), signature.AutomationId)) return 0;

        var score = 0;
        if (EqualsText(candidate.Label, signature.Label)) score += 220;
        if (EqualsText(Attribute(candidate, "automationId"), signature.AutomationId)) score += 360;
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
        return a == b;
    }

    private static string Normalize(string? value) => string.Join(' ',
        (value ?? string.Empty).Trim().ToLowerInvariant().Split(
            [' ', '\t', '\r', '\n', '?', '!', '.', ',', '/', '\\'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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
    {
        var width = Math.Clamp(selection.Width / screen.Width, 0, 1);
        var height = Math.Clamp(selection.Height / screen.Height, 0, 1);
        var x = Math.Clamp((selection.X - screen.X) / screen.Width, 0, 1 - width);
        var y = Math.Clamp((selection.Y - screen.Y) / screen.Height, 0, 1 - height);
        return new VisualTarget(x, y, width, height, label);
    }

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

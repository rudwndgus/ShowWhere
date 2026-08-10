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
    IReadOnlyList<string>? IssueTags = null);

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
    Task<DeveloperCorrectionRecord> SaveAsync(
        DeveloperCorrectionRecord correction,
        string? screenshotDataUrl,
        CancellationToken cancellationToken = default);
    Task<AnswerFeedbackRecord> SaveFeedbackAsync(
        AnswerFeedbackRecord feedback,
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
    private List<DeveloperCorrectionRecord> _records;

    public JsonlDeveloperCorrectionStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? ResolveDefaultDataDirectory();
        if (dataDirectory is null) MigrateLegacyTrainingData(DataDirectory);
        _recordsPath = Path.Combine(DataDirectory, "corrections.jsonl");
        _feedbackPath = Path.Combine(DataDirectory, "answer-feedback.jsonl");
        Directory.CreateDirectory(DataDirectory);
        _records = LoadRecords(_recordsPath);
    }

    public string DataDirectory { get; }

    public string ResolveIntent(string originalGoal)
    {
        var normalized = Normalize(originalGoal);
        if (normalized.Length == 0) return originalGoal.Trim();
        lock (_gate)
        {
            return _records
                .Where(record => Normalize(record.OriginalGoal) == normalized)
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
        var normalizedGoal = Normalize(goal);
        List<DeveloperCorrectionRecord> matchingRecords;
        lock (_gate)
        {
            matchingRecords = _records
                .Where(record => record.CorrectTarget is not null)
                .Where(record => Normalize(record.OriginalGoal) == normalizedGoal
                    || Normalize(record.EffectiveGoal) == normalizedGoal
                    || Normalize(record.CorrectedIntent) == normalizedGoal)
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

    private static void MigrateLegacyTrainingData(string destination)
    {
        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShowWhere",
            "training");
        if (!Directory.Exists(legacy)
            || string.Equals(
                Path.GetFullPath(legacy).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)) return;

        foreach (var sourcePath in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(legacy, sourcePath);
            var destinationPath = Path.Combine(destination, relativePath);
            if (File.Exists(destinationPath)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: false);
        }
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

    private static string Normalize(string? value) => string.Concat(
        (value ?? string.Empty).Trim().ToLowerInvariant().Where(character => !char.IsWhiteSpace(character)));
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

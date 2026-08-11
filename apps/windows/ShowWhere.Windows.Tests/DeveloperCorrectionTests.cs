using ShowWhere.Core;

namespace ShowWhere.Windows.Tests;

public sealed class DeveloperCorrectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ShowWhere.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Intent_correction_persists_across_store_instances()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        await store.SaveAsync(Record(correctedIntent: "유튜브 뮤직 웹페이지 안에서 음악 검색"), null);

        var reloaded = new JsonlDeveloperCorrectionStore(_directory);

        Assert.Equal(
            "유튜브 뮤직 웹페이지 안에서 음악 검색",
            reloaded.ResolveIntent("노래 찾아줘"));
    }

    [Fact]
    public async Task Verified_target_is_resolved_semantically_in_a_new_observation()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var signature = new CorrectionTargetSignature(
            "검색", null, "edit", "ytmusic-search", "SearchBox", "Edit", "chrome", "browser_content", "YouTube Music");
        await store.SaveAsync(Record(signature: signature), null);
        var candidates = new[]
        {
            Candidate("address", "검색", "address-bar", "browser_chrome"),
            Candidate("music", "검색", "ytmusic-search", "browser_content"),
        };

        var found = store.TryResolveTarget(
            "노래 찾아줘",
            new ApplicationContext(Platforms.Windows, "chrome", "YouTube Music"),
            candidates,
            out var target,
            out _);

        Assert.True(found);
        Assert.Equal("music", target.Id);
    }

    [Fact]
    public async Task Correction_never_substitutes_browser_chrome_for_a_missing_content_target()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var signature = new CorrectionTargetSignature(
            "검색", null, "edit", "ytmusic-search", "SearchBox", "Edit", "chrome", "browser_content", "YouTube Music");
        await store.SaveAsync(Record(signature: signature), null);

        var found = store.TryResolveTarget(
            "노래 찾아줘",
            new ApplicationContext(Platforms.Windows, "chrome", "YouTube Music"),
            [Candidate("address", "검색", "address-bar", "browser_chrome")],
            out _,
            out _);

        Assert.False(found);
    }

    [Fact]
    public async Task Every_O_X_or_completion_feedback_is_appended_to_the_permanent_dataset()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var correct = Feedback("correct", "좋은 답변");
        var incorrect = Feedback("incorrect", "잘못된 위치");
        var completed = Feedback("completed", "이 화면에서 목표 달성");

        await store.SaveFeedbackAsync(correct);
        await store.SaveFeedbackAsync(incorrect);
        await store.SaveFeedbackAsync(completed);

        var lines = await File.ReadAllLinesAsync(Path.Combine(_directory, "raw", "feedback-events.jsonl"));
        Assert.Equal(3, lines.Length);
        Assert.Contains("\"rating\":\"correct\"", lines[0]);
        Assert.Contains("\"rating\":\"incorrect\"", lines[1]);
        Assert.Contains("\"rating\":\"completed\"", lines[2]);
    }

    [Fact]
    public async Task Developer_completion_stops_on_the_same_semantic_screen_after_reload()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var candidates = new[]
        {
            Candidate("printers", "Printers & scanners", "PrintersScanners", "settings"),
            Candidate("add", "Add device", "AddDevice", "settings"),
            Candidate("queue", "Open print queue", "PrintQueue", "settings"),
        };
        await store.SaveCompletionAsync(Completion(candidates));

        var reloaded = new JsonlDeveloperCorrectionStore(_directory);
        var found = reloaded.TryResolveCompletion(
            "프린터 상태 어디서 봐?",
            new ApplicationContext(Platforms.Windows, "SystemSettings", "Settings"),
            "changed-snapshot",
            candidates,
            out var completion);

        Assert.True(found);
        Assert.Equal("windows.printer.done", completion.Id);
    }

    [Fact]
    public async Task Completion_does_not_match_a_similar_but_incomplete_screen()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var completedScreen = new[]
        {
            Candidate("printers", "Printers & scanners", "PrintersScanners", "settings"),
            Candidate("add", "Add device", "AddDevice", "settings"),
            Candidate("queue", "Open print queue", "PrintQueue", "settings"),
            Candidate("status", "Printer status", "PrinterStatus", "settings"),
        };
        await store.SaveCompletionAsync(Completion(completedScreen));
        var settingsHome = new[]
        {
            Candidate("system", "System", "System", "settings"),
            Candidate("devices", "Bluetooth & devices", "Devices", "settings"),
        };

        Assert.False(store.TryResolveCompletion(
            "프린터 상태 어디서 봐?",
            new ApplicationContext(Platforms.Windows, "SystemSettings", "Settings"),
            "other-snapshot",
            settingsHome,
            out _));
    }

    [Fact]
    public async Task Completion_memory_contains_semantics_but_no_screen_coordinates()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        await store.SaveCompletionAsync(Completion([
            Candidate("printers", "Printers & scanners", "PrintersScanners", "settings"),
        ]));

        var json = await File.ReadAllTextAsync(Path.Combine(_directory, "raw", "completion-events.jsonl"));
        Assert.Contains("PrintersScanners", json);
        Assert.DoesNotContain("targetBounds", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("selectedBounds", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Developer_comment_keeps_raw_text_and_adds_structured_issue_tags()
    {
        var refined = DeveloperCommentRefiner.Refine(
            "  크롬 주소창이 아니라   유튜브 뮤직 웹페이지 검색창을 표시해야 해  ");

        Assert.Equal("크롬 주소창이 아니라 유튜브 뮤직 웹페이지 검색창을 표시해야 해", refined.Normalized);
        Assert.Contains("wrong_scope", refined.IssueTags);
        Assert.Contains("wrong_application", refined.IssueTags);
    }

    [Fact]
    public void Drag_selection_prefers_the_small_clickable_control_at_its_center()
    {
        var candidates = new[]
        {
            Candidate("window", "설정", "window", "windows_window_overview", new UiBounds(0, 0, 800, 600)),
            Candidate("button", "프린터 및 스캐너", "printers", "settings", new UiBounds(300, 200, 180, 50)),
        };

        var selected = DeveloperCorrectionMatcher.FindSelectedCandidate(
            new UiBounds(325, 210, 100, 30),
            candidates);

        Assert.Equal("button", selected?.Id);
    }

    [Fact]
    public void Selection_is_normalized_against_the_full_virtual_screen()
    {
        var target = DeveloperCorrectionMatcher.NormalizeSelection(
            new UiBounds(-1920, 0, 3840, 1080),
            new UiBounds(0, 270, 960, 540),
            "target");

        Assert.Equal(0.5, target.X, 3);
        Assert.Equal(0.25, target.Y, 3);
        Assert.Equal(0.25, target.Width, 3);
        Assert.Equal(0.5, target.Height, 3);
    }

    [Fact]
    public void Saved_candidate_correction_creates_an_immediate_highlight_decision()
    {
        var candidate = Candidate("network", "Network", "SystemTrayIcon", "windows_taskbar");

        var decision = DeveloperCorrectionApplication.CreateImmediateDecision(
            candidate,
            null,
            "Network");

        Assert.NotNull(decision);
        Assert.Equal(GuideActions.Highlight, decision.Action);
        Assert.Equal("network", decision.TargetId);
        Assert.Equal(1, decision.Confidence);
    }

    [Fact]
    public void Saved_freeform_region_creates_an_immediate_visual_highlight_decision()
    {
        var visualTarget = new VisualTarget(0.2, 0.3, 0.1, 0.08, "프린터");

        var decision = DeveloperCorrectionApplication.CreateImmediateDecision(
            null,
            visualTarget,
            "프린터");

        Assert.NotNull(decision);
        Assert.Equal(GuideActions.HighlightVisual, decision.Action);
        Assert.Equal(visualTarget, decision.VisualTarget);
        Assert.Null(decision.TargetId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static DeveloperCorrectionRecord Record(
        string? correctedIntent = null,
        CorrectionTargetSignature? signature = null) => new(
        1,
        Guid.NewGuid().ToString("D"),
        DateTimeOffset.UtcNow,
        "노래 찾아줘",
        "노래 찾아줘",
        correctedIntent,
        new ApplicationContext(Platforms.Windows, "chrome", "YouTube Music"),
        "snapshot",
        GuideActions.Highlight,
        "wrong",
        "주소창",
        new UiBounds(10, 10, 100, 30),
        signature is null ? null : new UiBounds(100, 100, 200, 40),
        signature is null ? null : new VisualTarget(0.1, 0.1, 0.2, 0.05, "검색"),
        signature,
        null);

    private static UiCandidate Candidate(
        string id,
        string label,
        string automationId,
        string scope,
        UiBounds? bounds = null) => new(
        id,
        label,
        null,
        "edit",
        true,
        true,
        true,
        bounds ?? new UiBounds(100, 100, 200, 40),
        new Dictionary<string, object?>
        {
            ["automationId"] = automationId,
            ["className"] = "SearchBox",
            ["controlType"] = "Edit",
            ["processName"] = "chrome",
            ["sourceScope"] = scope,
            ["containerLabel"] = scope == "browser_content" ? "YouTube Music" : "Chrome",
        });

    private static AnswerFeedbackRecord Feedback(string rating, string answer) => new(
        1,
        Guid.NewGuid().ToString("D"),
        DateTimeOffset.UtcNow,
        rating,
        Guid.NewGuid().ToString("D"),
        answer,
        "노래 찾아줘",
        "유튜브 뮤직 안에서 노래 검색",
        new ApplicationContext(Platforms.Windows, "chrome", "YouTube Music"),
        "snapshot",
        GuideActions.Highlight,
        "target",
        "검색",
        new UiBounds(10, 10, 100, 30));

    private static DeveloperCompletionRecord Completion(IReadOnlyList<UiCandidate> candidates) => new(
        1,
        "windows.printer.done",
        DateTimeOffset.UtcNow,
        "프린터 상태 어디서 봐?",
        "프린터 상태 어디서 봐?",
        new ApplicationContext(Platforms.Windows, "SystemSettings", "Settings"),
        "completed-snapshot",
        ["설정 열기", "프린터 및 스캐너 열기"],
        DeveloperCompletionMatcher.CaptureEvidence(candidates),
        "프린터 상태 화면에 도달했어요.");
}

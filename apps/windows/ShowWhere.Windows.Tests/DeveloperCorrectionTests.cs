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
    public async Task Verified_target_is_reused_only_for_the_same_normalized_question()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var signature = DeveloperCorrectionMatcher.CreateSignature(
            Candidate("music", "YouTube Music", "ytmusic", "browser_content"));
        await store.SaveAsync(RecordForGoal("크롬에서 유튜브 뮤직 틀어줘", signature), null);

        var found = store.TryResolveTarget(
            "  크롬에서   유튜브 뮤직 틀어줘  ",
            new ApplicationContext(Platforms.Windows, "chrome", "새 탭 - Chrome"),
            [Candidate("new-music", "YouTube Music", "ytmusic", "browser_content")],
            out var target,
            out _);

        Assert.True(found);
        Assert.Equal("new-music", target.Id);
    }

    [Fact]
    public void Semantic_intent_matches_paraphrases_but_separates_different_actions()
    {
        Assert.True(DeveloperIntentMatcher.IsSameIntent(
            "크롬에서 유튜브 뮤직 틀어줘",
            "  크롬에서   유튜브 뮤직 틀어줘  "));
        Assert.True(DeveloperIntentMatcher.IsSameIntent(
            "크롬에서 유튜브 뮤직 틀어줘",
            "유튜브 뮤직을 열어줘"));
        Assert.False(DeveloperIntentMatcher.IsSameIntent(
            "유튜브 뮤직을 열어줘",
            "유튜브 뮤직에서 아이유 노래 찾아줘"));
        Assert.False(DeveloperIntentMatcher.IsSameIntent(
            "유튜브 뮤직을 열어줘",
            "유튜브 뮤직에서 아이유 노래 틀어줘"));
        Assert.True(DeveloperIntentMatcher.IsSameIntent(
            "프린터 연결됐는지 확인하고 싶어",
            "인쇄 장치 상태를 보여줘"));
        Assert.False(DeveloperIntentMatcher.IsSameIntent(
            "프린터 상태를 확인하고 싶어",
            "프린터를 추가해줘"));
        Assert.True(DeveloperIntentMatcher.IsSameIntent(
            "프린터 설정을 열어줘",
            "인쇄 장치 설정 어디야?"));
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
    public async Task Every_O_or_X_feedback_is_appended_to_the_permanent_dataset()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var correct = Feedback("correct", "좋은 답변");
        var incorrect = Feedback("incorrect", "잘못된 위치");

        await store.SaveFeedbackAsync(correct);
        await store.SaveFeedbackAsync(incorrect);

        var lines = await File.ReadAllLinesAsync(Path.Combine(_directory, "answer-feedback.jsonl"));
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"rating\":\"correct\"", lines[0]);
        Assert.Contains("\"rating\":\"incorrect\"", lines[1]);
    }

    [Fact]
    public async Task X_feedback_is_reloaded_and_removes_the_same_rejected_target()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var wrong = Feedback("incorrect", "주소창을 누르세요") with
        {
            OriginalGoal = "유튜브 뮤직에서 노래 검색해줘",
            EffectiveGoal = "유튜브 뮤직에서 노래 검색해줘",
            TargetId = "browser-address",
            TargetLabel = "검색",
        };
        await store.SaveFeedbackAsync(wrong);

        var reloaded = new JsonlDeveloperCorrectionStore(_directory);
        var filtered = reloaded.FilterRejectedCandidates(
            "유튜브 뮤직에서 음악 찾아줘",
            wrong.Context!,
            [Candidate("browser-address", "검색", "address", "browser_chrome"),
             Candidate("music-search", "검색", "music", "browser_content")]);

        Assert.DoesNotContain(filtered, candidate => candidate.Id == "browser-address");
        Assert.Contains(filtered, candidate => candidate.Id == "music-search");
    }

    [Fact]
    public async Task A_later_O_clears_the_same_target_from_negative_memory()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var incorrect = Feedback("incorrect", "wrong") with { TargetId = "target" };
        await store.SaveFeedbackAsync(incorrect);
        await store.SaveFeedbackAsync(incorrect with
        {
            Id = Guid.NewGuid().ToString("D"),
            CreatedAtUtc = incorrect.CreatedAtUtc.AddSeconds(1),
            Rating = "correct",
        });

        var reloaded = new JsonlDeveloperCorrectionStore(_directory);
        var filtered = reloaded.FilterRejectedCandidates(
            incorrect.OriginalGoal!, incorrect.Context!,
            [Candidate("target", "설정", "settings", "windows_start")]);
        Assert.Single(filtered);
    }

    [Fact]
    public async Task O_feedback_is_promoted_to_human_gold_and_replayed_without_ai_after_restart()
    {
        var originalStore = new JsonlDeveloperCorrectionStore(_directory);
        var feedback = Feedback("correct", "설정 버튼을 누르세요");
        var signature = DeveloperCorrectionMatcher.CreateSignature(
            Candidate("settings", "설정", "settings-button", "windows_start"));
        var gold = DeveloperPositiveFeedback.Create(feedback, signature);

        Assert.NotNull(gold);
        Assert.Contains("human_gold", gold.IssueTags!);
        Assert.Equal("human_gold", gold.LearningLabels?.Authority);
        Assert.Equal("correct_target", gold.LearningLabels?.OutcomeLabel);
        Assert.Contains("설정", gold.LearningLabels?.ExpectedEvidence ?? []);
        await originalStore.SaveFeedbackAsync(feedback);
        await originalStore.SaveAsync(gold, null);

        var reloadedStore = new JsonlDeveloperCorrectionStore(_directory);
        var found = reloadedStore.TryResolveTarget(
            feedback.OriginalGoal!,
            feedback.Context!,
            [Candidate("new-settings-id", "설정", "settings-button", "windows_start")],
            out var target,
            out var replayedGold);

        Assert.True(found);
        Assert.Equal("new-settings-id", target.Id);
        Assert.Equal(feedback.Id, replayedGold.FeedbackId);
    }

    [Fact]
    public async Task O_feedback_replays_a_semantically_equivalent_question_after_restart()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var feedback = Feedback("correct", "프린터 및 스캐너를 누르세요") with
        {
            OriginalGoal = "프린터 연결 상태를 확인하고 싶어",
            EffectiveGoal = "프린터 연결 상태 확인",
        };
        var signature = DeveloperCorrectionMatcher.CreateSignature(
            Candidate("printer", "프린터 및 스캐너", "printers", "settings"));
        await store.SaveAsync(DeveloperPositiveFeedback.Create(feedback, signature)!, null);

        var reloaded = new JsonlDeveloperCorrectionStore(_directory);
        Assert.True(reloaded.TryResolveTarget(
            "인쇄 장치 상태를 보여줘", feedback.Context!,
            [Candidate("new-printer", "프린터 및 스캐너", "printers", "settings")],
            out var target, out _));
        Assert.Equal("new-printer", target.Id);
    }

    [Fact]
    public void X_feedback_is_never_promoted_to_positive_memory()
    {
        var feedback = Feedback("incorrect", "잘못된 버튼");
        var signature = DeveloperCorrectionMatcher.CreateSignature(
            Candidate("wrong", "잘못된 버튼", "wrong-button", "browser_chrome"));

        Assert.Null(DeveloperPositiveFeedback.Create(feedback, signature));
    }

    [Fact]
    public async Task O_visual_feedback_is_replayed_only_on_the_same_verified_screen()
    {
        var feedback = Feedback("correct", "화면의 설정 아이콘을 누르세요") with
        {
            Action = GuideActions.HighlightVisual,
        };
        var visual = new VisualTarget(0.7, 0.2, 0.05, 0.05, "설정");
        var gold = DeveloperPositiveFeedback.Create(feedback, null, visual);
        Assert.NotNull(gold);
        var store = new JsonlDeveloperCorrectionStore(_directory);
        await store.SaveAsync(gold, null);

        Assert.True(store.TryResolveVisualTarget(
            feedback.OriginalGoal!, feedback.Context!, "snapshot", out var replay, out _));
        Assert.Equal(visual, replay);
        Assert.False(store.TryResolveVisualTarget(
            feedback.OriginalGoal!, feedback.Context!, "different-screen", out _, out _));
    }

    [Fact]
    public async Task Developer_completed_state_is_persisted_and_reused_for_the_same_intent()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var context = new ApplicationContext(Platforms.Windows, "chrome", "YouTube Music - Chrome");
        var labels = DeveloperLabeling.CreateCorrectionLabels(
            "크롬에서 유튜브 뮤직 틀어줘", context, "open:youtube_music",
            "task.open.youtube_music", null, "completed.youtube_music", "YouTube Music", "task_completed");
        await store.SaveCompletionAsync(new DeveloperCompletionRecord(
            1, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow,
            "크롬에서 유튜브 뮤직 틀어줘", "크롬에서 유튜브 뮤직 틀어줘",
            context, "snapshot", ["YouTube Music"], labels));

        var reloaded = new JsonlDeveloperCorrectionStore(_directory);
        Assert.True(reloaded.TryResolveCompletion(
            "크롬에서 유튜브 뮤직 틀어줘", context, [], out var completion));
        Assert.Equal("task_completed", completion.LearningLabels.OutcomeLabel);
        Assert.False(reloaded.TryResolveCompletion(
            "유튜브 뮤직에서 노래 검색해줘", context, [], out _));
        Assert.False(reloaded.TryResolveCompletion(
            "크롬에서 유튜브 뮤직 틀어줘",
            new ApplicationContext(Platforms.Windows, "chrome", "새 탭 - Chrome"),
            [], out _));
    }

    [Fact]
    public async Task Revoked_completion_is_not_reused_after_restart_and_can_be_restored()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var context = new ApplicationContext(Platforms.Windows, "chrome", "YouTube Music - Chrome");
        var feedback = Feedback("completed", "완료") with
        {
            OriginalGoal = "유튜브 뮤직 열어줘",
            EffectiveGoal = "유튜브 뮤직 열어줘",
            Context = context,
        };
        await store.SaveFeedbackAsync(feedback);
        var labels = DeveloperLabeling.CreateCorrectionLabels(
            feedback.OriginalGoal!, context, "youtube music", null, null,
            "completed.youtube_music", "YouTube Music", "task_completed");
        await store.SaveCompletionAsync(new DeveloperCompletionRecord(
            1, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow,
            feedback.OriginalGoal!, feedback.EffectiveGoal!, context, "snapshot",
            ["YouTube Music"], labels, true, feedback.Id));
        await store.SetFeedbackActiveAsync(feedback.Id, false, "잘못 누른 끝");

        var reloaded = new JsonlDeveloperCorrectionStore(_directory);
        Assert.False(reloaded.TryResolveCompletion(
            "유튜브 뮤직 열어줘", context, [], out _));
        Assert.False(Assert.Single(reloaded.GetHistory()).Active);

        await reloaded.SetFeedbackActiveAsync(feedback.Id, true, "복구");
        var restored = new JsonlDeveloperCorrectionStore(_directory);
        Assert.True(restored.TryResolveCompletion(
            "유튜브 뮤직 열어줘", context, [], out _));
        Assert.True(Assert.Single(restored.GetHistory()).Active);
    }

    [Fact]
    public async Task Revoking_O_or_X_removes_its_runtime_effect_without_deleting_history()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var positiveFeedback = Feedback("correct", "설정을 누르세요");
        await store.SaveFeedbackAsync(positiveFeedback);
        var signature = DeveloperCorrectionMatcher.CreateSignature(
            Candidate("settings", "설정", "settings", "windows_start"));
        await store.SaveAsync(DeveloperPositiveFeedback.Create(positiveFeedback, signature)!, null);
        await store.SetFeedbackActiveAsync(positiveFeedback.Id, false);

        var reloaded = new JsonlDeveloperCorrectionStore(_directory);
        Assert.False(reloaded.TryResolveTarget(
            positiveFeedback.OriginalGoal!, positiveFeedback.Context!,
            [Candidate("settings", "설정", "settings", "windows_start")], out _, out _));
        var history = Assert.Single(reloaded.GetHistory());
        Assert.Equal("correct", history.Rating);
        Assert.False(history.Active);
    }

    [Fact]
    public async Task Log_edit_is_append_only_reloaded_and_changes_positive_replay_immediately()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var feedback = Feedback("correct", "검색을 누르세요");
        await store.SaveFeedbackAsync(feedback);
        var signature = DeveloperCorrectionMatcher.CreateSignature(
            Candidate("music", "검색", "ytmusic-search", "browser_content"));
        await store.SaveAsync(DeveloperPositiveFeedback.Create(feedback, signature)!, null);

        await store.SaveHistoryEditAsync(new DeveloperLearningEditRecord(
            1, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow,
            feedback.Id, "correct", "유튜브 뮤직에서 음악 검색해줘",
            "상단 검색 버튼을 누르세요", "YouTube Music 검색", "의도를 더 구체적으로 수정"));

        var reloaded = new JsonlDeveloperCorrectionStore(_directory);
        var history = Assert.Single(reloaded.GetHistory());
        Assert.True(history.HasEdits);
        Assert.Equal("유튜브 뮤직에서 음악 검색해줘", history.Goal);
        Assert.Equal("상단 검색 버튼을 누르세요", history.Answer);
        Assert.Equal("YouTube Music 검색", history.TargetLabel);
        Assert.True(reloaded.TryResolveTarget(
            history.Goal, feedback.Context!,
            [Candidate("music-new", "검색", "ytmusic-search", "browser_content")], out _, out _));

        Assert.Single(File.ReadAllLines(Path.Combine(_directory, "learning-edits.jsonl")));
        Assert.Single(File.ReadAllLines(Path.Combine(_directory, "answer-feedback.jsonl")));
    }

    [Fact]
    public async Task Changing_log_rating_from_completed_stops_completion_replay()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var context = new ApplicationContext(Platforms.Windows, "chrome", "YouTube Music - Chrome");
        var feedback = Feedback("completed", "완료") with
        {
            OriginalGoal = "유튜브 뮤직 열어줘",
            EffectiveGoal = "유튜브 뮤직 열어줘",
            Context = context,
        };
        await store.SaveFeedbackAsync(feedback);
        var labels = DeveloperLabeling.CreateCorrectionLabels(
            feedback.OriginalGoal!, context, "youtube music", null, null,
            "completed.youtube_music", "YouTube Music", "task_completed");
        await store.SaveCompletionAsync(new DeveloperCompletionRecord(
            1, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow,
            feedback.OriginalGoal!, feedback.EffectiveGoal!, context, "snapshot",
            ["YouTube Music"], labels, true, feedback.Id));
        await store.SaveHistoryEditAsync(new DeveloperLearningEditRecord(
            1, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow,
            feedback.Id, "incorrect", feedback.OriginalGoal!,
            "아직 완료가 아님", "프로필", "끝을 잘못 눌렀음"));

        var reloaded = new JsonlDeveloperCorrectionStore(_directory);
        Assert.False(reloaded.TryResolveCompletion(feedback.OriginalGoal!, context, [], out _));
        Assert.Equal("incorrect", Assert.Single(reloaded.GetHistory()).Rating);
    }

    [Fact]
    public async Task Generic_settings_title_does_not_complete_a_task_without_specific_live_evidence()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var context = new ApplicationContext(Platforms.Windows, "SystemSettings", "설정");
        var labels = DeveloperLabeling.CreateCorrectionLabels(
            "프린터 설정 열어줘", context, "프린터 및 스캐너",
            null, null, "printers", "프린터 및 스캐너,장치 추가", "task_completed");
        await store.SaveCompletionAsync(new DeveloperCompletionRecord(
            1, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow,
            "프린터 설정 열어줘", "프린터 설정 열어줘", context, "snapshot",
            ["프린터 및 스캐너", "장치 추가"], labels));

        var reloaded = new JsonlDeveloperCorrectionStore(_directory);
        Assert.False(reloaded.TryResolveCompletion(
            "인쇄 장치 설정 보여줘", context,
            [Candidate("sound", "소리", "sound", "settings")], out _));
        Assert.True(reloaded.TryResolveCompletion(
            "인쇄 장치 설정 보여줘", context,
            [Candidate("printers", "프린터 및 스캐너", "printers", "settings")], out _));
    }

    [Fact]
    public void Completion_evidence_ignores_generic_window_chrome_and_keeps_specific_controls()
    {
        var evidence = DeveloperCompletionEvidence.Build(
            new ApplicationContext(Platforms.Windows, "SystemSettings", "설정"),
            [Candidate("close", "닫기", "close", "window_chrome"),
             Candidate("printers", "프린터 및 스캐너", "printers", "settings")]);
        Assert.DoesNotContain("설정", evidence);
        Assert.DoesNotContain("닫기", evidence);
        Assert.Contains("프린터 및 스캐너", evidence);
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
    public void Developer_labels_follow_brain_v2_task_state_target_and_evidence_contract()
    {
        var labels = DeveloperLabeling.CreateCorrectionLabels(
            "프린터를 추가해줘",
            new ApplicationContext(Platforms.Windows, "SystemSettings", "설정"),
            "프린터 및 스캐너",
            "Windows.Printer.Add",
            "Windows.Settings.Home",
            "Windows.Settings.Printers Scanners",
            "프린터 및 스캐너, 장치 추가",
            "wrong_target");

        Assert.Equal("windows.printer.add", labels.TaskId);
        Assert.Equal("windows.settings.home", labels.StateId);
        Assert.Equal("프린터.및.스캐너", labels.TargetConcept);
        Assert.Equal("windows.settings.printers.scanners", labels.ExpectedNextState);
        Assert.Equal(["프린터 및 스캐너", "장치 추가"], labels.ExpectedEvidence);
        Assert.Equal("human_gold", labels.Authority);
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

    private static DeveloperCorrectionRecord RecordForGoal(
        string goal,
        CorrectionTargetSignature signature) => Record(signature: signature) with
        {
            OriginalGoal = goal,
            EffectiveGoal = goal,
        };

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
}

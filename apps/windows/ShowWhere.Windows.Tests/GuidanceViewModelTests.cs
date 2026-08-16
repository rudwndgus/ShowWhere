using System.Reflection;
using System.Windows.Automation;
using ShowWhere.ApiClient;
using ShowWhere.Core;
using ShowWhere.Desktop;
using ShowWhere.Overlay;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class GuidanceViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ShowWhere.ViewModel.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Same_question_on_the_exact_same_screen_skips_the_second_screenshot_upload()
    {
        var observation = CreateObservation("same-screen");
        var api = new CountingGuideApiClient(new GuideDecision(
            GuideStatuses.NeedsClarification,
            GuideActions.AskUser,
            "어떤 작업을 원하시나요?",
            0.8));
        var viewModel = new GuidanceViewModel(
            new FixedObserver(observation),
            new NoChangeMonitor(),
            new CountingScreenCapture(),
            api,
            new RecordingOverlay(),
            new NoSelectionService(),
            new JsonlDeveloperCorrectionStore(_directory),
            () => { },
            delayAsync: (_, _) => Task.CompletedTask);

        viewModel.GoalText = "이 화면에서 도와줘";
        await SubmitAsync(viewModel);
        viewModel.GoalText = "이 화면에서 도와줘";
        await SubmitAsync(viewModel);

        Assert.Equal(1, api.CallCount);
        Assert.Equal("Saved answer applied instantly on the same screen", viewModel.StatusText);
        Assert.Equal("어떤 작업을 원하시나요?", viewModel.Messages[^1].Text);
    }

    [Fact]
    public async Task Changed_screen_does_not_reuse_a_safe_reply()
    {
        var observer = new SequenceObserver(CreateObservation("screen-one"), CreateObservation("screen-two"));
        var api = new CountingGuideApiClient(new GuideDecision(
            GuideStatuses.NeedsClarification,
            GuideActions.AskUser,
            "화면을 더 설명해 주세요.",
            0.7));
        var viewModel = new GuidanceViewModel(
            observer,
            new NoChangeMonitor(),
            new CountingScreenCapture(),
            api,
            new RecordingOverlay(),
            new NoSelectionService(),
            new JsonlDeveloperCorrectionStore(_directory),
            () => { });

        viewModel.GoalText = "도와줘";
        await SubmitAsync(viewModel);
        // Force the next submission to observe a genuinely changed screen instead
        // of offering the previous observation to the exact-screen cache.
        typeof(GuidanceViewModel).GetField("_lastObservation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, null);
        viewModel.GoalText = "도와줘";
        await SubmitAsync(viewModel);

        Assert.Equal(2, api.CallCount);
    }

    [Fact]
    public async Task Mobile_message_uses_the_same_observation_and_guide_pipeline_as_desktop_input()
    {
        var api = new CountingGuideApiClient(new GuideDecision(
            GuideStatuses.NeedsClarification,
            GuideActions.AskUser,
            "어느 프린터를 확인할까요?",
            0.75));
        var viewModel = new GuidanceViewModel(
            new FixedObserver(CreateObservation("remote-screen")),
            new NoChangeMonitor(),
            new CountingScreenCapture(),
            api,
            new RecordingOverlay(),
            new NoSelectionService(),
            new JsonlDeveloperCorrectionStore(_directory),
            () => { });

        await viewModel.SubmitRemoteAsync("프린터 설정 어디야?");

        Assert.Equal(1, api.CallCount);
        Assert.Contains(viewModel.Messages, item => item.Role == "user" && item.Text == "프린터 설정 어디야?");
        Assert.Equal("어느 프린터를 확인할까요?", viewModel.Messages[^1].Text);
    }

    [Fact]
    public async Task Bluu_Deli_demo_keeps_the_conversation_state_and_never_calls_the_AI()
    {
        var observation = CreateObservation("bluu-deli-demo");
        var demoDelays = new List<TimeSpan>();
        var api = new CountingGuideApiClient(new GuideDecision(
            GuideStatuses.NeedsClarification, GuideActions.AskUser, "AI should not be called", 0.1));
        var viewModel = new GuidanceViewModel(
            new FixedObserver(observation),
            new AlwaysChangeMonitor(observation),
            new CountingScreenCapture(),
            api,
            new RecordingOverlay(),
            new NoSelectionService(),
            new JsonlDeveloperCorrectionStore(_directory),
            () => { },
            delayAsync: (delay, _) =>
            {
                demoDelays.Add(delay);
                return Task.CompletedTask;
            });

        viewModel.GoalText = "where can I order latte and bacon cheese omlete";
        await SubmitAsync(viewModel);
        Assert.Contains("modifiers for your latte", viewModel.Messages[^1].Text, StringComparison.OrdinalIgnoreCase);

        viewModel.GoalText = "ice";
        await SubmitAsync(viewModel);
        Assert.Contains("modifiers for your bacon", viewModel.Messages[^1].Text, StringComparison.OrdinalIgnoreCase);

        viewModel.GoalText = "no thanks";
        await SubmitAsync(viewModel);
        Assert.Contains("tip", viewModel.Messages[^1].Text, StringComparison.OrdinalIgnoreCase);

        viewModel.GoalText = "no tip";
        await SubmitAsync(viewModel);
        Assert.Equal("Task complete", viewModel.StatusText);
        Assert.Contains("Guidance is complete", viewModel.Messages[^1].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, api.CallCount);
        Assert.NotEmpty(demoDelays);
        Assert.All(demoDelays, delay => Assert.Equal(TimeSpan.FromMilliseconds(1500), delay));
    }

    [Fact]
    public async Task Developer_visual_correction_is_saved_and_highlighted_immediately()
    {
        var observation = CreateObservation("correction-screen");
        var overlay = new RecordingOverlay();
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var api = new CountingGuideApiClient(new GuideDecision(
            GuideStatuses.InProgress,
            GuideActions.HighlightVisual,
            "기존 위치",
            0.8,
            VisualTarget: new VisualTarget(0.05, 0.05, 0.05, 0.05, "기존")));
        var viewModel = new GuidanceViewModel(
            new FixedObserver(observation),
            new NoChangeMonitor(),
            new CountingScreenCapture(),
            api,
            overlay,
            new NoSelectionService(),
            store,
            () => { });

        viewModel.GoalText = "화면의 올바른 위치";
        await SubmitAsync(viewModel);
        var answer = viewModel.Messages.Last(message => message.CanEvaluate);
        viewModel.ToggleDeveloperModeCommand.Execute(null);
        await InvokeAsync(viewModel, "MarkAnswerIncorrectAsync", answer);
        var correctedBounds = new UiBounds(400, 260, 120, 48);
        SetPrivate(viewModel, "_pendingCorrectionSelection", correctedBounds);
        SetPrivate(viewModel, "_pendingCorrectionCapture", new WindowsScreenCapture(
            "data:image/jpeg;base64,abc", new UiBounds(0, 0, 1920, 1080)));
        viewModel.SaveCorrectionScreenshot = false;
        viewModel.CorrectionCommentText = "이 위치가 올바른 대상";
        viewModel.CorrectionTargetConcept = "correct visual target";

        await InvokeAsync(viewModel, "SaveCorrectionAsync");

        Assert.Equal(correctedBounds, overlay.LastTarget);
        Assert.Contains("saved and applied", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.True(store.TryResolveVisualTarget(
            "화면의 올바른 위치",
            observation.Context,
            observation.SnapshotHash,
            out var storedTarget,
            out _));
        Assert.Equal(400d / 1920, storedTarget.X, 6);
        Assert.Equal(260d / 1080, storedTarget.Y, 6);
    }

    [Fact]
    public async Task Developer_O_uses_a_simple_confirmation_and_preserves_optional_comment()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var viewModel = new GuidanceViewModel(
            new FixedObserver(CreateObservation("positive-screen")),
            new NoChangeMonitor(),
            new CountingScreenCapture(),
            new CountingGuideApiClient(new GuideDecision(
                GuideStatuses.NeedsClarification, GuideActions.AskUser, "정확한 안내", 0.9)),
            new RecordingOverlay(),
            new NoSelectionService(),
            store,
            () => { });
        viewModel.GoalText = "도와줘";
        await SubmitAsync(viewModel);
        var answer = viewModel.Messages.Last(item => item.CanEvaluate);
        viewModel.ToggleDeveloperModeCommand.Execute(null);

        await InvokeAsync(viewModel, "MarkAnswerCorrectAsync", answer);

        Assert.True(viewModel.IsPositiveConfirmationVisible);
        Assert.False(File.Exists(Path.Combine(_directory, "answer-feedback.jsonl")));
        viewModel.PositiveCommentText = "이 답변은 현재 화면에서 정확함";
        await InvokeAsync(viewModel, "ConfirmPositiveAnswerAsync");
        Assert.False(viewModel.IsPositiveConfirmationVisible);
        Assert.Single(File.ReadAllLines(Path.Combine(_directory, "answer-feedback.jsonl")));
        using var edit = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_directory, "learning-edits.jsonl")));
        Assert.Contains("현재 화면에서 정확함", edit.RootElement.GetProperty("comment").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static async Task SubmitAsync(GuidanceViewModel viewModel)
    {
        await InvokeAsync(viewModel, "SubmitAsync");
    }

    private static async Task InvokeAsync(GuidanceViewModel viewModel, string methodName, params object?[] arguments)
    {
        var method = typeof(GuidanceViewModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(viewModel, arguments)!;
    }

    private static void SetPrivate(GuidanceViewModel viewModel, string fieldName, object value) =>
        typeof(GuidanceViewModel).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, value);

    private static WindowsObservation CreateObservation(string hash)
    {
        var context = new ApplicationContext(Platforms.Windows, "explorer", "Desktop");
        var registry = new CandidateRegistry(
            new Dictionary<string, AutomationElement>(),
            new Dictionary<string, UiCandidate>());
        return new WindowsObservation(context, [], registry, hash, [], null, false);
    }

    private sealed class FixedObserver(WindowsObservation observation) : IWindowsUiObserver
    {
        public Task<WindowsObservation> ObserveAsync(string? goal, CancellationToken cancellationToken) =>
            Task.FromResult(observation);
        public void RememberCurrentForegroundWindow() { }
    }

    private sealed class SequenceObserver(params WindowsObservation[] observations) : IWindowsUiObserver
    {
        private int _index;
        public Task<WindowsObservation> ObserveAsync(string? goal, CancellationToken cancellationToken) =>
            Task.FromResult(observations[Math.Min(_index++, observations.Length - 1)]);
        public void RememberCurrentForegroundWindow() { }
    }

    private sealed class CountingGuideApiClient(GuideDecision decision) : IGuideApiClient
    {
        public int CallCount { get; private set; }
        public Task<GuideDecision> DecideNextActionAsync(GuideRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(decision);
        }
    }

    private sealed class CountingScreenCapture : IWindowsScreenCaptureService
    {
        public Task<WindowsScreenCapture> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(
            new WindowsScreenCapture("data:image/jpeg;base64,abc", new UiBounds(0, 0, 1920, 1080)));
    }

    private sealed class NoChangeMonitor : IWindowsChangeMonitor
    {
        public Task<WindowsObservation?> WaitForTargetInteractionAsync(
            UiBounds targetBounds, string? goal, string? baselineSnapshotHash, TimeSpan maximumWait, CancellationToken cancellationToken) =>
            Task.FromResult<WindowsObservation?>(null);
    }

    private sealed class AlwaysChangeMonitor(WindowsObservation observation) : IWindowsChangeMonitor
    {
        public Task<WindowsObservation?> WaitForTargetInteractionAsync(
            UiBounds targetBounds, string? goal, string? baselineSnapshotHash, TimeSpan maximumWait, CancellationToken cancellationToken) =>
            Task.FromResult<WindowsObservation?>(observation);
    }

    private sealed class RecordingOverlay : IHighlightOverlay
    {
        public UiBounds? LastTarget { get; private set; }
        public void ShowTarget(UiBounds target, string message) => LastTarget = target;
        public void ShowScrollHint(UiBounds target, string message) { }
        public void Clear() { }
    }

    private sealed class NoSelectionService : ICorrectionSelectionService
    {
        public Task<UiBounds?> SelectRegionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UiBounds?>(null);
    }
}

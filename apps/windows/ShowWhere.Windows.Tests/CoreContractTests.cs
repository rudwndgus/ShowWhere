using ShowWhere.Core;

namespace ShowWhere.Windows.Tests;

public sealed class CoreContractTests
{
    [Fact]
    public void Target_activation_signal_accepts_only_one_touch_inside_the_selected_area()
    {
        var signal = new TargetActivationSignal();
        var selectedArea = new UiBounds(100, 200, 80, 60);

        signal.Record(140, 230);

        Assert.True(signal.TryConsumeInside(selectedArea));
        Assert.False(signal.TryConsumeInside(selectedArea));
        signal.Record(20, 30);
        Assert.False(signal.TryConsumeInside(selectedArea));
    }

    [Fact]
    public void Unknown_target_id_is_rejected()
    {
        var request = CreateRequest();
        var decision = new GuideDecision(
            GuideStatuses.InProgress,
            GuideActions.Highlight,
            "Select it.",
            0.9,
            "invented-target");

        Assert.Throws<ContractValidationException>(() => ContractValidator.ValidateDecision(decision, request));
    }

    [Fact]
    public void Low_confidence_highlight_becomes_clarification()
    {
        var request = CreateRequest();
        var decision = new GuideDecision(
            GuideStatuses.InProgress,
            GuideActions.Highlight,
            "Maybe select it.",
            0.2,
            "candidate-1");

        var validated = ContractValidator.ValidateDecision(decision, request);

        Assert.Equal(GuideActions.AskUser, validated.Action);
        Assert.Null(validated.TargetId);
    }

    [Fact]
    public void Unknown_alternative_target_id_is_rejected()
    {
        var request = CreateRequest();
        var decision = new GuideDecision(
            GuideStatuses.NeedsClarification,
            GuideActions.AskUser,
            "Which item?",
            0.4,
            AlternativeTargetIds: ["candidate-1", "invented-target"]);

        Assert.Throws<ContractValidationException>(() => ContractValidator.ValidateDecision(decision, request));
    }

    [Fact]
    public void Visual_target_requires_a_matching_screenshot_and_normalized_bounds()
    {
        var request = CreateRequest() with
        {
            Screenshot = "data:image/jpeg;base64,abc",
            ScreenshotBounds = new UiBounds(0, 0, 1920, 1080),
        };
        var decision = new GuideDecision(
            GuideStatuses.InProgress,
            GuideActions.HighlightVisual,
            "Select Settings.",
            0.9,
            VisualTarget: new VisualTarget(0.5, 0.2, 0.08, 0.06, "Settings"));

        Assert.Equal(GuideActions.HighlightVisual, ContractValidator.ValidateDecision(decision, request).Action);
        Assert.Throws<ContractValidationException>(() => ContractValidator.ValidateDecision(
            decision with { VisualTarget = new VisualTarget(0.98, 0.2, 0.08, 0.06, "Settings") },
            request));
        Assert.Throws<ContractValidationException>(() => ContractValidator.ValidateDecision(
            decision,
            CreateRequest()));
    }

    [Fact]
    public void Task_session_transitions_preserve_completed_steps()
    {
        var session = TaskSessionStateMachine.Create("Open settings", () => "session-1");
        session = TaskSessionStateMachine.ObservationStarted(session);
        session = TaskSessionStateMachine.AiRequested(session);
        session = TaskSessionStateMachine.GuidanceReady(session, "Select Settings", "Settings opens");
        session = TaskSessionStateMachine.StepCompleted(session, "Settings selected");

        Assert.Equal(TaskStatuses.Observing, session.Status);
        Assert.Contains("Select Settings", session.CompletedSteps);
        Assert.Contains("Settings selected", session.KnownFacts);
        Assert.Null(session.ExpectedChange);
    }

    [Fact]
    public void Complex_windows_screens_share_the_same_candidate_limit_as_the_backend()
    {
        var baseRequest = CreateRequest();
        var accepted = baseRequest with
        {
            Candidates = Enumerable.Range(0, 220)
                .Select(index => baseRequest.Candidates[0] with { Id = $"candidate-{index}" })
                .ToArray(),
        };
        ContractValidator.Validate(accepted);

        var rejected = accepted with
        {
            Candidates = Enumerable.Range(0, 251)
                .Select(index => baseRequest.Candidates[0] with { Id = $"candidate-{index}" })
                .ToArray(),
        };
        Assert.Throws<ContractValidationException>(() => ContractValidator.Validate(rejected));
    }

    internal static GuideRequest CreateRequest() => new(
        TaskSessionStateMachine.Create("Open settings", () => "session-1") with { Status = TaskStatuses.WaitingForAi },
        new ApplicationContext(Platforms.Windows, "notepad", "Untitled - Notepad", Locale: "en-US"),
        [new UiCandidate(
            "candidate-1",
            "Settings",
            null,
            "button",
            true,
            true,
            true,
            new UiBounds(10, 20, 100, 30))]);
}

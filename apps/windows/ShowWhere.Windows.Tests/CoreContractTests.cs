using ShowWhere.Core;

namespace ShowWhere.Windows.Tests;

public sealed class CoreContractTests
{
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

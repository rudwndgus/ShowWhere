using ShowWhere.Core;

namespace ShowWhere.Windows.Tests;

public sealed class BluuDeliDemoFlowTests
{
    [Theory]
    [InlineData("where can I order latte and bacon cheese omlete")]
    [InlineData("Where can I order a latte and a bacon & cheese omelette?")]
    public void Recognizes_the_English_demo_intent_and_prefers_live_controls(string question)
    {
        var flow = new BluuDeliDemoFlow();
        var latte = Candidate("latte-live", "LATTE");

        Assert.True(flow.TryStart(question));
        var decision = flow.Resolve([Candidate("coffee", "COFFEE"), latte]);

        Assert.Equal(GuideActions.Highlight, decision.Action);
        Assert.Equal(latte.Id, decision.TargetId);
        Assert.DoesNotContain("developer", decision.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correction", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Replies_in_the_same_language_as_the_demo_request()
    {
        var english = new BluuDeliDemoFlow();
        Assert.True(english.TryStart("where can I order latte and bacon cheese omlete"));
        Assert.Contains("latte", english.Resolve([]).Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("[가-힣]", english.Resolve([]).Message);

        var korean = new BluuDeliDemoFlow();
        Assert.True(korean.TryStart("라떼와 베이컨 치즈 오믈렛은 어디서 주문해?"));
        Assert.Contains("눌러", korean.Resolve([]).Message);
    }

    [Fact]
    public void Uses_polite_contextual_English_instead_of_terse_tap_commands()
    {
        var flow = new BluuDeliDemoFlow();
        Assert.True(flow.TryStart("where can I order latte and bacon cheese omlete"));
        Assert.Equal("To order a latte, please click here on LATTE.", flow.Resolve([]).Message);

        flow.TargetInteracted();
        flow.TryAcceptReply("no thanks");
        flow.TargetInteracted();
        var breakfast = flow.Resolve([]);

        Assert.Contains("omelette is in the Breakfast menu", breakfast.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("please click BREAKFAST", breakfast.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(breakfast.Message.StartsWith("Tap ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runs_the_complete_iced_latte_and_plain_omelette_demo_sequence()
    {
        var flow = new BluuDeliDemoFlow();
        Assert.True(flow.TryStart("where can I order latte and bacon cheese omlete"));

        AssertStep(flow, "LATTE", BluuDeliDemoStage.AskLatteModifier);
        Assert.Equal(GuideActions.AskUser, flow.Resolve([]).Action);
        Assert.True(flow.TryAcceptReply("ice"));
        AssertStep(flow, "ICE", BluuDeliDemoStage.AddLatteToCart);
        AssertStep(flow, "Add to Cart", BluuDeliDemoStage.SelectBreakfast);
        AssertStep(flow, "BREAKFAST", BluuDeliDemoStage.SelectBaconCheeseOmelette);
        AssertStep(flow, "BACON & CHEESE OMELETTE", BluuDeliDemoStage.AskOmeletteModifier);
        Assert.Equal(GuideActions.AskUser, flow.Resolve([]).Action);
        Assert.True(flow.TryAcceptReply("no thanks"));
        AssertStep(flow, "Add to Cart", BluuDeliDemoStage.SelectCredit);
        AssertStep(flow, "Credit", BluuDeliDemoStage.AskTip);
        Assert.Equal(GuideActions.AskUser, flow.Resolve([]).Action);
        Assert.True(flow.TryAcceptReply("no tip"));
        AssertStep(flow, "No Tip", BluuDeliDemoStage.SelectApplyAndTender);
        AssertStep(flow, "Apply & Tender", BluuDeliDemoStage.Completed);

        var completed = flow.Resolve([]);
        Assert.Equal(GuideStatuses.Completed, completed.Status);
        Assert.Equal(GuideActions.Explain, completed.Action);
    }

    [Fact]
    public void Supports_selecting_a_tip_or_going_directly_to_apply_and_tender()
    {
        var tipFlow = AdvanceToTipPrompt();
        Assert.True(tipFlow.TryAcceptReply("3% tip"));
        AssertStep(tipFlow, "tip amount", BluuDeliDemoStage.SelectApplyAndTender);

        var tenderFlow = AdvanceToTipPrompt();
        Assert.True(tenderFlow.TryAcceptReply("apply & tender"));
        Assert.Equal("Apply & Tender", tenderFlow.Resolve([]).VisualTarget?.Label);
    }

    [Fact]
    public void Fixed_demo_fallback_rectangles_are_normalized_and_safe()
    {
        var flow = new BluuDeliDemoFlow();
        Assert.True(flow.TryStart("where can I order latte and bacon cheese omlete"));

        var decision = flow.Resolve([]);

        Assert.Equal(GuideActions.HighlightVisual, decision.Action);
        Assert.NotNull(decision.VisualTarget);
        Assert.InRange(decision.VisualTarget.X, 0, 1);
        Assert.InRange(decision.VisualTarget.Y, 0, 1);
        Assert.InRange(decision.VisualTarget.X + decision.VisualTarget.Width, 0, 1);
        Assert.InRange(decision.VisualTarget.Y + decision.VisualTarget.Height, 0, 1);
    }

    [Fact]
    public void Ice_never_matches_the_unrelated_item_price_label()
    {
        var flow = new BluuDeliDemoFlow();
        flow.TryStart("where can I order latte and bacon cheese omlete");
        flow.TargetInteracted();
        flow.TryAcceptReply("ice");
        var ice = Candidate("ice-live", "ICE");

        var decision = flow.Resolve([Candidate("price", "Item Price"), ice]);

        Assert.Equal(ice.Id, decision.TargetId);
    }

    private static BluuDeliDemoFlow AdvanceToTipPrompt()
    {
        var flow = new BluuDeliDemoFlow();
        flow.TryStart("where can I order latte and bacon cheese omlete");
        flow.TargetInteracted();
        flow.TryAcceptReply("ice");
        flow.TargetInteracted();
        flow.TargetInteracted();
        flow.TargetInteracted();
        flow.TargetInteracted();
        flow.TryAcceptReply("no thanks");
        flow.TargetInteracted();
        flow.TargetInteracted();
        Assert.Equal(BluuDeliDemoStage.AskTip, flow.Stage);
        return flow;
    }

    private static void AssertStep(BluuDeliDemoFlow flow, string expectedLabel, BluuDeliDemoStage next)
    {
        var decision = flow.Resolve([]);
        Assert.Equal(GuideActions.HighlightVisual, decision.Action);
        Assert.Equal(expectedLabel, decision.VisualTarget?.Label);
        flow.TargetInteracted();
        Assert.Equal(next, flow.Stage);
    }

    private static UiCandidate Candidate(string id, string label) => new(
        id,
        label,
        null,
        "button",
        true,
        true,
        true,
        new UiBounds(10, 10, 100, 50));
}

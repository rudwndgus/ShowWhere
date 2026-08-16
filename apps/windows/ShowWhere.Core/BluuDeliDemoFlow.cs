using System.Text;

namespace ShowWhere.Core;

public enum BluuDeliDemoStage
{
    Inactive,
    SelectLatte,
    AskLatteModifier,
    SelectIce,
    AddLatteToCart,
    SelectBreakfast,
    SelectBaconCheeseOmelette,
    AskOmeletteModifier,
    AddOmeletteToCart,
    SelectCredit,
    AskTip,
    SelectTip,
    SelectNoTip,
    SelectApplyAndTender,
    Completed,
}

/// <summary>
/// Deterministic, offline-safe flow for the BLUU DELI kiosk demonstration.
/// Live UI Automation labels are preferred. The normalized rectangles are an
/// explicit fallback for the fixed 550 x 977 portrait demo layout.
/// </summary>
public sealed class BluuDeliDemoFlow
{
    private bool _respondInKorean;
    private sealed record TargetSpec(
        string Label,
        string[] Aliases,
        VisualTarget VisualFallback,
        string Message,
        BluuDeliDemoStage NextStage);

    public BluuDeliDemoStage Stage { get; private set; } = BluuDeliDemoStage.Inactive;
    public bool IsActive => Stage != BluuDeliDemoStage.Inactive;
    public bool IsAwaitingReply => Stage is BluuDeliDemoStage.AskLatteModifier
        or BluuDeliDemoStage.AskOmeletteModifier
        or BluuDeliDemoStage.AskTip;

    public bool TryStart(string message)
    {
        var normalized = Normalize(message);
        if ((!Contains(normalized, "latte") && !Contains(normalized, "라떼"))
            || (!Contains(normalized, "bacon") && !Contains(normalized, "베이컨"))
            || (!Contains(normalized, "cheese") && !Contains(normalized, "치즈"))
            || (!Contains(normalized, "omelet") && !Contains(normalized, "omlete")
                && !Contains(normalized, "오믈렛"))) return false;

        Stage = BluuDeliDemoStage.SelectLatte;
        _respondInKorean = UserLanguage.IsKorean(message);
        return true;
    }

    public bool TryAcceptReply(string message)
    {
        var normalized = Normalize(message);
        switch (Stage)
        {
            case BluuDeliDemoStage.AskLatteModifier when Contains(normalized, "ice")
                || Contains(normalized, "아이스") || Contains(normalized, "얼음"):
                Stage = BluuDeliDemoStage.SelectIce;
                return true;
            case BluuDeliDemoStage.AskLatteModifier when IsNoThanks(normalized):
                Stage = BluuDeliDemoStage.AddLatteToCart;
                return true;
            case BluuDeliDemoStage.AskOmeletteModifier when IsNoThanks(normalized):
                Stage = BluuDeliDemoStage.AddOmeletteToCart;
                return true;
            case BluuDeliDemoStage.AskTip when Contains(normalized, "apply") || Contains(normalized, "tender"):
                Stage = BluuDeliDemoStage.SelectApplyAndTender;
                return true;
            case BluuDeliDemoStage.AskTip when Contains(normalized, "no tip") || Contains(normalized, "without tip")
                || Contains(normalized, "팁 없음") || Contains(normalized, "팁 빼"):
                Stage = BluuDeliDemoStage.SelectNoTip;
                return true;
            case BluuDeliDemoStage.AskTip when Contains(normalized, "tip") || Contains(normalized, "팁") || normalized.Contains('%'):
                Stage = BluuDeliDemoStage.SelectTip;
                return true;
            default:
                return false;
        }
    }

    public GuideDecision Resolve(IReadOnlyList<UiCandidate> candidates)
    {
        if (Stage == BluuDeliDemoStage.AskLatteModifier)
            return Ask(Message("라떼에 추가할 것이 있나요? \"ice\" 또는 \"no thanks\"라고 말해 주세요.",
                "Would you like any modifiers for your latte? For an iced latte, say \"ice.\" If you do not want any extras, say \"no thanks.\""));
        if (Stage == BluuDeliDemoStage.AskOmeletteModifier)
            return Ask(Message("베이컨 치즈 오믈렛에 추가할 것이 있나요? 추가하지 않으려면 \"no thanks\"라고 말해 주세요.",
                "Would you like any modifiers for your bacon & cheese omelette? If you would like it as shown, please say \"no thanks.\""));
        if (Stage == BluuDeliDemoStage.AskTip)
            return Ask(Message("팁을 선택하거나 \"no tip\"이라고 말해 주세요. 준비되면 \"apply and tender\"라고 말해 주세요.",
                "Would you like to add a tip? Please choose a tip amount, or say \"no tip.\" Then I will guide you to Apply & Tender."));
        if (Stage == BluuDeliDemoStage.Completed)
            return new GuideDecision(
                GuideStatuses.Completed,
                GuideActions.Explain,
                Message("라떼와 베이컨 치즈 오믈렛 주문 안내가 완료됐습니다.",
                    "The latte and bacon & cheese omelette order is ready. Guidance is complete."),
                1);

        var spec = GetTargetSpec(Stage)
            ?? throw new InvalidOperationException($"Unsupported BLUU DELI demo stage: {Stage}");
        var liveTarget = FindLive(candidates, spec.Aliases);
        if (liveTarget is not null)
            return new GuideDecision(
                GuideStatuses.InProgress,
                GuideActions.Highlight,
                LocalizeStep(spec.Message),
                1,
                liveTarget.Id,
                $"{spec.Label} opens.");

        return new GuideDecision(
            GuideStatuses.InProgress,
            GuideActions.HighlightVisual,
            LocalizeStep(spec.Message),
            1,
            VisualTarget: spec.VisualFallback);
    }

    public void TargetInteracted()
    {
        var spec = GetTargetSpec(Stage);
        if (spec is not null) Stage = spec.NextStage;
    }

    public void Reset() => Stage = BluuDeliDemoStage.Inactive;

    private string Message(string korean, string english) => _respondInKorean ? korean : english;

    private string LocalizeStep(string english) => english switch
    {
        "To order a latte, please click here on LATTE." => Message("라떼를 주문하려면 여기 LATTE를 눌러주세요.", english),
        "Please click ICE here to make your latte iced." => Message("아이스 라떼로 변경하려면 여기 ICE를 눌러주세요.", english),
        "Great. Please click Add to Cart to add the latte to your order." => Message("좋아요. 라떼를 주문에 담으려면 Add to Cart를 눌러주세요.", english),
        "The bacon & cheese omelette is in the Breakfast menu. Could you please click BREAKFAST here first?" => Message("베이컨 치즈 오믈렛은 Breakfast 메뉴에 있어요. 먼저 여기 BREAKFAST를 눌러주시겠어요?", english),
        "Now, please click BACON & CHEESE OMELETTE here." => Message("이제 여기 BACON & CHEESE OMELETTE를 눌러주세요.", english),
        "Perfect. Please click Add to Cart to add the omelette without extra modifiers." => Message("좋아요. 추가 옵션 없이 오믈렛을 담으려면 Add to Cart를 눌러주세요.", english),
        "Please click Credit here to continue to payment." => Message("결제를 계속하려면 여기 Credit을 눌러주세요.", english),
        "Please select the tip amount you would like." => Message("원하는 팁 금액을 선택해주세요.", english),
        "If you do not want to add a tip, please click No Tip here." => Message("팁을 추가하지 않으려면 여기 No Tip을 눌러주세요.", english),
        "Finally, please click Apply & Tender to continue. This completes the guidance." => Message("마지막으로 Apply & Tender를 눌러주세요. 이 단계에서 안내가 완료됩니다.", english),
        "Tap LATTE." => Message("LATTE를 눌러주세요.", english),
        "Tap ICE to make the latte iced." => Message("라떼를 아이스로 만들려면 ICE를 눌러주세요.", english),
        "Tap Add to Cart for the latte." => Message("라떼를 담으려면 Add to Cart를 눌러주세요.", english),
        "Tap BREAKFAST on the left." => Message("왼쪽의 BREAKFAST를 눌러주세요.", english),
        "Tap BACON & CHEESE OMELETTE." => Message("BACON & CHEESE OMELETTE를 눌러주세요.", english),
        "Tap Add to Cart without adding an omelette modifier." => Message("추가 옵션 없이 Add to Cart를 눌러주세요.", english),
        "Tap Credit to continue to payment." => Message("결제를 계속하려면 Credit을 눌러주세요.", english),
        "Tap the tip amount you want." => Message("원하는 팁 금액을 눌러주세요.", english),
        "Tap No Tip." => Message("No Tip을 눌러주세요.", english),
        "Tap Apply & Tender. This is the final step." => Message("Apply & Tender를 눌러주세요. 마지막 단계입니다.", english),
        _ => english,
    };

    private static TargetSpec? GetTargetSpec(BluuDeliDemoStage stage) => stage switch
    {
        BluuDeliDemoStage.SelectLatte => new(
            "LATTE", ["LATTE"], Box(403, 314, 146, 145, "LATTE"),
            "To order a latte, please click here on LATTE.", BluuDeliDemoStage.AskLatteModifier),
        BluuDeliDemoStage.SelectIce => new(
            "ICE", ["ICE", "ICED"], Box(10, 586, 132, 141, "ICE"),
            "Please click ICE here to make your latte iced.", BluuDeliDemoStage.AddLatteToCart),
        BluuDeliDemoStage.AddLatteToCart => new(
            "Add to Cart", ["ADD TO CART", "ADD CART"], Box(462, 906, 88, 61, "Add to Cart"),
            "Great. Please click Add to Cart to add the latte to your order.", BluuDeliDemoStage.SelectBreakfast),
        BluuDeliDemoStage.SelectBreakfast => new(
            "BREAKFAST", ["BREAKFAST", "BFEAKFAST"], Box(0, 223, 102, 59, "BREAKFAST"),
            "The bacon & cheese omelette is in the Breakfast menu. Could you please click BREAKFAST here first?", BluuDeliDemoStage.SelectBaconCheeseOmelette),
        BluuDeliDemoStage.SelectBaconCheeseOmelette => new(
            "BACON & CHEESE OMELETTE",
            ["BACON & CHEESE OMELETTE", "BACON AND CHEESE OMELETTE", "BACON & CHEESE OMLETTE"],
            Box(109, 314, 146, 146, "BACON & CHEESE OMELETTE"),
            "Now, please click BACON & CHEESE OMELETTE here.", BluuDeliDemoStage.AskOmeletteModifier),
        BluuDeliDemoStage.AddOmeletteToCart => new(
            "Add to Cart", ["ADD TO CART", "ADD CART"], Box(462, 906, 88, 61, "Add to Cart"),
            "Perfect. Please click Add to Cart to add the omelette without extra modifiers.", BluuDeliDemoStage.SelectCredit),
        BluuDeliDemoStage.SelectCredit => new(
            "Credit", ["CREDIT"], Box(426, 914, 62, 63, "Credit"),
            "Please click Credit here to continue to payment.", BluuDeliDemoStage.AskTip),
        BluuDeliDemoStage.SelectTip => new(
            "tip amount", ["3%", "TIP"], Box(94, 186, 164, 126, "tip amount"),
            "Please select the tip amount you would like.", BluuDeliDemoStage.SelectApplyAndTender),
        BluuDeliDemoStage.SelectNoTip => new(
            "No Tip", ["NO TIP"], Box(184, 581, 176, 64, "No Tip"),
            "If you do not want to add a tip, please click No Tip here.", BluuDeliDemoStage.SelectApplyAndTender),
        BluuDeliDemoStage.SelectApplyAndTender => new(
            "Apply & Tender", ["APPLY & TENDER", "APPLY AND TENDER"], Box(293, 830, 206, 64, "Apply & Tender"),
            "Finally, please click Apply & Tender to continue. This completes the guidance.", BluuDeliDemoStage.Completed),
        _ => null,
    };

    private static GuideDecision Ask(string message) => new(
        GuideStatuses.NeedsClarification,
        GuideActions.AskUser,
        message,
        1);

    private static UiCandidate? FindLive(IEnumerable<UiCandidate> candidates, IEnumerable<string> aliases)
    {
        var normalizedAliases = aliases.Select(Normalize).ToArray();
        return candidates
            .Where(candidate => candidate.Visible && candidate.Enabled && candidate.Clickable)
            .OrderBy(candidate => candidate.Bounds.Y)
            .ThenBy(candidate => candidate.Bounds.X)
            .FirstOrDefault(candidate =>
            {
                var label = Normalize($"{candidate.Label} {candidate.Description}");
                return normalizedAliases.Any(alias => MatchesLabel(label, alias));
            });
    }

    private static bool MatchesLabel(string label, string alias) => label == alias
        || $" {label} ".Contains($" {alias} ", StringComparison.Ordinal);

    private static bool IsNoThanks(string normalized) => Contains(normalized, "no thanks")
        || Contains(normalized, "nothing")
        || Contains(normalized, "none")
        || Contains(normalized, "괜찮")
        || Contains(normalized, "추가 안")
        || Contains(normalized, "없어");

    private static bool Contains(string normalized, string value) =>
        normalized.Contains(Normalize(value), StringComparison.Ordinal);

    private static string Normalize(string? value)
    {
        var builder = new StringBuilder();
        var previousSpace = true;
        foreach (var character in (value ?? string.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character == '%' || character == '&')
            {
                builder.Append(character);
                previousSpace = false;
            }
            else if (!previousSpace)
            {
                builder.Append(' ');
                previousSpace = true;
            }
        }
        return builder.ToString().Trim();
    }

    private static VisualTarget Box(double x, double y, double width, double height, string label) =>
        new(x / 550d, y / 977d, width / 550d, height / 977d, label);
}

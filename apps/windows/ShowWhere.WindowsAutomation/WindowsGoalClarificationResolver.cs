namespace ShowWhere.WindowsAutomation;

public sealed record GoalClarificationChoice(string Label, string ResolvedGoal);

public sealed record GoalClarificationPrompt(
    string Message,
    IReadOnlyList<GoalClarificationChoice> Choices);

public static class WindowsGoalClarificationResolver
{
    private static readonly string[] GenericPhotoTerms =
        ["사진", "photo", "photos", "picture", "pictures"];

    private static readonly string[] ExplicitPhotoSourceTerms =
    [
        "카메라", "찍은 사진", "camera", "webcam",
        "스크린샷", "화면 캡처", "화면캡처", "screenshot", "screen capture",
        "다운로드", "download", "문서", "document",
        "사진 폴더", "pictures folder", "photos folder",
    ];

    public static bool TryCreate(string goal, out GoalClarificationPrompt prompt)
    {
        prompt = null!;
        if (string.IsNullOrWhiteSpace(goal)) return false;

        var normalized = goal.Trim().ToLowerInvariant();
        if (!GenericPhotoTerms.Any(normalized.Contains)
            || ExplicitPhotoSourceTerms.Any(normalized.Contains)) return false;

        prompt = new GoalClarificationPrompt(
            "어떤 사진을 찾고 계신가요? 하나를 선택하면 바로 다음 위치를 알려드릴게요.",
            [
                new GoalClarificationChoice("카메라로 찍은 사진", "컴퓨터 카메라로 찍은 사진을 보고 싶어"),
                new GoalClarificationChoice("화면 캡처 · 스크린샷", "컴퓨터 화면을 캡처한 스크린샷을 보고 싶어"),
                new GoalClarificationChoice("일반 사진 폴더", "Windows 사진 폴더에 저장된 일반 사진을 보고 싶어"),
            ]);
        return true;
    }
}

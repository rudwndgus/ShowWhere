using ShowWhere.Core;

namespace ShowWhere.WindowsAutomation;

public static class GoalCompletionResolver
{
    private static readonly IReadOnlyDictionary<string, string[]> EvidenceByIntent =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["open:youtube_music"] = ["youtube music", "유튜브 뮤직"],
            ["open:youtube"] = ["youtube", "유튜브"],
            ["open:chrome"] = ["chrome", "크롬"],
            ["open:settings"] = ["systemsettings", "windows 설정", "설정"],
            ["open:calculator"] = ["calculator", "계산기"],
            ["open:camera"] = ["camera", "카메라"],
        };

    public static bool TryResolve(
        string goal,
        ApplicationContext context,
        IReadOnlyList<UiCandidate> candidates,
        out string message)
    {
        message = string.Empty;
        var intent = DeveloperIntentMatcher.CreateIntentKey(goal);
        if (!EvidenceByIntent.TryGetValue(intent, out var evidence)) return false;
        var currentState = string.Join(' ', new[]
        {
            context.ApplicationName,
            context.WindowTitle,
            context.Url,
        }.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();
        if (!evidence.Any(currentState.Contains)) return false;

        var destination = evidence[0] switch
        {
            "youtube music" => "YouTube Music",
            "youtube" => "YouTube",
            "chrome" => "Chrome",
            "systemsettings" => "Windows 설정",
            "calculator" => "계산기",
            "camera" => "카메라",
            _ => evidence[0],
        };
        message = $"완료됐어요. 요청하신 {destination} 화면이 열렸습니다.";
        return true;
    }
}

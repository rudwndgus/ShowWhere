using ShowWhere.Core;

namespace ShowWhere.WindowsAutomation;

public static class WindowsCandidatePrioritizer
{
    private sealed record IntentTerms(string[] GoalTerms, string[] CandidateTerms);

    private static readonly IntentTerms[] SystemIntents =
    [
        new(["인터넷", "네트워크", "와이파이", "wifi", "wi-fi", "network", "internet"],
            ["network", "internet", "wi-fi", "wifi", "ethernet"]),
        new(["소리", "볼륨", "음량", "스피커", "volume", "sound", "speaker"],
            ["volume", "sound", "speaker", "audio"]),
        new(["시간", "날짜", "시계", "time", "date", "clock"],
            ["clock", "time", "date", "calendar"]),
        new(["블루투스", "bluetooth"], ["bluetooth"]),
        new(["배터리", "전원", "battery", "power"], ["battery", "power"]),
        new(["알림", "notification"], ["notification", "action center"]),
    ];

    public static IReadOnlyList<UiCandidate> Prioritize(
        string goal,
        IReadOnlyList<UiCandidate> candidates,
        int maximumSystemCandidates = 40)
    {
        var normalizedGoal = goal.ToLowerInvariant();
        var eligibleCandidates = WindowsSettingsCatalog.IsSettingsGoal(normalizedGoal)
            ? candidates.Where(candidate => !WindowsWindowChromeFilter.IsCaptionControl(candidate)).ToArray()
            : candidates;
        var intent = SystemIntents.FirstOrDefault(group =>
            group.GoalTerms.Any(normalizedGoal.Contains));
        if (intent is null) return eligibleCandidates;

        return eligibleCandidates
            .Select((candidate, index) => new
            {
                Candidate = candidate,
                Index = index,
                Score = Score(candidate, intent),
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(Math.Max(1, maximumSystemCandidates))
            .Select(item => item.Candidate)
            .ToArray();
    }

    private static int Score(UiCandidate candidate, IntentTerms intent)
    {
        var searchable = $"{candidate.Label} {candidate.Description}".ToLowerInvariant();
        var score = intent.CandidateTerms.Count(searchable.Contains) * 100;
        if (candidate.Attributes?.TryGetValue("sourceScope", out var sourceScope) == true
            && string.Equals(Convert.ToString(sourceScope), "windows_taskbar", StringComparison.Ordinal))
            score += 40;
        else if (candidate.Attributes?.TryGetValue("processName", out var processName) == true
            && string.Equals(Convert.ToString(processName), "explorer", StringComparison.OrdinalIgnoreCase))
            score += 20;
        if (candidate.Role is "button" or "menuitem") score += 5;
        return score;
    }
}

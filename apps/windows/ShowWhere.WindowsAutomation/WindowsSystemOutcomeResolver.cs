using System.Text.RegularExpressions;
using ShowWhere.Core;

namespace ShowWhere.WindowsAutomation;

public static partial class WindowsSystemOutcomeResolver
{
    private static readonly string[] InternetTerms =
        ["인터넷", "네트워크", "와이파이", "wifi", "wi-fi", "network", "internet"];
    private static readonly string[] CheckTerms =
        ["상태", "확인", "알고", "check", "status"];
    private static readonly string[] DisconnectedTerms =
        ["no internet", "not connected", "disconnected", "연결 안", "인터넷 없음"];
    private static readonly string[] ConnectedTerms =
        ["internet access", "connected", "인터넷 액세스", "연결됨"];

    public static bool TryResolve(string goal, UiCandidate selectedCandidate, out string message)
    {
        message = string.Empty;
        var normalizedGoal = goal.ToLowerInvariant();
        if (!InternetTerms.Any(normalizedGoal.Contains) || !CheckTerms.Any(normalizedGoal.Contains))
            return false;

        var label = $"{selectedCandidate.Label} {selectedCandidate.Description}".ToLowerInvariant();
        var isKorean = KoreanText().IsMatch(goal);
        if (DisconnectedTerms.Any(label.Contains))
        {
            message = isKorean
                ? "빠른 설정이 열렸어요. 현재 Windows에는 인터넷 연결이 없는 상태로 표시됩니다."
                : "Quick Settings is open. Windows currently reports no internet connection.";
            return true;
        }
        if (ConnectedTerms.Any(label.Contains))
        {
            message = isKorean
                ? "빠른 설정이 열렸어요. 현재 Windows에는 인터넷이 연결된 상태로 표시됩니다."
                : "Quick Settings is open. Windows currently reports an active internet connection.";
            return true;
        }

        return false;
    }

    [GeneratedRegex("[가-힣]")]
    private static partial Regex KoreanText();
}

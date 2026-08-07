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
    private static readonly string[] PrinterTerms =
        ["프린터", "프린트", "printer", "printers", "printing"];
    private static readonly string[] PrinterSettingsTerms =
        ["printers & scanners", "printers and scanners", "프린터 및 스캐너", "프린터와 스캐너"];
    private static readonly (string[] GoalTerms, string[] CandidateTerms, string KoreanCompletion)[] BuiltInApps =
    [
        (["계산기", "calculator", "calc"], ["calculator", "계산기"], "계산기를 열었어요."),
        (["메모장", "notepad"], ["notepad", "메모장"], "메모장을 열었어요."),
        (["그림판", "paint", "mspaint"], ["paint", "그림판"], "그림판을 열었어요."),
        (["캡처 도구", "snipping tool"], ["snipping tool", "캡처 도구"], "캡처 도구를 열었어요."),
    ];

    public static bool TryResolve(string goal, UiCandidate selectedCandidate, out string message)
    {
        message = string.Empty;
        var normalizedGoal = goal.ToLowerInvariant();
        var label = $"{selectedCandidate.Label} {selectedCandidate.Description}".ToLowerInvariant();
        var isKorean = KoreanText().IsMatch(goal);
        var openedApp = BuiltInApps.FirstOrDefault(app =>
            app.GoalTerms.Any(normalizedGoal.Contains)
            && app.CandidateTerms.Any(label.Contains));
        if (openedApp.GoalTerms is not null)
        {
            message = isKorean
                ? $"잘하셨어요! {openedApp.KoreanCompletion}"
                : $"Great! {selectedCandidate.Label ?? "the app"} is open.";
            return true;
        }
        if (PrinterTerms.Any(normalizedGoal.Contains) && PrinterSettingsTerms.Any(label.Contains))
        {
            message = isKorean
                ? "잘하셨어요! 프린터 및 스캐너 화면을 열었어요. 여기에서 연결된 프린터 상태를 확인할 수 있어요."
                : "Printers & scanners is open. You can check connected printer status here.";
            return true;
        }
        if (!InternetTerms.Any(normalizedGoal.Contains) || !CheckTerms.Any(normalizedGoal.Contains))
            return false;

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

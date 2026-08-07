using System.Text.RegularExpressions;
using ShowWhere.Core;

namespace ShowWhere.WindowsAutomation;

public static partial class WindowsFastPathResolver
{
    private sealed record Route(
        string[] GoalTerms,
        string[][] CandidateSteps,
        string[]? ExcludedCandidateTerms = null);

    private static readonly HashSet<string> TrustedWindowsProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "SystemSettings",
        "StartMenuExperienceHost",
        "SearchHost",
        "ShellExperienceHost",
        "ShellHost",
        "ApplicationFrameHost",
        "Photos",
        "Microsoft.Photos",
        "CalculatorApp",
        "WindowsCalculator",
        "Notepad",
        "mspaint",
        "SnippingTool",
        "HxOutlook",
        "olk",
    };

    private static readonly Route[] Routes =
    [
        new(
            ["계산기", "calculator", "calc"],
            [
                ["calculator", "계산기"],
                ["start", "시작"],
            ]),
        new(
            ["메모장", "notepad"],
            [
                ["notepad", "메모장"],
                ["start", "시작"],
            ]),
        new(
            ["그림판", "paint", "mspaint"],
            [
                ["paint", "그림판"],
                ["start", "시작"],
            ]),
        new(
            ["캡처 도구", "snipping tool"],
            [
                ["snipping tool", "캡처 도구"],
                ["start", "시작"],
            ]),
        new(
            ["카메라로 찍", "카메라 사진", "카메라 롤", "찍은 사진", "camera photo", "camera roll", "webcam photo"],
            [
                ["camera roll", "카메라 롤"],
                ["pictures", "picture", "사진"],
                ["photos", "photo", "사진"],
                ["file explorer", "파일 탐색기"],
                ["start", "시작"],
            ],
            ["screenshots", "screenshot", "스크린샷"]),
        new(
            ["스크린샷", "화면 캡처", "화면캡처", "screenshot", "screen capture"],
            [
                ["screenshots", "screenshot", "스크린샷"],
                ["pictures", "picture", "사진"],
                ["photos", "photo", "사진"],
                ["file explorer", "파일 탐색기"],
                ["start", "시작"],
            ],
            ["camera roll", "카메라 롤"]),
        new(
            ["사진 폴더", "pictures folder", "photos folder"],
            [
                ["pictures", "picture", "사진"],
                ["photos", "photo", "사진"],
                ["file explorer", "파일 탐색기"],
                ["start", "시작"],
            ]),
        new(
            ["다운로드", "download", "downloads", "받은 파일"],
            [
                ["downloads", "download", "다운로드"],
                ["file explorer", "파일 탐색기"],
                ["start", "시작"],
            ]),
        new(
            ["문서 폴더", "내 문서", "documents folder", "my documents"],
            [
                ["documents", "문서"],
                ["file explorer", "파일 탐색기"],
                ["start", "시작"],
            ]),
        new(
            ["프린터", "프린트", "printer", "printers", "printing"],
            [
                ["printers & scanners", "printers and scanners", "프린터 및 스캐너", "프린터와 스캐너"],
                ["bluetooth & devices", "bluetooth and devices", "블루투스 및 장치"],
                ["settings", "설정"],
                ["start", "시작"],
            ],
            ["network", "internet", "wi-fi", "wifi", "ethernet", "네트워크", "인터넷"]),
        new(
            ["기본 앱", "default apps", "default app"],
            [
                ["default apps", "기본 앱"],
                ["apps", "앱"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["설치된 앱", "앱 제거", "프로그램 제거", "installed apps", "uninstall app", "uninstall program"],
            [
                ["installed apps", "설치된 앱"],
                ["apps", "앱"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["계정 설정", "사용자 계정", "로그인 옵션", "accounts", "sign-in options"],
            [
                ["sign-in options", "로그인 옵션"],
                ["accounts", "계정"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["개인 설정", "배경 화면", "바탕 화면 배경", "테마", "잠금 화면", "personalization", "background", "themes", "lock screen"],
            [
                ["background", "배경"],
                ["themes", "테마"],
                ["lock screen", "잠금 화면"],
                ["personalization", "개인 설정"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["저장 공간", "디스크 공간", "storage", "disk space"],
            [
                ["storage", "저장소", "저장 공간"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["마우스 설정", "터치패드", "키보드 설정", "mouse settings", "touchpad", "keyboard settings"],
            [
                ["mouse", "마우스"],
                ["touchpad", "터치패드"],
                ["typing", "입력"],
                ["bluetooth & devices", "bluetooth and devices", "블루투스 및 장치"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["개인 정보", "보안 설정", "privacy", "security", "windows security"],
            [
                ["windows security", "windows 보안"],
                ["privacy & security", "privacy and security", "개인 정보 및 보안"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["장치 정보", "pc 정보", "시스템 정보", "about pc", "device specifications", "system information"],
            [
                ["about", "정보"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["정품 인증", "윈도우 인증", "activation", "activate windows"],
            [
                ["activation", "정품 인증"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["pc 초기화", "컴퓨터 초기화", "복구 옵션", "reset this pc", "recovery options"],
            [
                ["recovery", "복구"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["문제 해결", "문제해결", "troubleshoot", "troubleshooter"],
            [
                ["troubleshoot", "문제 해결"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["클립보드", "clipboard"],
            [
                ["clipboard", "클립보드"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["멀티태스킹", "창 맞춤", "스냅 창", "multitasking", "snap windows"],
            [
                ["multitasking", "멀티태스킹"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["원격 데스크톱", "remote desktop"],
            [
                ["remote desktop", "원격 데스크톱"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["언어 설정", "지역 설정", "표시 언어", "language & region", "language and region", "display language"],
            [
                ["language & region", "language and region", "언어 및 지역"],
                ["time & language", "time and language", "시간 및 언어"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["게임 모드", "xbox game bar", "게임 캡처", "game mode", "gaming"],
            [
                ["game mode", "게임 모드"],
                ["xbox game bar"],
                ["captures", "캡처"],
                ["gaming", "게임"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["작업 표시줄 설정", "작업표시줄 설정", "taskbar settings", "taskbar behaviors"],
            [
                ["taskbar", "작업 표시줄", "작업표시줄"],
                ["personalization", "개인 설정"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["카메라 설정", "웹캠 설정", "camera settings", "webcam settings"],
            [
                ["cameras", "카메라"],
                ["bluetooth & devices", "bluetooth and devices", "블루투스 및 장치"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["인터넷", "네트워크", "와이파이", "wifi", "wi-fi", "network", "internet", "ethernet"],
            [
                ["network", "internet", "wi-fi", "wifi", "ethernet"],
                ["network & internet", "네트워크 및 인터넷"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["소리", "볼륨", "음량", "스피커", "volume", "sound", "speaker", "audio"],
            [
                ["volume", "sound", "speaker", "audio", "볼륨", "소리", "스피커"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["블루투스", "bluetooth"],
            [
                ["bluetooth", "블루투스"],
                ["bluetooth & devices", "bluetooth and devices", "블루투스 및 장치"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["화면 밝기", "밝기", "디스플레이", "해상도", "brightness", "display", "resolution"],
            [
                ["brightness", "밝기"],
                ["display", "디스플레이"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["날짜", "시간", "시계", "date", "time", "clock", "calendar"],
            [
                ["clock", "date", "time", "calendar", "시계", "날짜", "시간", "달력"],
                ["time & language", "time and language", "시간 및 언어"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["배터리", "전원", "battery", "power"],
            [
                ["battery", "power", "배터리", "전원"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["알림", "notification", "notifications"],
            [
                ["notification", "notifications", "알림"],
                ["system", "시스템"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["윈도우 업데이트", "windows update", "업데이트 확인"],
            [
                ["windows update", "윈도우 업데이트"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["접근성", "accessibility"],
            [
                ["accessibility", "접근성"],
                ["settings", "설정"],
                ["start", "시작"],
            ]),
        new(
            ["윈도우 설정", "windows settings", "설정 앱", "설정 열"],
            [
                ["settings", "설정"],
                ["start", "시작"],
            ]),
    ];

    public static bool TryResolve(GuideRequest request, out GuideDecision decision)
    {
        decision = null!;
        if (!string.Equals(request.Context.Platform, Platforms.Windows, StringComparison.Ordinal)) return false;

        var goal = request.Session.OriginalUserMessage.ToLowerInvariant();
        var route = Routes.FirstOrDefault(candidate => candidate.GoalTerms.Any(goal.Contains));
        if (route is null) return false;

        foreach (var stepTerms in route.CandidateSteps)
        {
            var target = request.Candidates
                .Where(IsEligibleTrustedCandidate)
                .Where(candidate => !WasAlreadySelected(request.Session, candidate))
                .Where(candidate => !ContainsExcludedTerm(candidate, route.ExcludedCandidateTerms))
                .Select(candidate => new { Candidate = candidate, Score = Score(candidate, stepTerms) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Candidate.Bounds.Y)
                .ThenBy(item => item.Candidate.Bounds.X)
                .Select(item => item.Candidate)
                .FirstOrDefault();
            if (target is null) continue;

            var label = target.Label ?? target.Description ?? "Windows 항목";
            var isKorean = KoreanText().IsMatch(request.Session.OriginalUserMessage);
            var stepNumber = request.Session.KnownFacts.Count(fact =>
                fact.Contains("컨트롤을 클릭", StringComparison.OrdinalIgnoreCase)) + 1;
            var targetDescription = DescribeKoreanTarget(target, label);
            decision = new GuideDecision(
                GuideStatuses.InProgress,
                GuideActions.Highlight,
                isKorean
                    ? CreateKoreanGuidanceMessage(
                        request.Session.OriginalUserMessage,
                        targetDescription,
                        stepNumber)
                    : $"Step {stepNumber}: Select '{label}'. Only use the highlighted target.",
                0.99,
                target.Id,
                isKorean
                    ? "선택한 Windows 화면이나 설정이 열립니다."
                    : "The selected Windows screen or setting opens.");
            return true;
        }

        return false;
    }

    public static bool IsKnownSystemGoal(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal)) return false;
        var normalized = goal.ToLowerInvariant();
        return Routes.Any(route => route.GoalTerms.Any(normalized.Contains))
            || ContainsAny(normalized, ["사진", "photo", "photos", "picture", "pictures"]);
    }

    internal static bool IsTrustedWindowsProcess(string processName) =>
        TrustedWindowsProcesses.Contains(processName);

    private static bool IsEligibleTrustedCandidate(UiCandidate candidate)
    {
        if (!candidate.Visible || !candidate.Enabled || !candidate.Clickable) return false;
        var processName = StringAttribute(candidate, "processName");
        var scope = StringAttribute(candidate, "sourceScope");
        if (scope == "windows_taskbar") return true;
        if (scope == "windows_window_overview")
            return processName is not null && IsTrustedWindowsProcess(processName);
        return processName is not null && IsTrustedWindowsProcess(processName);
    }

    private static int Score(UiCandidate candidate, IReadOnlyList<string> terms)
    {
        var searchable = $"{candidate.Label} {candidate.Description}".ToLowerInvariant();
        var matchedTerms = terms.Where(searchable.Contains).ToArray();
        if (matchedTerms.Length == 0) return 0;

        var score = matchedTerms.Max(term => term.Length) * 10 + matchedTerms.Length;
        var label = candidate.Label?.Trim().ToLowerInvariant();
        if (label is not null && terms.Any(term => string.Equals(label, term, StringComparison.Ordinal))) score += 100;
        if (StringAttribute(candidate, "sourceScope") == "windows_taskbar") score += 20;
        if (candidate.Role is "button" or "menuitem" or "link") score += 5;
        return score;
    }

    private static bool ContainsExcludedTerm(UiCandidate candidate, IReadOnlyList<string>? excludedTerms)
    {
        if (excludedTerms is null || excludedTerms.Count == 0) return false;
        var searchable = $"{candidate.Label} {candidate.Description}".ToLowerInvariant();
        return excludedTerms.Any(searchable.Contains);
    }

    private static bool WasAlreadySelected(TaskSession session, UiCandidate candidate)
    {
        var label = candidate.Label?.Trim();
        if (string.IsNullOrWhiteSpace(label)) return false;
        return session.KnownFacts.Any(fact =>
            fact.Contains($"'{label}'", StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeKoreanTarget(UiCandidate candidate, string label)
    {
        var normalized = label.Trim().ToLowerInvariant();
        if (normalized == "start" || normalized == "시작")
            return "화면 아래 작업표시줄의 '시작' 버튼";
        if (normalized is "settings" or "설정") return "'설정'";
        if (normalized.Contains("bluetooth & devices")
            || normalized.Contains("bluetooth and devices")
            || normalized.Contains("블루투스 및 장치")) return "'Bluetooth 및 장치'";
        if (normalized.Contains("printers & scanners")
            || normalized.Contains("printers and scanners")
            || normalized.Contains("프린터 및 스캐너")) return "'프린터 및 스캐너'";
        if (StringAttribute(candidate, "sourceScope") == "windows_taskbar")
            return $"화면 아래 작업표시줄의 '{label}'";
        return $"'{label}'";
    }

    private static string CreateKoreanGuidanceMessage(
        string goal,
        string targetDescription,
        int stepNumber)
    {
        if (stepNumber > 1)
            return $"좋아요! 그렇다면 이제 다음으로 누를 곳은 {targetDescription}예요. 제가 표시한 곳을 눌러보세요!";

        var normalizedGoal = goal.ToLowerInvariant();
        if (ContainsAny(normalizedGoal, ["프린터", "프린트", "printer", "printing"]))
        {
            return "프린터 설정을 확인하고 싶으시군요! Windows의 프린터 설정으로 이동해야 해요. "
                + $"우선 다음으로 누를 곳은 {targetDescription}입니다. 제가 표시한 곳을 눌러보시겠어요?";
        }

        var intent = DescribeKoreanIntent(normalizedGoal);
        return $"{intent} 함께 차근차근 찾아볼게요. "
            + $"우선 다음으로 누를 곳은 {targetDescription}입니다. 제가 표시한 곳을 눌러보시겠어요?";
    }

    private static string DescribeKoreanIntent(string normalizedGoal)
    {
        if (ContainsAny(normalizedGoal, ["계산기", "calculator", "calc"]))
            return "계산기를 찾고 계시는군요!";
        if (ContainsAny(normalizedGoal, ["메모장", "notepad"]))
            return "메모장을 열고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["그림판", "paint", "mspaint"]))
            return "그림판을 열고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["캡처 도구", "snipping tool"]))
            return "캡처 도구를 열고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["카메라", "찍은 사진", "camera", "webcam"]))
            return "카메라로 찍은 사진을 확인하고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["스크린샷", "화면 캡처", "screenshot"]))
            return "저장된 스크린샷을 찾고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["다운로드", "download"]))
            return "다운로드한 파일을 찾고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["인터넷", "네트워크", "와이파이", "wifi", "network", "internet"]))
            return "인터넷과 네트워크 상태를 확인하고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["소리", "볼륨", "음량", "스피커", "sound", "volume"]))
            return "소리 설정을 확인하고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["블루투스", "bluetooth"]))
            return "Bluetooth 설정을 확인하고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["밝기", "디스플레이", "해상도", "brightness", "display"]))
            return "화면 설정을 확인하고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["날짜", "시간", "시계", "date", "time", "clock"]))
            return "날짜와 시간 설정을 확인하고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["배터리", "전원", "battery", "power"]))
            return "배터리와 전원 설정을 확인하고 싶으시군요!";
        if (ContainsAny(normalizedGoal, ["알림", "notification"]))
            return "알림 설정을 확인하고 싶으시군요!";
        return "원하시는 작업을 확인했어요!";
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> terms) => terms.Any(value.Contains);

    private static string? StringAttribute(UiCandidate candidate, string name) =>
        candidate.Attributes?.TryGetValue(name, out var value) == true ? Convert.ToString(value) : null;

    [GeneratedRegex("[가-힣]")]
    private static partial Regex KoreanText();
}

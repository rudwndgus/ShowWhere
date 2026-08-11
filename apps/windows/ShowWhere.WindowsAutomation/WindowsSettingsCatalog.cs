namespace ShowWhere.WindowsAutomation;

public sealed record WindowsSettingsRoute(
    string Id,
    string[] GoalTerms,
    string[] Breadcrumb,
    string[][] PageAliases,
    string SettingsUri)
{
    public IReadOnlyList<string[]> CandidateSteps =>
        [.. PageAliases.Reverse(), ["settings", "설정"], ["start", "시작"]];
}

/// <summary>
/// Stable Windows Settings navigation knowledge based on Microsoft's documented
/// Settings categories and ms-settings URI reference. UI aliases cover the common
/// Windows 10/11 Korean and English labels; runtime selection still requires a real
/// visible UI Automation candidate from the current computer.
/// </summary>
public static class WindowsSettingsCatalog
{
    private static readonly WindowsSettingsRoute[] Routes =
    [
        Route("display", ["디스플레이", "화면 밝기", "밝기", "해상도", "배율", "display", "brightness", "resolution", "scale"],
            ["설정", "시스템", "디스플레이"], [["system", "시스템"], ["display", "디스플레이"]], "ms-settings:display"),
        Route("sound", ["소리 설정", "사운드 설정", "입력 장치", "출력 장치", "마이크 볼륨", "sound settings", "audio output", "audio input"],
            ["설정", "시스템", "소리"], [["system", "시스템"], ["sound", "소리"]], "ms-settings:sound"),
        Route("notifications", ["알림 설정", "방해 금지", "notification settings", "do not disturb"],
            ["설정", "시스템", "알림"], [["system", "시스템"], ["notifications", "notification", "알림"]], "ms-settings:notifications"),
        Route("power", ["전원 및 배터리", "절전", "화면 꺼짐", "배터리 사용량", "power & battery", "power and battery", "sleep settings", "battery usage"],
            ["설정", "시스템", "전원 및 배터리"], [["system", "시스템"], ["power & battery", "power and battery", "전원 및 배터리", "전원"]], "ms-settings:powersleep"),
        Route("storage", ["저장 공간", "저장소", "디스크 공간", "임시 파일", "storage", "disk space", "temporary files"],
            ["설정", "시스템", "저장소"], [["system", "시스템"], ["storage", "저장소", "저장 공간"]], "ms-settings:storagesense"),
        Route("multitasking", ["멀티태스킹", "스냅 창", "창 맞춤", "multitasking", "snap windows"],
            ["설정", "시스템", "멀티태스킹"], [["system", "시스템"], ["multitasking", "멀티태스킹"]], "ms-settings:multitasking"),
        Route("recovery", ["pc 초기화", "컴퓨터 초기화", "복구 옵션", "고급 시작 옵션", "reset this pc", "recovery options", "advanced startup"],
            ["설정", "시스템", "복구"], [["system", "시스템"], ["recovery", "복구"]], "ms-settings:recovery"),
        Route("activation", ["정품 인증", "윈도우 인증", "제품 키", "activation", "activate windows", "product key"],
            ["설정", "시스템", "정품 인증"], [["system", "시스템"], ["activation", "정품 인증"]], "ms-settings:activation"),
        Route("troubleshoot", ["문제 해결", "문제해결사", "troubleshoot", "troubleshooter"],
            ["설정", "시스템", "문제 해결"], [["system", "시스템"], ["troubleshoot", "문제 해결"]], "ms-settings:troubleshoot"),
        Route("clipboard", ["클립보드", "클립보드 기록", "clipboard", "clipboard history"],
            ["설정", "시스템", "클립보드"], [["system", "시스템"], ["clipboard", "클립보드"]], "ms-settings:clipboard"),
        Route("remote-desktop", ["원격 데스크톱", "remote desktop"],
            ["설정", "시스템", "원격 데스크톱"], [["system", "시스템"], ["remote desktop", "원격 데스크톱"]], "ms-settings:remotedesktop"),
        Route("about", ["장치 정보", "pc 정보", "내 컴퓨터 사양", "시스템 정보", "about pc", "device specifications", "system information"],
            ["설정", "시스템", "정보"], [["system", "시스템"], ["about", "정보"]], "ms-settings:about"),

        Route("bluetooth-pair", ["블루투스 이어폰", "블루투스 헤드폰", "장치 연결", "기기 연결", "페어링", "pair bluetooth", "connect bluetooth"],
            ["설정", "Bluetooth 및 장치", "장치 추가"], [["bluetooth & devices", "bluetooth and devices", "bluetooth 및 장치", "블루투스 및 장치"], ["devices", "장치", "bluetooth"], ["add device", "장치 추가", "디바이스 추가"]], "ms-settings:bluetooth"),
        Route("bluetooth", ["블루투스", "bluetooth", "장치 추가", "add device"],
            ["설정", "Bluetooth 및 장치", "장치"], [["bluetooth & devices", "bluetooth and devices", "bluetooth 및 장치", "블루투스 및 장치"], ["devices", "장치", "bluetooth"]], "ms-settings:bluetooth"),
        Route("printers", ["프린터", "프린트", "스캐너", "printer", "printers", "printing", "scanner"],
            ["설정", "Bluetooth 및 장치", "프린터 및 스캐너"], [["bluetooth & devices", "bluetooth and devices", "bluetooth 및 장치", "블루투스 및 장치"], ["printers & scanners", "printers and scanners", "프린터 및 스캐너", "프린터와 스캐너"]], "ms-settings:printers"),
        Route("mouse", ["마우스 설정", "마우스 속도", "기본 마우스 단추", "mouse settings", "mouse speed", "primary mouse button"],
            ["설정", "Bluetooth 및 장치", "마우스"], [["bluetooth & devices", "bluetooth and devices", "bluetooth 및 장치", "블루투스 및 장치"], ["mouse", "마우스"]], "ms-settings:mousetouchpad"),
        Route("touchpad", ["터치패드", "touchpad"],
            ["설정", "Bluetooth 및 장치", "터치패드"], [["bluetooth & devices", "bluetooth and devices", "bluetooth 및 장치", "블루투스 및 장치"], ["touchpad", "터치패드"]], "ms-settings:devices-touchpad"),
        Route("typing", ["키보드 설정", "입력 설정", "자동 고침", "keyboard settings", "typing settings", "autocorrect"],
            ["설정", "시간 및 언어", "입력"], [["time & language", "time and language", "시간 및 언어"], ["typing", "입력"]], "ms-settings:typing"),
        Route("cameras", ["카메라 설정", "웹캠 설정", "camera settings", "webcam settings"],
            ["설정", "Bluetooth 및 장치", "카메라"], [["bluetooth & devices", "bluetooth and devices", "bluetooth 및 장치", "블루투스 및 장치"], ["cameras", "카메라"]], "ms-settings:camera"),
        Route("autoplay", ["자동 실행", "autoplay"],
            ["설정", "Bluetooth 및 장치", "자동 실행"], [["bluetooth & devices", "bluetooth and devices", "bluetooth 및 장치", "블루투스 및 장치"], ["autoplay", "자동 실행"]], "ms-settings:autoplay"),
        Route("usb", ["usb 설정", "usb 알림", "usb settings"],
            ["설정", "Bluetooth 및 장치", "USB"], [["bluetooth & devices", "bluetooth and devices", "bluetooth 및 장치", "블루투스 및 장치"], ["usb", "USB"]], "ms-settings:usb"),

        Route("wifi", ["와이파이", "wi-fi", "wifi", "무선 네트워크"],
            ["설정", "네트워크 및 인터넷", "Wi-Fi"], [["network & internet", "network and internet", "네트워크 및 인터넷"], ["wi-fi", "wifi", "와이파이"]], "ms-settings:network-wifi"),
        Route("ethernet", ["이더넷", "ethernet", "유선 네트워크"],
            ["설정", "네트워크 및 인터넷", "이더넷"], [["network & internet", "network and internet", "네트워크 및 인터넷"], ["ethernet", "이더넷"]], "ms-settings:network-ethernet"),
        Route("vpn", ["vpn", "가상 사설망"],
            ["설정", "네트워크 및 인터넷", "VPN"], [["network & internet", "network and internet", "네트워크 및 인터넷"], ["vpn", "VPN"]], "ms-settings:network-vpn"),
        Route("hotspot", ["모바일 핫스팟", "핫스팟", "mobile hotspot", "hotspot"],
            ["설정", "네트워크 및 인터넷", "모바일 핫스팟"], [["network & internet", "network and internet", "네트워크 및 인터넷"], ["mobile hotspot", "모바일 핫스팟"]], "ms-settings:network-mobilehotspot"),
        Route("proxy", ["프록시", "proxy"],
            ["설정", "네트워크 및 인터넷", "프록시"], [["network & internet", "network and internet", "네트워크 및 인터넷"], ["proxy", "프록시"]], "ms-settings:network-proxy"),
        Route("airplane", ["비행기 모드", "에어플레인 모드", "airplane mode"],
            ["설정", "네트워크 및 인터넷", "비행기 모드"], [["network & internet", "network and internet", "네트워크 및 인터넷"], ["airplane mode", "비행기 모드"]], "ms-settings:network-airplanemode"),

        Route("background", ["배경 화면", "바탕 화면 배경", "wallpaper", "desktop background"],
            ["설정", "개인 설정", "배경"], [["personalization", "개인 설정"], ["background", "배경"]], "ms-settings:personalization-background"),
        Route("colors", ["윈도우 색", "강조 색", "다크 모드", "라이트 모드", "colors", "accent color", "dark mode", "light mode"],
            ["설정", "개인 설정", "색"], [["personalization", "개인 설정"], ["colors", "색"]], "ms-settings:colors"),
        Route("themes", ["테마", "theme", "themes"],
            ["설정", "개인 설정", "테마"], [["personalization", "개인 설정"], ["themes", "테마"]], "ms-settings:themes"),
        Route("lock-screen", ["잠금 화면", "lock screen"],
            ["설정", "개인 설정", "잠금 화면"], [["personalization", "개인 설정"], ["lock screen", "잠금 화면"]], "ms-settings:lockscreen"),
        Route("start", ["시작 메뉴 설정", "시작 설정", "start menu settings"],
            ["설정", "개인 설정", "시작"], [["personalization", "개인 설정"], ["start", "시작"]], "ms-settings:personalization-start"),
        Route("taskbar", ["작업 표시줄 설정", "작업표시줄 설정", "taskbar settings", "taskbar behaviors"],
            ["설정", "개인 설정", "작업 표시줄"], [["personalization", "개인 설정"], ["taskbar", "작업 표시줄", "작업표시줄"]], "ms-settings:taskbar"),
        Route("fonts", ["글꼴", "폰트 설정", "font settings", "fonts"],
            ["설정", "개인 설정", "글꼴"], [["personalization", "개인 설정"], ["fonts", "글꼴"]], "ms-settings:fonts"),

        Route("installed-apps", ["설치된 앱", "설치된 프로그램", "앱 제거", "앱 삭제", "프로그램 제거", "프로그램 삭제", "installed apps", "uninstall app", "uninstall program"],
            ["설정", "앱", "설치된 앱"], [["apps", "앱"], ["installed apps", "설치된 앱", "apps & features", "앱 및 기능"]], "ms-settings:appsfeatures"),
        Route("default-apps", ["기본 앱", "기본 프로그램", "파일 연결", "default apps", "default program", "file association"],
            ["설정", "앱", "기본 앱"], [["apps", "앱"], ["default apps", "기본 앱"]], "ms-settings:defaultapps"),
        Route("startup-apps", ["시작 프로그램", "시작 앱", "startup apps", "startup programs"],
            ["설정", "앱", "시작 프로그램"], [["apps", "앱"], ["startup", "시작 프로그램", "시작 앱"]], "ms-settings:startupapps"),
        Route("optional-features", ["선택적 기능", "윈도우 기능 추가", "optional features", "add windows feature"],
            ["설정", "시스템", "선택적 기능"], [["system", "시스템"], ["optional features", "선택적 기능"]], "ms-settings:optionalfeatures"),

        Route("signin", ["로그인 옵션", "비밀번호 변경", "pin 변경", "windows hello", "sign-in options", "change password", "change pin"],
            ["설정", "계정", "로그인 옵션"], [["accounts", "계정"], ["sign-in options", "로그인 옵션"]], "ms-settings:signinoptions"),
        Route("your-info", ["내 정보", "계정 사진", "your info", "account picture"],
            ["설정", "계정", "사용자 정보"], [["accounts", "계정"], ["your info", "사용자 정보", "내 정보"]], "ms-settings:yourinfo"),
        Route("email-accounts", ["이메일 계정", "앱 계정", "email accounts", "email & accounts"],
            ["설정", "계정", "내 계정"], [["accounts", "계정"], ["my account", "내 계정", "email & accounts", "email and accounts", "이메일 및 계정"]], "ms-settings:emailandaccounts"),
        Route("family", ["가족 계정", "다른 사용자", "family", "other users"],
            ["설정", "계정", "가족 및 다른 사용자"], [["accounts", "계정"], ["family", "other users", "가족", "다른 사용자"]], "ms-settings:otherusers"),
        Route("backup", ["윈도우 백업", "설정 동기화", "windows backup", "sync settings", "remember my preferences"],
            ["설정", "계정", "Windows 백업"], [["accounts", "계정"], ["windows backup", "windows 백업"]], "ms-settings:backup"),
        Route("workplace", ["회사 또는 학교 액세스", "회사 계정", "학교 계정", "access work or school", "work account", "school account"],
            ["설정", "계정", "회사 또는 학교 액세스"], [["accounts", "계정"], ["access work or school", "회사 또는 학교 액세스"]], "ms-settings:workplace"),

        Route("date-time", ["날짜 및 시간", "시간대", "시계 설정", "date & time", "date and time", "time zone", "clock settings"],
            ["설정", "시간 및 언어", "날짜 및 시간"], [["time & language", "time and language", "시간 및 언어"], ["date & time", "date and time", "날짜 및 시간"]], "ms-settings:dateandtime"),
        Route("language", ["언어 설정", "지역 설정", "표시 언어", "키보드 언어", "language & region", "language and region", "display language", "keyboard language"],
            ["설정", "시간 및 언어", "언어 및 지역"], [["time & language", "time and language", "시간 및 언어"], ["language & region", "language and region", "언어 및 지역"]], "ms-settings:regionlanguage"),
        Route("speech", ["음성 설정", "음성 언어", "speech settings", "speech language"],
            ["설정", "시간 및 언어", "음성"], [["time & language", "time and language", "시간 및 언어"], ["speech", "음성"]], "ms-settings:speech"),

        Route("game-bar", ["게임 바", "xbox game bar", "game bar"],
            ["설정", "게임", "Game Bar"], [["gaming", "게임"], ["game bar", "xbox game bar"]], "ms-settings:gaming-gamebar"),
        Route("game-captures", ["게임 캡처", "게임 녹화", "game captures", "game recording"],
            ["설정", "게임", "캡처"], [["gaming", "게임"], ["captures", "캡처"]], "ms-settings:gaming-gamedvr"),
        Route("game-mode", ["게임 모드", "game mode"],
            ["설정", "게임", "게임 모드"], [["gaming", "게임"], ["game mode", "게임 모드"]], "ms-settings:gaming-gamemode"),

        Route("accessibility-vision", ["텍스트 크기", "마우스 포인터 크기", "색 필터", "고대비", "내레이터", "돋보기", "text size", "mouse pointer size", "color filters", "contrast themes", "narrator", "magnifier"],
            ["설정", "접근성", "시각"], [["accessibility", "접근성"], ["vision", "시각", "text size", "텍스트 크기", "magnifier", "돋보기", "narrator", "내레이터"]], "ms-settings:easeofaccess-display"),
        Route("accessibility-captions", ["자막", "캡션", "captions"],
            ["설정", "접근성", "캡션"], [["accessibility", "접근성"], ["captions", "캡션", "자막"]], "ms-settings:easeofaccess-closedcaptioning"),
        Route("accessibility-hearing", ["청각 장치", "오디오 접근성", "hearing devices", "audio accessibility"],
            ["설정", "접근성", "오디오"], [["accessibility", "접근성"], ["hearing", "청각", "audio", "오디오"]], "ms-settings:easeofaccess-audio"),
        Route("accessibility-input", ["고정 키", "화상 키보드", "음성 액세스", "눈 제어", "sticky keys", "on-screen keyboard", "voice access", "eye control"],
            ["설정", "접근성", "상호 작용"], [["accessibility", "접근성"], ["interaction", "상호 작용", "keyboard", "키보드", "speech", "음성", "eye control", "눈 제어"]], "ms-settings:easeofaccess-keyboard"),

        Route("windows-security", ["윈도우 보안", "바이러스 및 위협 방지", "방화벽", "windows security", "virus & threat protection", "firewall"],
            ["설정", "개인 정보 및 보안", "Windows 보안"], [["privacy & security", "privacy and security", "개인 정보 및 보안"], ["windows security", "windows 보안"]], "ms-settings:windowsdefender"),
        Route("location-privacy", ["위치 권한", "위치 서비스", "location permission", "location services"],
            ["설정", "개인 정보 및 보안", "위치"], [["privacy & security", "privacy and security", "개인 정보 및 보안"], ["location", "위치"]], "ms-settings:privacy-location"),
        Route("camera-privacy", ["카메라 권한", "카메라 액세스", "camera permission", "camera access"],
            ["설정", "개인 정보 및 보안", "카메라"], [["privacy & security", "privacy and security", "개인 정보 및 보안"], ["camera", "카메라"]], "ms-settings:privacy-webcam"),
        Route("microphone-privacy", ["마이크 권한", "마이크 액세스", "microphone permission", "microphone access"],
            ["설정", "개인 정보 및 보안", "마이크"], [["privacy & security", "privacy and security", "개인 정보 및 보안"], ["microphone", "마이크"]], "ms-settings:privacy-microphone"),
        Route("developer", ["개발자 모드", "개발자용", "developer mode", "for developers"],
            ["설정", "시스템", "개발자용"], [["system", "시스템"], ["for developers", "개발자용"]], "ms-settings:developers"),

        Route("windows-update", ["윈도우 업데이트", "업데이트 확인", "windows update", "check for updates"],
            ["설정", "Windows 업데이트"], [["windows update", "windows 업데이트", "윈도우 업데이트"]], "ms-settings:windowsupdate"),
        Route("update-history", ["업데이트 기록", "업데이트 내역", "update history"],
            ["설정", "Windows 업데이트", "업데이트 기록"], [["windows update", "windows 업데이트", "윈도우 업데이트"], ["update history", "업데이트 기록"]], "ms-settings:windowsupdate-history"),
        Route("update-advanced", ["업데이트 고급 옵션", "선택적 업데이트", "advanced update options", "optional updates"],
            ["설정", "Windows 업데이트", "고급 옵션"], [["windows update", "windows 업데이트", "윈도우 업데이트"], ["advanced options", "고급 옵션", "optional updates", "선택적 업데이트"]], "ms-settings:windowsupdate-options"),
    ];

    public static bool TryFind(string? goal, out WindowsSettingsRoute route)
    {
        route = null!;
        if (string.IsNullOrWhiteSpace(goal)) return false;
        var normalized = goal.Trim().ToLowerInvariant();
        route = Routes
            .SelectMany(candidate => candidate.GoalTerms
                .Where(normalized.Contains)
                .Select(term => new
                {
                    Route = candidate,
                    Score = candidate.PageAliases.Length * 100 + term.Length,
                    GoalIndex = normalized.IndexOf(term, StringComparison.Ordinal),
                }))
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.GoalIndex)
            .Select(match => match.Route)
            .FirstOrDefault()!;
        return route is not null;
    }

    public static bool IsSettingsGoal(string? goal) => TryFind(goal, out _);

    private static WindowsSettingsRoute Route(
        string id,
        string[] goalTerms,
        string[] breadcrumb,
        string[][] pages,
        string settingsUri) => new(id, goalTerms, breadcrumb, pages, settingsUri);
}

public static class WindowsWindowChromeFilter
{
    private static readonly HashSet<string> CaptionLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "minimize", "최소화", "maximize", "최대화", "restore", "복원", "restore down", "이전 크기로 복원",
        "close", "닫기", "system menu", "시스템 메뉴",
        "system menu bar", "시스템 메뉴 모음",
    };

    public static bool IsCaptionControl(ShowWhere.Core.UiCandidate candidate)
    {
        var label = candidate.Label?.Trim();
        if (label is not null && CaptionLabels.Contains(label)) return true;
        if (string.Equals(candidate.Role, "menubar", StringComparison.OrdinalIgnoreCase)
            && label?.StartsWith("system menu", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        if (string.Equals(candidate.Role, "menuitem", StringComparison.OrdinalIgnoreCase)
            && string.Equals(label, "system", StringComparison.OrdinalIgnoreCase))
            return true;
        var automationId = StringAttribute(candidate, "automationId");
        if (automationId is null) return false;
        return automationId.Contains("minimize", StringComparison.OrdinalIgnoreCase)
            || automationId.Contains("maximize", StringComparison.OrdinalIgnoreCase)
            || automationId.Contains("restore", StringComparison.OrdinalIgnoreCase)
            || automationId.Contains("close", StringComparison.OrdinalIgnoreCase)
            || automationId.Contains("caption", StringComparison.OrdinalIgnoreCase)
            || automationId.Contains("NavigationViewBackButton", StringComparison.OrdinalIgnoreCase);
    }

    private static string? StringAttribute(ShowWhere.Core.UiCandidate candidate, string name) =>
        candidate.Attributes?.TryGetValue(name, out var value) == true ? Convert.ToString(value) : null;
}

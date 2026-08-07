using ShowWhere.Core;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class WindowsFastPathResolverTests
{
    [Fact]
    public void Calculator_goal_opens_start_then_selects_the_calculator_tile_without_ai()
    {
        var initial = Request("계산기 어디야?", [
            Candidate("network", "Network Internet access", "explorer", "windows_taskbar"),
            Candidate("start", "Start", "explorer", "windows_taskbar"),
        ]);

        Assert.True(WindowsFastPathResolver.TryResolve(initial, out var first));
        Assert.Equal("start", first.TargetId);
        Assert.Contains("계산기를 찾고 계시는군요", first.Message);

        var startMenu = Request("계산기 어디야?", [
            Candidate("calculator", "Calculator", "StartMenuExperienceHost"),
            Candidate("settings", "Settings", "StartMenuExperienceHost"),
            Candidate("start", "Start", "explorer", "windows_taskbar"),
        ], ["사용자가 'Start' 컨트롤을 클릭함."]);

        Assert.True(WindowsFastPathResolver.TryResolve(startMenu, out var second));
        Assert.Equal("calculator", second.TargetId);
        Assert.StartsWith("좋아요!", second.Message);
        Assert.DoesNotContain("Settings", second.Message);
    }

    [Fact]
    public void Photo_goal_switches_to_an_open_photos_window_instead_of_scanning_the_active_app()
    {
        var request = Request("내가 컴퓨터로 찍은 사진은 어디서 봐야해?", [
            Candidate("erp", "POS Tech Support", "bluuERP"),
            Candidate("photos", "Microsoft Photos", "Photos", "windows_window_overview"),
            Candidate("start", "Start", "explorer", "windows_taskbar"),
        ]);

        var resolved = WindowsFastPathResolver.TryResolve(request, out var decision);

        Assert.True(resolved);
        Assert.Equal("photos", decision.TargetId);
    }

    [Fact]
    public void Camera_photo_goal_never_selects_an_open_screenshots_window()
    {
        var request = Request("내가 컴퓨터 카메라로 찍은 사진은 어디서 볼 수 있어?", [
            Candidate("screenshots", "Screenshots - File Explorer", "explorer", "windows_window_overview"),
            Candidate("explorer", "File Explorer", "explorer", "windows_taskbar"),
        ]);

        var resolved = WindowsFastPathResolver.TryResolve(request, out var decision);

        Assert.True(resolved);
        Assert.Equal("explorer", decision.TargetId);
    }

    [Fact]
    public void Screenshot_goal_prefers_screenshots_window()
    {
        var request = Request("스크린샷을 보고 싶어", [
            Candidate("screenshots", "Screenshots - File Explorer", "explorer", "windows_window_overview"),
            Candidate("explorer", "File Explorer", "explorer", "windows_taskbar"),
        ]);

        var resolved = WindowsFastPathResolver.TryResolve(request, out var decision);

        Assert.True(resolved);
        Assert.Equal("screenshots", decision.TargetId);
    }

    [Fact]
    public void Explicit_pictures_folder_goal_uses_pictures_without_clarifying_again()
    {
        var request = Request("Windows 사진 폴더에 저장된 일반 사진을 보고 싶어", [
            Candidate("pictures", "Pictures", "explorer"),
            Candidate("explorer", "File Explorer", "explorer", "windows_taskbar"),
        ]);

        var resolved = WindowsFastPathResolver.TryResolve(request, out var decision);

        Assert.True(resolved);
        Assert.Equal("pictures", decision.TargetId);
    }

    [Fact]
    public void Korean_internet_goal_selects_the_network_control_without_ai()
    {
        var request = Request("인터넷 상태 확인하고 싶어", [
            Candidate("network", "Network ebluu.com Internet access", "explorer", "windows_taskbar"),
            Candidate("clock", "Clock 11:30 AM", "explorer", "windows_taskbar"),
        ]);

        var resolved = WindowsFastPathResolver.TryResolve(request, out var decision);

        Assert.True(resolved);
        Assert.Equal("network", decision.TargetId);
        Assert.Equal(0.99, decision.Confidence);
        Assert.Contains("함께 차근차근", decision.Message);
        Assert.Contains("눌러보시겠어요", decision.Message);
    }

    [Fact]
    public void Printer_goal_ignores_network_and_starts_the_settings_route()
    {
        var request = Request("프린터 연결은 어디서 확인해?", [
            Candidate("network", "Network ebluu.com Internet access", "explorer", "windows_taskbar"),
            Candidate("start", "Start", "explorer", "windows_taskbar"),
        ]);

        var resolved = WindowsFastPathResolver.TryResolve(request, out var decision);

        Assert.True(resolved);
        Assert.Equal("start", decision.TargetId);
        Assert.DoesNotContain("Network", decision.Message);
        Assert.Contains("작업표시줄의 '시작' 버튼", decision.Message);
        Assert.Contains("프린터 설정을 확인하고 싶으시군요", decision.Message);
        Assert.Contains("눌러보시겠어요", decision.Message);
    }

    [Theory]
    [InlineData("settings", "Settings", "start")]
    [InlineData("devices", "Bluetooth & devices", "settings")]
    [InlineData("printers", "Printers & scanners", "devices")]
    public void Printer_route_selects_only_the_deepest_available_next_step(
        string expectedId,
        string expectedLabel,
        string fallbackId)
    {
        var request = Request("프린터 연결은 어디서 확인해?", [
            Candidate(fallbackId, fallbackId, "SystemSettings"),
            Candidate(expectedId, expectedLabel, "SystemSettings"),
            Candidate("network", "Network Internet access", "explorer", "windows_taskbar"),
        ]);

        var resolved = WindowsFastPathResolver.TryResolve(request, out var decision);

        Assert.True(resolved);
        Assert.Equal(expectedId, decision.TargetId);
        Assert.DoesNotContain("Network", decision.Message);
    }

    [Fact]
    public void Windows_fast_path_does_not_switch_to_an_untrusted_browser_overview()
    {
        var request = Request("프린터 연결은 어디서 확인해?", [
            Candidate("browser", "Printer Settings Guide - Chrome", "chrome", "windows_window_overview"),
            Candidate("start", "Start", "explorer", "windows_taskbar"),
        ]);

        var resolved = WindowsFastPathResolver.TryResolve(request, out var decision);

        Assert.True(resolved);
        Assert.Equal("start", decision.TargetId);
    }

    [Fact]
    public void Printer_route_never_highlights_the_same_completed_step_again()
    {
        var request = Request("프린터 연결은 어디서 확인해?", [
            Candidate("start", "Start", "explorer", "windows_taskbar"),
            Candidate("network", "Network Internet access", "explorer", "windows_taskbar"),
        ], ["사용자가 'Start' 컨트롤을 클릭함."]);

        Assert.False(WindowsFastPathResolver.TryResolve(request, out _));
    }

    [Fact]
    public void Printer_followup_acknowledges_progress_and_guides_only_the_next_target()
    {
        var request = Request("프린터 연결은 어디서 확인해?", [
            Candidate("settings", "Settings", "SystemSettings"),
            Candidate("network", "Network Internet access", "explorer", "windows_taskbar"),
        ], ["사용자가 'Start' 컨트롤을 클릭함."]);

        var resolved = WindowsFastPathResolver.TryResolve(request, out var decision);

        Assert.True(resolved);
        Assert.Equal("settings", decision.TargetId);
        Assert.StartsWith("좋아요!", decision.Message);
        Assert.Contains("'설정'", decision.Message);
        Assert.DoesNotContain("프린터 및 스캐너", decision.Message);
        Assert.DoesNotContain("Network", decision.Message);
    }

    [Fact]
    public void Printer_route_advances_start_settings_devices_and_printers_in_order()
    {
        var facts = new List<string>();
        var screens = new[]
        {
            (Expected: "start", Candidates: new[] { Candidate("start", "Start", "explorer", "windows_taskbar") }),
            (Expected: "settings", Candidates: new[] { Candidate("settings", "Settings", "StartMenuExperienceHost") }),
            (Expected: "devices", Candidates: new[] { Candidate("devices", "Bluetooth & devices", "SystemSettings") }),
            (Expected: "printers", Candidates: new[] { Candidate("printers", "Printers & scanners", "SystemSettings") }),
        };

        foreach (var screen in screens)
        {
            var request = Request("프린터 연결은 어디서 확인해?", screen.Candidates, facts);
            Assert.True(WindowsFastPathResolver.TryResolve(request, out var decision));
            Assert.Equal(screen.Expected, decision.TargetId);
            var selected = screen.Candidates.Single(candidate => candidate.Id == decision.TargetId);
            facts.Add($"사용자가 '{selected.Label}' 컨트롤을 클릭함.");
        }
    }

    [Theory]
    [InlineData("기본 앱을 바꾸고 싶어", "Default apps")]
    [InlineData("설치된 앱을 제거하고 싶어", "Installed apps")]
    [InlineData("로그인 옵션을 확인하고 싶어", "Sign-in options")]
    [InlineData("배경 화면을 바꾸고 싶어", "Background")]
    [InlineData("저장 공간을 확인하고 싶어", "Storage")]
    [InlineData("마우스 설정을 열고 싶어", "Mouse")]
    [InlineData("개인 정보 설정을 보고 싶어", "Privacy & security")]
    [InlineData("PC 장치 정보를 보고 싶어", "About")]
    [InlineData("Windows 정품 인증을 확인하고 싶어", "Activation")]
    [InlineData("PC 초기화 복구 옵션을 찾고 싶어", "Recovery")]
    [InlineData("문제 해결 설정을 열고 싶어", "Troubleshoot")]
    [InlineData("클립보드 기록을 설정하고 싶어", "Clipboard")]
    [InlineData("멀티태스킹 설정을 바꾸고 싶어", "Multitasking")]
    [InlineData("원격 데스크톱을 설정하고 싶어", "Remote Desktop")]
    [InlineData("표시 언어를 바꾸고 싶어", "Language & region")]
    [InlineData("게임 모드를 켜고 싶어", "Game Mode")]
    [InlineData("작업 표시줄 설정을 열고 싶어", "Taskbar")]
    [InlineData("웹캠 설정을 확인하고 싶어", "Cameras")]
    public void Windows_settings_catalog_selects_the_specific_visible_destination(
        string goal,
        string destination)
    {
        var request = Request(goal, [
            Candidate("destination", destination, "SystemSettings"),
            Candidate("settings", "Settings", "SystemSettings"),
            Candidate("start", "Start", "explorer", "windows_taskbar"),
        ]);

        Assert.True(WindowsFastPathResolver.TryResolve(request, out var decision));
        Assert.Equal("destination", decision.TargetId);
    }

    [Fact]
    public void Display_goal_ignores_a_web_settings_button_and_uses_windows_start()
    {
        var request = Request("화면 밝기를 바꾸고 싶어", [
            Candidate("web-settings", "Settings", "chrome"),
            Candidate("start", "Start", "explorer", "windows_taskbar"),
        ]);

        var resolved = WindowsFastPathResolver.TryResolve(request, out var decision);

        Assert.True(resolved);
        Assert.Equal("start", decision.TargetId);
    }

    [Fact]
    public void Display_goal_uses_the_deepest_visible_settings_step()
    {
        var request = Request("화면 밝기를 바꾸고 싶어", [
            Candidate("system", "System", "SystemSettings"),
            Candidate("display", "Display", "SystemSettings"),
        ]);

        var resolved = WindowsFastPathResolver.TryResolve(request, out var decision);

        Assert.True(resolved);
        Assert.Equal("display", decision.TargetId);
    }

    [Fact]
    public void Unknown_windows_goal_falls_back_to_ai()
    {
        var request = Request("내 티켓을 확인하고 싶어", [
            Candidate("tickets", "My Tickets", "chrome"),
        ]);

        Assert.False(WindowsFastPathResolver.TryResolve(request, out _));
    }

    [Fact]
    public void Browser_requests_never_use_the_windows_fast_path()
    {
        var request = Request("인터넷 상태 확인", [
            Candidate("network", "Network Internet access", "explorer", "windows_taskbar"),
        ]) with { Context = new ApplicationContext(Platforms.Browser, "chrome") };

        Assert.False(WindowsFastPathResolver.TryResolve(request, out _));
    }

    private static GuideRequest Request(
        string goal,
        IReadOnlyList<UiCandidate> candidates,
        IReadOnlyList<string>? knownFacts = null) => new(
        new TaskSession("session", goal, goal, "guidance", TaskStatuses.WaitingForAi, [], knownFacts ?? [], 0),
        new ApplicationContext(Platforms.Windows, "explorer"),
        candidates);

    private static UiCandidate Candidate(string id, string label, string processName, string? scope = null)
    {
        var attributes = new Dictionary<string, object?> { ["processName"] = processName };
        if (scope is not null) attributes["sourceScope"] = scope;
        return new UiCandidate(
            id,
            label,
            null,
            "button",
            true,
            true,
            true,
            new UiBounds(10, 10, 100, 30),
            attributes);
    }
}

using ShowWhere.Core;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class WindowsSettingsCatalogTests
{
    [Theory]
    [InlineData("프린터를 연결하고 싶어", "printers", "ms-settings:printers")]
    [InlineData("마이크 권한을 켜줘", "microphone-privacy", "ms-settings:privacy-microphone")]
    [InlineData("기본 앱을 바꾸고 싶어", "default-apps", "ms-settings:defaultapps")]
    [InlineData("윈도우 업데이트 기록 보여줘", "update-history", "ms-settings:windowsupdate-history")]
    [InlineData("작업 표시줄 설정 어디야", "taskbar", "ms-settings:taskbar")]
    [InlineData("블루투스 이어폰 연결하고 싶어", "bluetooth-pair", "ms-settings:bluetooth")]
    [InlineData("설치된 프로그램 삭제하는 곳 어디야", "installed-apps", "ms-settings:appsfeatures")]
    public void Resolves_official_windows_settings_routes(string goal, string id, string uri)
    {
        Assert.True(WindowsSettingsCatalog.TryFind(goal, out var route));
        Assert.Equal(id, route.Id);
        Assert.Equal(uri, route.SettingsUri);
        Assert.Equal("설정", route.Breadcrumb[0]);
    }

    [Theory]
    [InlineData("Minimize")]
    [InlineData("최대화")]
    [InlineData("Restore down")]
    [InlineData("닫기")]
    [InlineData("System Menu Bar")]
    public void Recognizes_system_caption_buttons(string label)
    {
        Assert.True(WindowsWindowChromeFilter.IsCaptionControl(Candidate("caption", label)));
    }

    [Fact]
    public void Printer_navigation_never_selects_window_caption_controls()
    {
        var request = new GuideRequest(
            new TaskSession("session", "프린터 설정을 확인하고 싶어", "프린터 설정", "guidance",
                TaskStatuses.WaitingForAi, [], [], 0),
            new ApplicationContext(Platforms.Windows, "SystemSettings", "Settings"),
            [
                Candidate("minimize", "Minimize", "SystemSettings", "MinimizeButton"),
                Candidate("maximize", "Maximize", "SystemSettings", "MaximizeButton"),
                Candidate("close", "Close", "SystemSettings", "CloseButton"),
                Candidate("devices", "Bluetooth & devices", "SystemSettings"),
            ]);

        Assert.True(WindowsFastPathResolver.TryResolve(request, out var decision));
        Assert.Equal("devices", decision.TargetId);
        Assert.Contains("설정 > Bluetooth 및 장치 > 프린터 및 스캐너", decision.Message);
    }

    [Fact]
    public void Settings_candidate_prioritization_removes_caption_controls_before_ai()
    {
        var candidates = new[]
        {
            Candidate("close", "Close", "SystemSettings", "CloseButton"),
            Candidate("printers", "Printers & scanners", "SystemSettings"),
        };

        var prioritized = WindowsCandidatePrioritizer.Prioritize("프린터 설정", candidates);

        Assert.DoesNotContain(prioritized, candidate => candidate.Id == "close");
        Assert.Contains(prioritized, candidate => candidate.Id == "printers");
    }

    [Fact]
    public void Settings_navigation_never_uses_the_back_button_as_a_category_match()
    {
        var back = Candidate("back", "System", "SystemSettings", "NavigationViewBackButton");

        Assert.True(WindowsWindowChromeFilter.IsCaptionControl(back));
        var prioritized = WindowsCandidatePrioritizer.Prioritize("저장 공간 설정", [
            back,
            Candidate("system", "시스템", "SystemSettings"),
        ]);
        Assert.DoesNotContain(prioritized, candidate => candidate.Id == "back");
    }

    [Fact]
    public void Settings_navigation_ignores_the_native_system_menu_item()
    {
        var nativeSystemMenu = new UiCandidate(
            "native-system-menu", "System", null, "menuitem", true, true, true,
            new UiBounds(10, 10, 28, 28),
            new Dictionary<string, object?>
            {
                ["processName"] = "ApplicationFrameHost",
                ["automationId"] = "Item 1",
                ["inViewport"] = true,
            });

        Assert.True(WindowsWindowChromeFilter.IsCaptionControl(nativeSystemMenu));
    }

    [Fact]
    public void Bluetooth_pairing_selects_add_device_instead_of_breadcrumb_or_toggle()
    {
        var request = new GuideRequest(
            new TaskSession("session", "블루투스 이어폰 연결하고 싶어", "블루투스 이어폰 연결", "guidance",
                TaskStatuses.WaitingForAi, [], [], 0),
            new ApplicationContext(Platforms.Windows, "SystemSettings", "Settings"),
            [
                Candidate("breadcrumb", "장치", "SystemSettings"),
                Candidate("toggle", "Bluetooth", "SystemSettings", "SystemSettings_Device_BluetoothRadioToggle_ToggleSwitch"),
                Candidate("add", "장치 추가", "SystemSettings"),
            ]);

        Assert.True(WindowsFastPathResolver.TryResolve(request, out var decision));
        Assert.Equal("add", decision.TargetId);
    }

    private static UiCandidate Candidate(
        string id,
        string label,
        string processName = "SystemSettings",
        string? automationId = null) => new(
        id,
        label,
        null,
        "button",
        true,
        true,
        true,
        new UiBounds(10, 10, 120, 36),
        new Dictionary<string, object?>
        {
            ["processName"] = processName,
            ["automationId"] = automationId,
            ["inViewport"] = true,
        });
}

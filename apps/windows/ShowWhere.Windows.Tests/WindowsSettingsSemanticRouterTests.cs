using ShowWhere.Core;

namespace ShowWhere.Windows.Tests;

public sealed class WindowsSettingsSemanticRouterTests
{
    private static readonly ApplicationContext Context = new(
        Platforms.Windows, "ApplicationFrameHost", "Settings", Locale: "en-US");

    [Theory]
    [InlineData("시스템 설정 어디야?", "System")]
    [InlineData("블루투스 설정을 열고 싶어", "Bluetooth & devices")]
    [InlineData("인터넷 설정 어디야?", "Network & internet")]
    [InlineData("개인 설정을 바꾸고 싶어", "Personalization")]
    [InlineData("앱 설정을 보여줘", "Apps")]
    [InlineData("계정 설정 어디야?", "Accounts")]
    [InlineData("언어 설정을 바꾸고 싶어", "Time & language")]
    [InlineData("게임 설정 어디 있어?", "Gaming")]
    [InlineData("접근성 설정을 열어줘", "Accessibility")]
    [InlineData("개인 정보 설정 어디야?", "Privacy & security")]
    [InlineData("윈도우 업데이트 확인하고 싶어", "Windows Update")]
    public void Resolves_each_Settings_home_concept_to_the_live_candidate(
        string goal, string expectedLabel)
    {
        var candidates = SettingsHomeCandidates();
        Assert.True(WindowsSettingsSemanticRouter.TryResolve(goal, Context, candidates, out var target));
        Assert.Equal(expectedLabel, target.Label);
        Assert.Equal(candidates.Single(item => item.Label == expectedLabel).Bounds, target.Bounds);
    }

    [Theory]
    [InlineData("윈도우 프린터 어디서해?")]
    [InlineData("프린터 설정 어디야?")]
    [InlineData("윈도우에서 프린터 어디서 설정해?")]
    [InlineData("프린터 추가하고 싶어")]
    [InlineData("Printers & scanners 어디 있어?")]
    public void Printer_variants_use_the_current_screen_next_step(string goal)
    {
        Assert.True(WindowsSettingsSemanticRouter.TryResolve(
            goal, Context, SettingsHomeCandidates(), out var homeTarget));
        Assert.Equal("Bluetooth & devices", homeTarget.Label);

        var printers = Candidate("printers-live", "Printers & scanners", new UiBounds(607, 547, 1006, 71));
        Assert.True(WindowsSettingsSemanticRouter.TryResolve(goal, Context, [printers], out var destinationTarget));
        Assert.Same(printers, destinationTarget);
    }

    [Fact]
    public void Never_reuses_a_learned_physical_rectangle()
    {
        var pcA = Candidate("pc-a", "Bluetooth & devices", new UiBounds(84, 270, 280, 36));
        var pcB = Candidate("pc-b", "Bluetooth & devices", new UiBounds(16, 223, 280, 36));
        Assert.True(WindowsSettingsSemanticRouter.TryResolve("프린터 설정 어디야?", Context, [pcB], out var target));
        Assert.Equal(pcB.Id, target.Id);
        Assert.Equal(pcB.Bounds, target.Bounds);
        Assert.NotEqual(pcA.Bounds, target.Bounds);
    }

    [Fact]
    public void Supports_the_native_SystemSettings_process_used_on_other_Windows_builds()
    {
        var context = Context with { ApplicationName = "SystemSettings" };
        Assert.True(WindowsSettingsSemanticRouter.TryResolve(
            "printer settings", context, SettingsHomeCandidates(), out var target));
        Assert.Equal("Bluetooth & devices", target.Label);
    }

    private static UiCandidate[] SettingsHomeCandidates() =>
    [
        Candidate("system", "System", new UiBounds(84, 230, 280, 36)),
        Candidate("bluetooth", "Bluetooth & devices", new UiBounds(84, 270, 280, 36)),
        Candidate("network", "Network & internet", new UiBounds(84, 310, 280, 36)),
        Candidate("personalization", "Personalization", new UiBounds(84, 350, 280, 36)),
        Candidate("apps", "Apps", new UiBounds(84, 390, 280, 36)),
        Candidate("accounts", "Accounts", new UiBounds(84, 430, 280, 36)),
        Candidate("time", "Time & language", new UiBounds(84, 470, 280, 36)),
        Candidate("gaming", "Gaming", new UiBounds(84, 510, 280, 36)),
        Candidate("accessibility", "Accessibility", new UiBounds(84, 550, 280, 36)),
        Candidate("privacy", "Privacy & security", new UiBounds(84, 590, 280, 36)),
        Candidate("update", "Windows Update", new UiBounds(84, 630, 280, 36)),
    ];

    private static UiCandidate Candidate(string id, string label, UiBounds bounds) => new(
        id, label, null, "listitem", true, true, true, bounds,
        new Dictionary<string, object?>
        {
            ["processName"] = "ApplicationFrameHost",
            ["sourceScope"] = "foreground_application",
            ["controlType"] = "ControlType.ListItem",
        });
}

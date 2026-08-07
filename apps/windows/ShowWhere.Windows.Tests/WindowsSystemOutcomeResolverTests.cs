using ShowWhere.Core;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class WindowsSystemOutcomeResolverTests
{
    [Fact]
    public void Connected_network_status_completes_after_the_user_opens_quick_settings()
    {
        var resolved = WindowsSystemOutcomeResolver.TryResolve(
            "인터넷 상태 확인하고 싶어",
            Candidate("Network ebluu.com Internet access"),
            out var message);

        Assert.True(resolved);
        Assert.Contains("연결된 상태", message);
    }

    [Fact]
    public void Disconnected_status_is_not_mistaken_for_connected_status()
    {
        var resolved = WindowsSystemOutcomeResolver.TryResolve(
            "check internet status",
            Candidate("Network No internet access"),
            out var message);

        Assert.True(resolved);
        Assert.Contains("no internet connection", message);
    }

    [Fact]
    public void Network_configuration_goal_continues_to_the_next_observation()
    {
        var resolved = WindowsSystemOutcomeResolver.TryResolve(
            "와이파이 설정을 바꾸고 싶어",
            Candidate("Network ebluu.com Internet access"),
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void Printer_goal_completes_only_after_printers_and_scanners_opens()
    {
        Assert.False(WindowsSystemOutcomeResolver.TryResolve(
            "프린터 연결은 어디서 확인해?",
            Candidate("Bluetooth & devices"),
            out _));

        var resolved = WindowsSystemOutcomeResolver.TryResolve(
            "프린터 연결은 어디서 확인해?",
            Candidate("Printers & scanners"),
            out var message);

        Assert.True(resolved);
        Assert.Contains("프린터 상태", message);
    }

    [Theory]
    [InlineData("계산기 어디야?", "Calculator", "계산기를 열었어요")]
    [InlineData("메모장을 열어줘", "Notepad", "메모장을 열었어요")]
    [InlineData("그림판 어디 있어?", "Paint", "그림판을 열었어요")]
    [InlineData("캡처 도구를 열고 싶어", "Snipping Tool", "캡처 도구를 열었어요")]
    public void Built_in_app_goal_completes_after_the_selected_app_opens(
        string goal,
        string label,
        string expectedMessage)
    {
        var resolved = WindowsSystemOutcomeResolver.TryResolve(goal, Candidate(label), out var message);

        Assert.True(resolved);
        Assert.Contains(expectedMessage, message);
    }

    private static UiCandidate Candidate(string label) => new(
        "network",
        label,
        null,
        "button",
        true,
        true,
        true,
        new UiBounds(10, 10, 30, 30));
}

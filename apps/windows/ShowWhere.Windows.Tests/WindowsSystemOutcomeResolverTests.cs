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

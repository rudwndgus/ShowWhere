using ShowWhere.Core;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class WindowsCandidatePrioritizerTests
{
    [Fact]
    public void Internet_goal_prioritizes_the_global_network_control()
    {
        var candidates = Enumerable.Range(0, 80)
            .Select(index => Candidate($"code-{index}", $"Visual Studio Code command {index}", "Code"))
            .Append(Candidate("network", "Network ebluu.com Internet access", "explorer"))
            .ToArray();

        var prioritized = WindowsCandidatePrioritizer.Prioritize("인터넷 상태 확인하고 싶어", candidates);

        Assert.Equal(40, prioritized.Count);
        Assert.Equal("network", prioritized[0].Id);
    }

    [Fact]
    public void Non_system_goal_preserves_the_complete_candidate_set()
    {
        var candidates = new[]
        {
            Candidate("new", "New Ticket", "chrome"),
            Candidate("mine", "My Tickets", "chrome"),
        };

        var prioritized = WindowsCandidatePrioritizer.Prioritize("내 티켓을 확인하고 싶어", candidates);

        Assert.Same(candidates, prioritized);
    }

    private static UiCandidate Candidate(string id, string label, string processName) => new(
        id,
        label,
        null,
        "button",
        true,
        true,
        true,
        new UiBounds(10, 10, 100, 30),
        new Dictionary<string, object?> { ["processName"] = processName });
}

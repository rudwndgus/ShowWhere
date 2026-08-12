using ShowWhere.Core;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class GoalCompletionResolverTests
{
    [Fact]
    public void Opening_youtube_music_completes_when_destination_page_is_current()
    {
        var completed = GoalCompletionResolver.TryResolve(
            "크롬에서 유튜브 뮤직 틀어줘",
            new ApplicationContext(Platforms.Windows, "chrome", "YouTube Music - Chrome"),
            [],
            out var message);

        Assert.True(completed);
        Assert.Contains("완료", message);
    }

    [Fact]
    public void Youtube_music_bookmark_on_new_tab_does_not_complete_the_goal()
    {
        var completed = GoalCompletionResolver.TryResolve(
            "유튜브 뮤직을 열어줘",
            new ApplicationContext(Platforms.Windows, "chrome", "새 탭 - Chrome"),
            [Candidate("YouTube Music")],
            out _);

        Assert.False(completed);
    }

    [Fact]
    public void Search_goal_does_not_complete_just_because_youtube_music_is_open()
    {
        var completed = GoalCompletionResolver.TryResolve(
            "유튜브 뮤직에서 아이유 노래 찾아줘",
            new ApplicationContext(Platforms.Windows, "chrome", "YouTube Music - Chrome"),
            [],
            out _);

        Assert.False(completed);
    }

    private static UiCandidate Candidate(string label) => new(
        "candidate", label, null, "link", true, true, true, new UiBounds(1, 1, 10, 10));
}

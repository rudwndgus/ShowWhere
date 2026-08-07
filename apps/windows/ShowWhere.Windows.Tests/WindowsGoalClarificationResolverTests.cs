using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class WindowsGoalClarificationResolverTests
{
    [Fact]
    public void Generic_photo_request_returns_selectable_sources()
    {
        var requiresChoice = WindowsGoalClarificationResolver.TryCreate("사진은 어디에서 봐?", out var prompt);

        Assert.True(requiresChoice);
        Assert.Equal(3, prompt.Choices.Count);
        Assert.Contains(prompt.Choices, choice => choice.Label.Contains("카메라"));
        Assert.Contains(prompt.Choices, choice => choice.Label.Contains("스크린샷"));
    }

    [Theory]
    [InlineData("카메라로 찍은 사진을 보고 싶어")]
    [InlineData("스크린샷을 보고 싶어")]
    [InlineData("다운로드한 사진을 찾아줘")]
    [InlineData("인터넷 상태를 확인하고 싶어")]
    public void Explicit_or_unrelated_request_does_not_interrupt_with_choices(string goal)
    {
        Assert.False(WindowsGoalClarificationResolver.TryCreate(goal, out _));
    }
}

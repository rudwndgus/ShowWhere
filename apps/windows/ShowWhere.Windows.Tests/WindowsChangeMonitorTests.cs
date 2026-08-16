using ShowWhere.Core;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class WindowsChangeMonitorTests
{
    [Theory]
    [InlineData(100, 200, true)]
    [InlineData(180, 240, true)]
    [InlineData(99, 220, false)]
    [InlineData(181, 220, false)]
    [InlineData(140, 241, false)]
    public void Target_interaction_requires_a_click_inside_the_highlighted_bounds(int x, int y, bool expected)
    {
        var bounds = new UiBounds(100, 200, 80, 40);

        var actual = WindowsChangeMonitor.Contains(bounds, new WindowsChangeMonitor.NativePoint(x, y));

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("menu", "latte-detail", true)]
    [InlineData("menu", "menu", false)]
    [InlineData("", "latte-detail", false)]
    public void Screen_transition_is_detected_even_when_the_mouse_click_event_was_missed(
        string baseline,
        string current,
        bool expected)
    {
        Assert.Equal(expected, WindowsChangeMonitor.HasMeaningfulScreenChange(baseline, current));
    }
}

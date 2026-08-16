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

    [Fact]
    public void Large_visual_change_inside_the_highlighted_control_advances_the_kiosk()
    {
        var baseline = Enumerable.Repeat((byte)240, 24 * 24 * 3).ToArray();
        var current = baseline.ToArray();
        for (var pixel = 0; pixel < 120; pixel++)
        {
            current[pixel * 3] = 30;
            current[pixel * 3 + 1] = 70;
            current[pixel * 3 + 2] = 110;
        }

        Assert.True(WindowsChangeMonitor.HasMeaningfulVisualChange(baseline, current));
    }

    [Fact]
    public void Tiny_rendering_noise_does_not_advance_the_kiosk()
    {
        var baseline = Enumerable.Repeat((byte)120, 24 * 24 * 3).ToArray();
        var current = baseline.Select((value, index) =>
            (byte)(index % 7 == 0 ? value + 2 : value)).ToArray();

        Assert.False(WindowsChangeMonitor.HasMeaningfulVisualChange(baseline, current));
    }

    [Fact]
    public void Kiosk_touch_requires_a_persistent_change_inside_the_selected_button()
    {
        var baseline = Enumerable.Repeat((byte)240, 24 * 24 * 3).ToArray();
        var changed = baseline.ToArray();
        for (var pixel = 0; pixel < 120; pixel++)
        {
            changed[pixel * 3] = 30;
            changed[pixel * 3 + 1] = 70;
            changed[pixel * 3 + 2] = 110;
        }
        var detector = new WindowsChangeMonitor.PersistentTargetVisualChangeDetector(baseline, 3);

        Assert.False(detector.Observe(changed));
        Assert.False(detector.Observe(changed));
        Assert.True(detector.Observe(changed));
    }

    [Fact]
    public void Kiosk_visual_confirmation_resets_when_the_target_returns_to_baseline()
    {
        var baseline = Enumerable.Repeat((byte)240, 24 * 24 * 3).ToArray();
        var changed = Enumerable.Repeat((byte)20, 24 * 24 * 3).ToArray();
        var detector = new WindowsChangeMonitor.PersistentTargetVisualChangeDetector(baseline, 3);

        Assert.False(detector.Observe(changed));
        Assert.False(detector.Observe(baseline));
        Assert.False(detector.Observe(changed));
        Assert.False(detector.Observe(changed));
        Assert.True(detector.Observe(changed));
    }
}

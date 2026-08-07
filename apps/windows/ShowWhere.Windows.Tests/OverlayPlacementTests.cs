using ShowWhere.Core;
using ShowWhere.Overlay;

namespace ShowWhere.Windows.Tests;

public sealed class OverlayPlacementTests
{
    [Fact]
    public void Tooltip_stays_inside_negative_coordinate_monitor_bounds()
    {
        var workingArea = new PhysicalRectangle(-1920, 0, 1920, 1040);
        var target = new UiBounds(-80, 980, 60, 35);

        var placement = OverlayPlacementCalculator.Calculate(target, workingArea);
        var absoluteTooltip = placement.Tooltip with
        {
            X = placement.Window.X + placement.Tooltip.X,
            Y = placement.Window.Y + placement.Tooltip.Y,
        };

        Assert.True(placement.TooltipAboveTarget);
        Assert.True(absoluteTooltip.X >= workingArea.X);
        Assert.True(absoluteTooltip.Right <= workingArea.Right);
        Assert.True(absoluteTooltip.Y >= workingArea.Y);
        Assert.True(absoluteTooltip.Bottom <= workingArea.Bottom);
    }

    [Fact]
    public void Taskbar_target_is_inside_the_monitor_even_when_outside_the_work_area()
    {
        var workArea = new PhysicalRectangle(0, 0, 1920, 1032);
        var monitorArea = new PhysicalRectangle(0, 0, 1920, 1080);
        var taskbarStart = new UiBounds(474, 1032, 45, 48);

        Assert.True(OverlayPlacementCalculator.IsOutside(taskbarStart, workArea));
        Assert.False(OverlayPlacementCalculator.IsOutside(taskbarStart, monitorArea));

        var placement = OverlayPlacementCalculator.Calculate(taskbarStart, monitorArea);
        var absoluteHighlightBottom = placement.Window.Y + placement.Highlight.Bottom;
        Assert.True(absoluteHighlightBottom <= monitorArea.Bottom);
    }
}

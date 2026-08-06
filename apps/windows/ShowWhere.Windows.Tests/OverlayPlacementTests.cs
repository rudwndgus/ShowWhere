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
}

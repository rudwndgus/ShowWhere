using ShowWhere.Core;

namespace ShowWhere.Overlay;

public sealed record PhysicalRectangle(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public sealed record OverlayPlacement(
    PhysicalRectangle Window,
    PhysicalRectangle Highlight,
    PhysicalRectangle Tooltip,
    bool TooltipAboveTarget);

public static class OverlayPlacementCalculator
{
    public static OverlayPlacement Calculate(
        UiBounds target,
        PhysicalRectangle workingArea,
        double tooltipWidth = 280,
        double tooltipHeight = 72)
    {
        const double padding = 8;
        const double gap = 14;
        var highlight = new PhysicalRectangle(
            target.X - padding,
            target.Y - padding,
            target.Width + padding * 2,
            target.Height + padding * 2);
        var placeAbove = highlight.Bottom + gap + tooltipHeight > workingArea.Bottom
            && highlight.Y - gap - tooltipHeight >= workingArea.Y;
        var tooltipY = placeAbove
            ? highlight.Y - gap - tooltipHeight
            : Math.Min(highlight.Bottom + gap, workingArea.Bottom - tooltipHeight);
        tooltipY = Math.Max(workingArea.Y, tooltipY);
        var tooltipX = Math.Clamp(
            highlight.X + highlight.Width / 2 - tooltipWidth / 2,
            workingArea.X,
            Math.Max(workingArea.X, workingArea.Right - tooltipWidth));
        var tooltip = new PhysicalRectangle(tooltipX, tooltipY, tooltipWidth, tooltipHeight);

        var windowX = Math.Min(highlight.X, tooltip.X);
        var windowY = Math.Min(highlight.Y, tooltip.Y);
        var windowRight = Math.Max(highlight.Right, tooltip.Right);
        var windowBottom = Math.Max(highlight.Bottom, tooltip.Bottom);
        var window = new PhysicalRectangle(windowX, windowY, windowRight - windowX, windowBottom - windowY);
        return new OverlayPlacement(
            window,
            highlight with { X = highlight.X - windowX, Y = highlight.Y - windowY },
            tooltip with { X = tooltip.X - windowX, Y = tooltip.Y - windowY },
            placeAbove);
    }
}

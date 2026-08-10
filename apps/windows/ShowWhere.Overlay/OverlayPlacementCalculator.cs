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

public sealed record NativeWindowPlacement(int X, int Y, int Width, int Height);

public static class OverlayPlacementCalculator
{
    public static NativeWindowPlacement ToNativeWindowPlacement(PhysicalRectangle rectangle)
    {
        var left = (int)Math.Floor(rectangle.X);
        var top = (int)Math.Floor(rectangle.Y);
        var right = (int)Math.Ceiling(rectangle.Right);
        var bottom = (int)Math.Ceiling(rectangle.Bottom);
        return new NativeWindowPlacement(
            left,
            top,
            Math.Max(1, right - left),
            Math.Max(1, bottom - top));
    }

    public static OverlayPlacement Calculate(
        UiBounds target,
        PhysicalRectangle workingArea,
        double tooltipWidth = 280,
        double tooltipHeight = 72)
    {
        const double padding = 8;
        const double gap = 14;
        var highlightX = Math.Max(workingArea.X, target.X - padding);
        var highlightY = Math.Max(workingArea.Y, target.Y - padding);
        var highlightRight = Math.Min(workingArea.Right, target.X + target.Width + padding);
        var highlightBottom = Math.Min(workingArea.Bottom, target.Y + target.Height + padding);
        var highlight = new PhysicalRectangle(
            highlightX,
            highlightY,
            Math.Max(1, highlightRight - highlightX),
            Math.Max(1, highlightBottom - highlightY));
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

    public static bool IsOutside(UiBounds target, PhysicalRectangle area) =>
        target.X >= area.Right || target.X + target.Width <= area.X
        || target.Y >= area.Bottom || target.Y + target.Height <= area.Y;
}

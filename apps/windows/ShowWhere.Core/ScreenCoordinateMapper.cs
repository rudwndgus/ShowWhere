namespace ShowWhere.Core;

/// <summary>
/// Converts between normalized screenshot coordinates and Windows physical pixels.
/// UI Automation, Win32 screen capture, cursor input, and overlay placement all use
/// the same physical coordinate space; WPF DIPs must never enter these calculations.
/// </summary>
public static class ScreenCoordinateMapper
{
    public static UiBounds MapVisualTarget(UiBounds screen, VisualTarget target)
    {
        EnsureValidScreen(screen);
        var width = Math.Min(screen.Width, Math.Max(8, target.Width * screen.Width));
        var height = Math.Min(screen.Height, Math.Max(8, target.Height * screen.Height));
        var x = Math.Clamp(
            screen.X + target.X * screen.Width,
            screen.X,
            screen.X + screen.Width - width);
        var y = Math.Clamp(
            screen.Y + target.Y * screen.Height,
            screen.Y,
            screen.Y + screen.Height - height);
        return new UiBounds(x, y, width, height);
    }

    public static VisualTarget NormalizeSelection(UiBounds screen, UiBounds selection, string label)
    {
        EnsureValidScreen(screen);
        var width = Math.Clamp(selection.Width / screen.Width, 0, 1);
        var height = Math.Clamp(selection.Height / screen.Height, 0, 1);
        var x = Math.Clamp((selection.X - screen.X) / screen.Width, 0, 1 - width);
        var y = Math.Clamp((selection.Y - screen.Y) / screen.Height, 0, 1 - height);
        return new VisualTarget(x, y, width, height, label);
    }

    private static void EnsureValidScreen(UiBounds screen)
    {
        if (screen.Width <= 0 || screen.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(screen), "Screen bounds must have a positive size.");
    }
}

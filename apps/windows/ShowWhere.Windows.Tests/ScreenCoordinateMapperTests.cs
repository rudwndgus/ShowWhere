using ShowWhere.Core;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class ScreenCoordinateMapperTests
{
    [Theory]
    [InlineData(0, 0, 1366, 768)]
    [InlineData(0, 0, 1920, 1080)]
    [InlineData(0, 0, 3840, 2160)]
    [InlineData(-1920, 0, 4480, 1440)]
    [InlineData(-2560, -1440, 6400, 3600)]
    public void Normalized_target_round_trips_across_resolutions_and_monitor_origins(
        double x, double y, double width, double height)
    {
        var screen = new UiBounds(x, y, width, height);
        var selection = new UiBounds(
            x + width * 0.713,
            y + height * 0.227,
            width * 0.061,
            height * 0.043);

        var normalized = ScreenCoordinateMapper.NormalizeSelection(screen, selection, "target");
        var mapped = ScreenCoordinateMapper.MapVisualTarget(screen, normalized);

        Assert.Equal(selection.X, mapped.X, 6);
        Assert.Equal(selection.Y, mapped.Y, 6);
        Assert.Equal(selection.Width, mapped.Width, 6);
        Assert.Equal(selection.Height, mapped.Height, 6);
    }

    [Theory]
    [InlineData(1366, 768, 1366, 768)]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(2560, 1440, 2048, 1152)]
    [InlineData(3840, 2160, 2048, 1152)]
    [InlineData(7680, 2160, 2048, 576)]
    [InlineData(2160, 3840, 648, 1152)]
    public void Screenshot_resize_preserves_aspect_ratio_with_more_readable_detail(
        int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(
            (expectedWidth, expectedHeight),
            WindowsScreenCaptureService.CalculateOutputSize(width, height));
    }

    [Fact]
    public void Tiny_visual_targets_are_clamped_inside_the_physical_screen()
    {
        var screen = new UiBounds(-1600, -900, 1600, 900);
        var mapped = ScreenCoordinateMapper.MapVisualTarget(
            screen,
            new VisualTarget(0.999, 0.999, 0.005, 0.005, "tiny"));

        Assert.True(mapped.Width >= 8);
        Assert.True(mapped.Height >= 8);
        Assert.InRange(mapped.X, screen.X, screen.X + screen.Width - mapped.Width);
        Assert.InRange(mapped.Y, screen.Y, screen.Y + screen.Height - mapped.Height);
    }
}

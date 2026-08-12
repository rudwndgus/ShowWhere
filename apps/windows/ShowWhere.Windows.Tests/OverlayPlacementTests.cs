using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ShowWhere.Core;
using ShowWhere.Overlay;

namespace ShowWhere.Windows.Tests;

public sealed class OverlayPlacementTests
{
    [Fact]
    public void Overlay_creates_a_visible_native_topmost_window_covering_the_target()
    {
        Exception? failure = null;
        var completed = false;
        var thread = new Thread(() =>
        {
            try
            {
                var target = new UiBounds(900, 260, 60, 70);
                var overlay = new HighlightOverlayWindow();
                overlay.ShowTarget(target, "설정을 누르세요.");
                var handle = new WindowInteropHelper(overlay).Handle;

                Assert.NotEqual(IntPtr.Zero, handle);
                Assert.True(IsWindowVisible(handle));
                Assert.True((GetWindowLongPtr(handle, GwlExStyle).ToInt64() & WsExTopmost) != 0);
                Assert.True(GetWindowRect(handle, out var rectangle));
                Assert.InRange(target.X + target.Width / 2, rectangle.Left, rectangle.Right);
                Assert.InRange(target.Y + target.Height / 2, rectangle.Top, rectangle.Bottom);
                overlay.Clear();
                overlay.Close();
                completed = true;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Overlay test thread timed out.");
        if (failure is not null) throw failure;
        Assert.True(completed);
    }

    [Fact]
    public void Overlay_reclaims_z_order_above_a_later_topmost_shell_surface()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? shellSurface = null;
            HighlightOverlayWindow? overlay = null;
            try
            {
                overlay = new HighlightOverlayWindow();
                overlay.ShowTarget(new UiBounds(400, 220, 80, 60), "표시 테스트");
                var overlayHandle = new WindowInteropHelper(overlay).Handle;

                shellSurface = new Window
                {
                    Width = 300,
                    Height = 240,
                    Left = 350,
                    Top = 180,
                    Topmost = true,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    ShowActivated = false,
                };
                shellSurface.Show();
                var shellHandle = new WindowInteropHelper(shellSurface).Handle;

                PumpDispatcher(TimeSpan.FromMilliseconds(350));

                Assert.True(ZOrderRank(overlayHandle) < ZOrderRank(shellHandle),
                    "The guidance overlay must remain above later topmost shell surfaces.");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                shellSurface?.Close();
                overlay?.Clear();
                overlay?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Overlay z-order test thread timed out.");
        if (failure is not null) throw failure;
    }

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

    [Theory]
    [InlineData(2419.25, 181.5, 503.25, 119.25, 2419, 181, 504, 120)]
    [InlineData(-1840.75, -910.25, 320.5, 88.5, -1841, -911, 321, 90)]
    public void Native_window_placement_preserves_absolute_physical_screen_coordinates(
        double x,
        double y,
        double width,
        double height,
        int expectedX,
        int expectedY,
        int expectedWidth,
        int expectedHeight)
    {
        var native = OverlayPlacementCalculator.ToNativeWindowPlacement(
            new PhysicalRectangle(x, y, width, height));

        Assert.Equal(expectedX, native.X);
        Assert.Equal(expectedY, native.Y);
        Assert.Equal(expectedWidth, native.Width);
        Assert.Equal(expectedHeight, native.Height);
    }

    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008;
    private const uint GwHwndNext = 2;

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static int ZOrderRank(IntPtr target)
    {
        var rank = 0;
        for (var window = GetTopWindow(IntPtr.Zero); window != IntPtr.Zero; window = GetWindow(window, GwHwndNext))
        {
            if (window == target) return rank;
            rank++;
        }
        return int.MaxValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr windowHandle, int index);

    private static IntPtr GetWindowLongPtr(IntPtr handle, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : GetWindowLong32(handle, index);
}

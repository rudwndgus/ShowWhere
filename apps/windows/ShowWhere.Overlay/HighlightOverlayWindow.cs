using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ShowWhere.Core;

namespace ShowWhere.Overlay;

public interface IHighlightOverlay
{
    void ShowTarget(UiBounds target, string message);
    void ShowScrollHint(UiBounds target, string message);
    void Clear();
}

public sealed class HighlightOverlayWindow : Window, IHighlightOverlay
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint MonitorDefaultToNearest = 2;
    private readonly Canvas _canvas = new();
    private readonly Border _highlightBorder;
    private readonly Border _tooltip;
    private readonly TextBlock _message;

    public HighlightOverlayWindow()
    {
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        _highlightBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 180, 0)),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(26, 255, 190, 0)),
        };
        _message = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _tooltip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(244, 28, 31, 38)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 180, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Child = _message,
        };
        _canvas.Children.Add(_highlightBorder);
        _canvas.Children.Add(_tooltip);
        Content = _canvas;
    }

    protected override void OnSourceInitialized(EventArgs eventArgs)
    {
        base.OnSourceInitialized(eventArgs);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        _ = SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
    }

    public void ShowTarget(UiBounds target, string message)
    {
        var monitorArea = MonitorUtilities.GetMonitorArea(target);
        if (OverlayPlacementCalculator.IsOutside(target, monitorArea))
        {
            ShowScrollHint(target, message);
            return;
        }
        var scale = MonitorUtilities.GetScale(target);
        _message.Text = message;
        var tooltipSize = MeasureTooltip(monitorArea, scale);
        var placement = OverlayPlacementCalculator.Calculate(
            target,
            monitorArea,
            tooltipSize.Width,
            tooltipSize.Height);
        Left = placement.Window.X / scale;
        Top = placement.Window.Y / scale;
        Width = placement.Window.Width / scale;
        Height = placement.Window.Height / scale;
        SetElementBounds(_highlightBorder, placement.Highlight, scale);
        SetElementBounds(_tooltip, placement.Tooltip, scale);
        _highlightBorder.Visibility = Visibility.Visible;
        if (!IsVisible) Show();
        Topmost = false;
        Topmost = true;
    }

    public void ShowScrollHint(UiBounds target, string message)
    {
        var workingArea = MonitorUtilities.GetWorkingArea(target);
        var monitorArea = MonitorUtilities.GetMonitorArea(target);
        var down = target.Y + target.Height / 2 >= monitorArea.Y + monitorArea.Height / 2;
        var scale = MonitorUtilities.GetScale(target);
        _message.Text = $"{(down ? "↓" : "↑")}  {(down ? "아래" : "위")}로 스크롤하세요\n{message}";
        var tooltipSize = MeasureTooltip(workingArea, scale);
        var left = workingArea.X + (workingArea.Width - tooltipSize.Width) / 2;
        var top = down
            ? workingArea.Bottom - tooltipSize.Height - 24 * scale
            : workingArea.Y + 24 * scale;
        Left = left / scale;
        Top = top / scale;
        Width = tooltipSize.Width / scale;
        Height = tooltipSize.Height / scale;
        _highlightBorder.Visibility = Visibility.Collapsed;
        Canvas.SetLeft(_tooltip, 0);
        Canvas.SetTop(_tooltip, 0);
        _tooltip.Width = tooltipSize.Width / scale;
        _tooltip.Height = tooltipSize.Height / scale;
        if (!IsVisible) Show();
        Topmost = false;
        Topmost = true;
    }

    public void Clear()
    {
        if (IsVisible) Hide();
        _message.Text = string.Empty;
    }

    private static void SetElementBounds(FrameworkElement element, PhysicalRectangle rectangle, double scale)
    {
        Canvas.SetLeft(element, rectangle.X / scale);
        Canvas.SetTop(element, rectangle.Y / scale);
        element.Width = rectangle.Width / scale;
        element.Height = rectangle.Height / scale;
    }

    private (double Width, double Height) MeasureTooltip(PhysicalRectangle availableArea, double scale)
    {
        var width = Math.Min(320 * scale, Math.Max(180 * scale, availableArea.Width - 16 * scale));
        var contentWidth = Math.Max(120, width / scale - 28);
        _message.Width = contentWidth;
        _message.Measure(new Size(contentWidth, double.PositiveInfinity));
        var heightInDips = Math.Clamp(_message.DesiredSize.Height + 20, 72, 180);
        return (width, heightInDips * scale);
    }

    private static class MonitorUtilities
    {
        public static PhysicalRectangle GetWorkingArea(UiBounds target)
        {
            var information = GetMonitorInformation(target);
            if (information is not null)
                return ToPhysicalRectangle(information.Value.Work);
            return VirtualScreenArea();
        }

        public static PhysicalRectangle GetMonitorArea(UiBounds target)
        {
            var information = GetMonitorInformation(target);
            if (information is not null)
                return ToPhysicalRectangle(information.Value.Monitor);
            return VirtualScreenArea();
        }

        private static MonitorInfo? GetMonitorInformation(UiBounds target)
        {
            var rectangle = ToNativeRectangle(target);
            var monitor = MonitorFromRect(ref rectangle, MonitorDefaultToNearest);
            var information = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref information))
                return information;
            return null;
        }

        private static PhysicalRectangle ToPhysicalRectangle(NativeRectangle rectangle) => new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);

        private static PhysicalRectangle VirtualScreenArea() => new(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

        public static double GetScale(UiBounds target)
        {
            var rectangle = ToNativeRectangle(target);
            var monitor = MonitorFromRect(ref rectangle, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0)
                return Math.Max(1, dpiX / 96d);
            return 1;
        }

        private static NativeRectangle ToNativeRectangle(UiBounds target) => new()
        {
            Left = (int)Math.Round(target.X),
            Top = (int)Math.Round(target.Y),
            Right = (int)Math.Round(target.X + target.Width),
            Bottom = (int)Math.Round(target.Y + target.Height),
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRectangle rectangle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo information);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr windowHandle, int index, IntPtr value);

    private static IntPtr GetWindowLongPtr(IntPtr handle, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : GetWindowLong32(handle, index);

    private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(handle, index, value) : SetWindowLong32(handle, index, value);
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    private const int WmNcHitTest = 0x0084;
    private const int HitTestClient = 1;
    private const int HitTestTransparent = -1;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const int SwShowNoActivate = 4;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private readonly Canvas _canvas = new();
    private readonly Border _highlightBorder;
    private readonly Border _tooltip;
    private readonly TextBlock _message;
    private readonly DispatcherTimer _visibilityTimer;
    private readonly TargetActivationSignal? _targetActivationSignal;
    private NativeWindowPlacement? _lastPlacement;
    private UiBounds? _activeTarget;
    private int _activationPending;
    private Point? _pendingMouseActivation;
    private Point? _pendingTouchActivation;

    public HighlightOverlayWindow(TargetActivationSignal? targetActivationSignal = null)
    {
        _targetActivationSignal = targetActivationSignal;
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
            BorderBrush = new SolidColorBrush(Color.FromRgb(202, 44, 52)),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(24, 202, 44, 52)),
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
            BorderBrush = new SolidColorBrush(Color.FromRgb(202, 44, 52)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Child = _message,
        };
        _canvas.Children.Add(_highlightBorder);
        _canvas.Children.Add(_tooltip);
        Content = _canvas;
        _visibilityTimer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMilliseconds(75),
        };
        _visibilityTimer.Tick += (_, _) => ReassertNativeTopmost();
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        PreviewTouchDown += OnPreviewTouchDown;
        PreviewTouchUp += OnPreviewTouchUp;
        Closed += (_, _) => _visibilityTimer.Stop();
    }

    protected override void OnSourceInitialized(EventArgs eventArgs)
    {
        base.OnSourceInitialized(eventArgs);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        var clickThroughStyle = _targetActivationSignal is null ? WsExTransparent : 0;
        var combinedStyle = (style & ~(long)WsExTransparent)
            | (long)clickThroughStyle | WsExToolWindow | WsExNoActivate;
        _ = SetWindowLongPtr(
            handle,
            GwlExStyle,
            new IntPtr(combinedStyle));
        if (HwndSource.FromHwnd(handle) is { } source) source.AddHook(WindowProcedure);
    }

    public void ShowTarget(UiBounds target, string message)
    {
        _activeTarget = target;
        Interlocked.Exchange(ref _activationPending, 0);
        _pendingMouseActivation = null;
        _pendingTouchActivation = null;
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
        PlaceWindow(placement.Window, scale);
        var windowScale = MonitorUtilities.GetWindowScale(new WindowInteropHelper(this).Handle, scale);
        if (Math.Abs(windowScale - scale) > 0.001)
        {
            scale = windowScale;
            tooltipSize = MeasureTooltip(monitorArea, scale);
            placement = OverlayPlacementCalculator.Calculate(
                target,
                monitorArea,
                tooltipSize.Width,
                tooltipSize.Height);
            PlaceWindow(placement.Window, scale);
        }
        SetElementBounds(_highlightBorder, placement.Highlight, scale);
        SetElementBounds(_tooltip, placement.Tooltip, scale);
        _highlightBorder.Visibility = Visibility.Visible;
        UpdateLayout();
        StartVisibilityGuard();
    }

    public void ShowScrollHint(UiBounds target, string message)
    {
        _activeTarget = null;
        var workingArea = MonitorUtilities.GetWorkingArea(target);
        var monitorArea = MonitorUtilities.GetMonitorArea(target);
        var down = target.Y + target.Height / 2 >= monitorArea.Y + monitorArea.Height / 2;
        var scale = MonitorUtilities.GetScale(target);
        _message.Text = $"{(down ? "↓" : "↑")}  Scroll {(down ? "down" : "up")}\n{message}";
        var tooltipSize = MeasureTooltip(workingArea, scale);
        var left = workingArea.X + (workingArea.Width - tooltipSize.Width) / 2;
        var top = down
            ? workingArea.Bottom - tooltipSize.Height - 24 * scale
            : workingArea.Y + 24 * scale;
        var window = new PhysicalRectangle(left, top, tooltipSize.Width, tooltipSize.Height);
        PlaceWindow(window, scale);
        var windowScale = MonitorUtilities.GetWindowScale(new WindowInteropHelper(this).Handle, scale);
        if (Math.Abs(windowScale - scale) > 0.001)
        {
            scale = windowScale;
            tooltipSize = MeasureTooltip(workingArea, scale);
            left = workingArea.X + (workingArea.Width - tooltipSize.Width) / 2;
            top = down
                ? workingArea.Bottom - tooltipSize.Height - 24 * scale
                : workingArea.Y + 24 * scale;
            window = new PhysicalRectangle(left, top, tooltipSize.Width, tooltipSize.Height);
            PlaceWindow(window, scale);
        }
        _highlightBorder.Visibility = Visibility.Collapsed;
        Canvas.SetLeft(_tooltip, 0);
        Canvas.SetTop(_tooltip, 0);
        _tooltip.Width = tooltipSize.Width / scale;
        _tooltip.Height = tooltipSize.Height / scale;
        UpdateLayout();
        StartVisibilityGuard();
    }

    public void Clear()
    {
        _visibilityTimer.Stop();
        _lastPlacement = null;
        _activeTarget = null;
        _pendingMouseActivation = null;
        _pendingTouchActivation = null;
        if (IsVisible) Hide();
        _message.Text = string.Empty;
    }

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmNcHitTest || _targetActivationSignal is null) return IntPtr.Zero;
        var packed = lParam.ToInt64();
        var x = unchecked((short)(packed & 0xffff));
        var y = unchecked((short)((packed >> 16) & 0xffff));
        handled = true;
        return IsInsideActiveTarget(x, y)
            ? new IntPtr(HitTestClient)
            : new IntPtr(HitTestTransparent);
    }

    private void OnPreviewMouseLeftButtonDown(object? sender, MouseButtonEventArgs eventArgs)
    {
        if (_pendingTouchActivation is not null) return;
        var point = PointToScreen(eventArgs.GetPosition(this));
        if (!ReserveTarget(point.X, point.Y)) return;
        _pendingMouseActivation = point;
        _ = Mouse.Capture(this, CaptureMode.Element);
        eventArgs.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object? sender, MouseButtonEventArgs eventArgs)
    {
        if (_pendingMouseActivation is not { } point) return;
        _pendingMouseActivation = null;
        if (Mouse.Captured == this) Mouse.Capture(null);
        CompleteTargetActivation(point.X, point.Y);
        eventArgs.Handled = true;
    }

    private void OnPreviewTouchDown(object? sender, TouchEventArgs eventArgs)
    {
        var point = PointToScreen(eventArgs.GetTouchPoint(this).Position);
        if (!ReserveTarget(point.X, point.Y)) return;
        _pendingTouchActivation = point;
        _ = eventArgs.TouchDevice.Capture(this, CaptureMode.Element);
        eventArgs.Handled = true;
    }

    private void OnPreviewTouchUp(object? sender, TouchEventArgs eventArgs)
    {
        if (_pendingTouchActivation is not { } point) return;
        _pendingTouchActivation = null;
        eventArgs.TouchDevice.Capture(null);
        CompleteTargetActivation(point.X, point.Y);
        eventArgs.Handled = true;
    }

    private bool ReserveTarget(double x, double y)
    {
        if (_targetActivationSignal is null || !IsInsideActiveTarget(x, y)) return false;
        return Interlocked.Exchange(ref _activationPending, 1) == 0;
    }

    private void CompleteTargetActivation(double x, double y)
    {
        _targetActivationSignal?.Record(x, y);
        Clear();
        ForwardActivationToKioskAfterRelease(x, y);
    }

    private bool IsInsideActiveTarget(double x, double y) => _activeTarget is { } target
        && x >= target.X && x <= target.X + target.Width
        && y >= target.Y && y <= target.Y + target.Height;

    private async void ForwardActivationToKioskAfterRelease(double x, double y)
    {
        // Let Windows finish the physical touch/mouse release and remove this
        // HWND from hit testing before delivering the one relayed activation.
        await Task.Delay(TimeSpan.FromMilliseconds(80));
        _ = SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
    }

    private static void SetElementBounds(FrameworkElement element, PhysicalRectangle rectangle, double scale)
    {
        Canvas.SetLeft(element, rectangle.X / scale);
        Canvas.SetTop(element, rectangle.Y / scale);
        element.Width = rectangle.Width / scale;
        element.Height = rectangle.Height / scale;
    }

    private void PlaceWindow(PhysicalRectangle rectangle, double scale)
    {
        // UI Automation bounds and Win32 monitor rectangles are physical pixels. WPF's
        // Left/Top are DIPs whose virtual-screen origin changes with each monitor's DPI,
        // so dividing absolute coordinates by a scale shifts mixed-DPI secondary monitors.
        // Keep only the WPF size in DIPs and position the HWND in physical pixels.
        Width = Math.Max(1, rectangle.Width / scale);
        Height = Math.Max(1, rectangle.Height / scale);
        if (!IsVisible) Show();

        var handle = new WindowInteropHelper(this).Handle;
        var native = OverlayPlacementCalculator.ToNativeWindowPlacement(rectangle);
        _lastPlacement = native;
        _ = ShowWindow(handle, SwShowNoActivate);
        _ = SetWindowPos(
            handle,
            HwndTopmost,
            native.X,
            native.Y,
            native.Width,
            native.Height,
            SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder);
    }

    private void StartVisibilityGuard()
    {
        ReassertNativeTopmost();
        if (!_visibilityTimer.IsEnabled) _visibilityTimer.Start();
    }

    private void ReassertNativeTopmost()
    {
        if (!IsVisible || _lastPlacement is not { } native) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        _ = ShowWindow(handle, SwShowNoActivate);
        _ = SetWindowPos(
            handle,
            HwndTopmost,
            native.X,
            native.Y,
            native.Width,
            native.Height,
            SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder);
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
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen),
            GetSystemMetrics(SmCxVirtualScreen),
            GetSystemMetrics(SmCyVirtualScreen));

        public static double GetScale(UiBounds target)
        {
            var rectangle = ToNativeRectangle(target);
            var monitor = MonitorFromRect(ref rectangle, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0)
                return Math.Max(1, dpiX / 96d);
            return 1;
        }

        public static double GetWindowScale(IntPtr windowHandle, double fallback)
        {
            if (windowHandle == IntPtr.Zero) return fallback;
            var dpi = GetDpiForWindow(windowHandle);
            return dpi == 0 ? fallback : Math.Max(1, dpi / 96d);
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

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint flags,
        uint deltaX,
        uint deltaY,
        uint data,
        UIntPtr extraInformation);

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

    private static readonly IntPtr HwndTopmost = new(-1);
}

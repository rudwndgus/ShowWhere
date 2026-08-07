using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ShowWhere.Desktop;

public partial class FloatingAssistantWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int ScreenMargin = 8;
    private readonly GuidancePanelWindow _panel;
    private readonly AssistantPositionStore _positionStore;
    private readonly Action _rememberForegroundWindow;
    private Point? _mouseDownPosition;
    private bool _dragging;

    public FloatingAssistantWindow(
        GuidancePanelWindow panel,
        AssistantPositionStore positionStore,
        Action rememberForegroundWindow)
    {
        _panel = panel;
        _positionStore = positionStore;
        _rememberForegroundWindow = rememberForegroundWindow;
        InitializeComponent();
        var saved = _positionStore.Load();
        Left = saved?.Left ?? SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width - 24;
        Top = saved?.Top ?? SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight / 2 - Height / 2;
    }

    protected override void OnSourceInitialized(EventArgs eventArgs)
    {
        base.OnSourceInitialized(eventArgs);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        _ = SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExToolWindow | WsExNoActivate));
        ConstrainToNearestMonitor(handle);
        Dispatcher.BeginInvoke(() => _positionStore.Save(Left, Top));
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        _mouseDownPosition = eventArgs.GetPosition(this);
        _dragging = false;
        CaptureMouse();
        eventArgs.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (_mouseDownPosition is null || eventArgs.LeftButton != MouseButtonState.Pressed || _dragging) return;
        var current = eventArgs.GetPosition(this);
        var delta = current - _mouseDownPosition.Value;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 8) return;

        _dragging = true;
        ReleaseMouseCapture();
        try { DragMove(); } catch (InvalidOperationException) { }
        SnapAndConstrain();
        _positionStore.Save(Left, Top);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_mouseDownPosition is null) return;
        ReleaseMouseCapture();
        if (!_dragging) TogglePanel();
        _mouseDownPosition = null;
        _dragging = false;
        eventArgs.Handled = true;
    }

    private void TogglePanel()
    {
        if (_panel.IsVisible)
        {
            _panel.Hide();
            return;
        }
        if (_panel.WindowState == WindowState.Minimized) _panel.WindowState = WindowState.Normal;
        _rememberForegroundWindow();
        _panel.PositionNear(Left, Top, Width, Height);
        _panel.Show();
        _panel.Activate();
        _panel.FocusGoalInput();
    }

    private void SnapAndConstrain()
    {
        var leftEdge = SystemParameters.VirtualScreenLeft;
        var topEdge = SystemParameters.VirtualScreenTop;
        var rightEdge = leftEdge + SystemParameters.VirtualScreenWidth - Width;
        var bottomEdge = topEdge + SystemParameters.VirtualScreenHeight - Height;
        Left = Math.Clamp(Left, leftEdge, rightEdge);
        Top = Math.Clamp(Top, topEdge, bottomEdge);
        if (Math.Abs(Left - leftEdge) < 22) Left = leftEdge + 8;
        if (Math.Abs(Left - rightEdge) < 22) Left = rightEdge - 8;
        ConstrainToNearestMonitor(new WindowInteropHelper(this).Handle);
    }

    private static void ConstrainToNearestMonitor(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var windowRect)) return;

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo)) return;

        var width = windowRect.Right - windowRect.Left;
        var height = windowRect.Bottom - windowRect.Top;
        var minimumLeft = monitorInfo.WorkArea.Left + ScreenMargin;
        var maximumLeft = Math.Max(minimumLeft, monitorInfo.WorkArea.Right - width - ScreenMargin);
        var minimumTop = monitorInfo.WorkArea.Top + ScreenMargin;
        var maximumTop = Math.Max(minimumTop, monitorInfo.WorkArea.Bottom - height - ScreenMargin);
        var left = Math.Clamp(windowRect.Left, minimumLeft, maximumLeft);
        var top = Math.Clamp(windowRect.Top, minimumTop, maximumTop);

        if (left == windowRect.Left && top == windowRect.Top) return;
        _ = SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
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
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr windowHandle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr windowHandle, int index, IntPtr value);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
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
    private static IntPtr GetWindowLongPtr(IntPtr handle, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : GetWindowLong32(handle, index);
    private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(handle, index, value) : SetWindowLong32(handle, index, value);
}

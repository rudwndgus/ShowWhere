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
    private readonly GuidancePanelWindow _panel;
    private readonly AssistantPositionStore _positionStore;

    public FloatingAssistantWindow(GuidancePanelWindow panel, AssistantPositionStore positionStore)
    {
        _panel = panel;
        _positionStore = positionStore;
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
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        var before = new Point(Left, Top);
        try { DragMove(); } catch (InvalidOperationException) { }
        SnapAndConstrain();
        _positionStore.Save(Left, Top);
        var moved = Math.Abs(Left - before.X) + Math.Abs(Top - before.Y) > 5;
        if (!moved) TogglePanel();
    }

    private void TogglePanel()
    {
        if (_panel.IsVisible)
        {
            _panel.Hide();
            return;
        }
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
    }

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

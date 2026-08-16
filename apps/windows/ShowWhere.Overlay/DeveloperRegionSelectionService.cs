using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ShowWhere.Core;

namespace ShowWhere.Overlay;

public interface ICorrectionSelectionService
{
    Task<UiBounds?> SelectRegionAsync(CancellationToken cancellationToken = default);
}

public sealed class DeveloperRegionSelectionService : ICorrectionSelectionService
{
    public Task<UiBounds?> SelectRegionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new RegionSelectionWindow();
        using var registration = cancellationToken.Register(() => window.Dispatcher.BeginInvoke(window.Cancel));
        _ = window.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(window.Selection);
    }
}

internal sealed class RegionSelectionWindow : Window
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private readonly Canvas _canvas = new();
    private readonly Border _selectionBorder;
    private NativePoint? _start;

    public RegionSelectionWindow()
    {
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(52, 0, 0, 0));
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _selectionBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 202, 44)),
            BorderThickness = new Thickness(3),
            Background = new SolidColorBrush(Color.FromArgb(35, 255, 202, 44)),
            Visibility = Visibility.Collapsed,
        };
        var instruction = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(238, 28, 31, 38)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 202, 44)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 11, 16, 11),
            Child = new TextBlock
            {
                Text = "Drag over the correct button · Press Esc or right-click to cancel",
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
            },
        };
        Canvas.SetLeft(instruction, 24);
        Canvas.SetTop(instruction, 24);
        _canvas.Children.Add(_selectionBorder);
        _canvas.Children.Add(instruction);
        Content = _canvas;

        PreviewMouseLeftButtonDown += OnMouseDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseUp;
        PreviewMouseRightButtonDown += (_, eventArgs) => { eventArgs.Handled = true; Cancel(); };
        PreviewKeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Key.Escape) return;
            eventArgs.Handled = true;
            Cancel();
        };
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            Mouse.Capture(this);
        };
    }

    public UiBounds? Selection { get; private set; }

    public void Cancel()
    {
        Selection = null;
        Mouse.Capture(null);
        DialogResult = false;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!TryGetCursorPosition(out var point)) return;
        _start = point;
        _selectionBorder.Visibility = Visibility.Visible;
        UpdateSelectionVisual(point, point);
        eventArgs.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (_start is null || eventArgs.LeftButton != MouseButtonState.Pressed) return;
        if (TryGetCursorPosition(out var point)) UpdateSelectionVisual(_start.Value, point);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_start is null || !TryGetCursorPosition(out var end)) return;
        var start = _start.Value;
        _start = null;
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);
        if (width < 6 || height < 6)
        {
            _selectionBorder.Visibility = Visibility.Collapsed;
            return;
        }
        Selection = new UiBounds(x, y, width, height);
        Mouse.Capture(null);
        DialogResult = true;
        eventArgs.Handled = true;
    }

    private void UpdateSelectionVisual(NativePoint start, NativePoint end)
    {
        var startDip = PointFromScreen(new Point(start.X, start.Y));
        var endDip = PointFromScreen(new Point(end.X, end.Y));
        Canvas.SetLeft(_selectionBorder, Math.Min(startDip.X, endDip.X));
        Canvas.SetTop(_selectionBorder, Math.Min(startDip.Y, endDip.Y));
        _selectionBorder.Width = Math.Abs(endDip.X - startDip.X);
        _selectionBorder.Height = Math.Abs(endDip.Y - startDip.Y);
    }

    protected override void OnSourceInitialized(EventArgs eventArgs)
    {
        base.OnSourceInitialized(eventArgs);
        var handle = new WindowInteropHelper(this).Handle;
        _ = SetWindowPos(
            handle,
            new IntPtr(-1),
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen),
            GetSystemMetrics(SmCxVirtualScreen),
            GetSystemMetrics(SmCyVirtualScreen),
            0x0040);
    }

    private static bool TryGetCursorPosition(out NativePoint point) => GetCursorPos(out point);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public NativePoint(int x, int y) { X = x; Y = y; }
        public int X { get; }
        public int Y { get; }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

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
}

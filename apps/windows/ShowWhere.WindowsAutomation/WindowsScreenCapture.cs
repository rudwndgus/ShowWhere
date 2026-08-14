using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShowWhere.Core;

namespace ShowWhere.WindowsAutomation;

public sealed record WindowsScreenCapture(string DataUrl, UiBounds Bounds);

public interface IWindowsScreenCaptureService
{
    Task<WindowsScreenCapture> CaptureAsync(CancellationToken cancellationToken);
}

public sealed class WindowsScreenCaptureService : IWindowsScreenCaptureService
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int SrcCopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;
    private const int MaximumImageWidth = 2048;
    private const int MaximumImageHeight = 1152;

    public Task<WindowsScreenCapture> CaptureAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private static WindowsScreenCapture Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var x = GetSystemMetrics(SmXVirtualScreen);
        var y = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("The Windows screen is unavailable.");

        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = CreateCompatibleBitmap(screenDc, width, height);
        var previous = SelectObject(memoryDc, bitmapHandle);
        try
        {
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SrcCopy | CaptureBlt))
                throw new InvalidOperationException("The Windows screen could not be captured.");

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            var cached = new CachedBitmap(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            cached.Freeze();
            cancellationToken.ThrowIfCancellationRequested();

            BitmapSource output = cached;
            var outputSize = CalculateOutputSize(width, height);
            if (outputSize.Width != width || outputSize.Height != height)
            {
                var transformed = new TransformedBitmap(cached, new ScaleTransform(
                    outputSize.Width / (double)width,
                    outputSize.Height / (double)height));
                transformed.Freeze();
                output = transformed;
            }

            var encoder = new JpegBitmapEncoder { QualityLevel = 78 };
            encoder.Frames.Add(BitmapFrame.Create(output));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return new WindowsScreenCapture(
                $"data:image/jpeg;base64,{Convert.ToBase64String(stream.ToArray())}",
                new UiBounds(x, y, width, height));
        }
        finally
        {
            _ = SelectObject(memoryDc, previous);
            _ = DeleteObject(bitmapHandle);
            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    internal static (int Width, int Height) CalculateOutputSize(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var scale = Math.Min(1d, Math.Min(
            MaximumImageWidth / (double)width,
            MaximumImageHeight / (double)height));
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr graphicsObject);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr source,
        int sourceX,
        int sourceY,
        int operation);
}

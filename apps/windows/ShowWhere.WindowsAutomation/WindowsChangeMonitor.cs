using System.Runtime.InteropServices;
using ShowWhere.Core;

namespace ShowWhere.WindowsAutomation;

public interface IWindowsChangeMonitor
{
    Task<WindowsObservation?> WaitForTargetInteractionAsync(
        UiBounds targetBounds,
        string? goal,
        string? baselineSnapshotHash,
        TimeSpan maximumWait,
        bool requireExplicitTargetClick,
        CancellationToken cancellationToken);
}

public sealed class WindowsChangeMonitor : IWindowsChangeMonitor
{
    private const int FingerprintWidth = 24;
    private const int FingerprintHeight = 24;
    private const int SrcCopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;
    private const int DibRgbColors = 0;
    private const uint BiRgb = 0;
    private readonly IWindowsUiObserver _observer;
    private readonly Action<string>? _interactionDiagnostic;

    public WindowsChangeMonitor(
        IWindowsUiObserver observer,
        Action<string>? interactionDiagnostic = null)
    {
        _observer = observer;
        _interactionDiagnostic = interactionDiagnostic;
    }

    public async Task<WindowsObservation?> WaitForTargetInteractionAsync(
        UiBounds targetBounds,
        string? goal,
        string? baselineSnapshotHash,
        TimeSpan maximumWait,
        bool requireExplicitTargetClick,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(maximumWait);

        try
        {
            _interactionDiagnostic?.Invoke("monitor_started");
            using var lowLevelClicks = new LowLevelTargetClickTracker(targetBounds);
            using var dedicatedClickPoller = new DedicatedTargetClickPoller(targetBounds, timeout.Token);
            _ = GetAsyncKeyState(VirtualKeyLeftButton);
            var wasPressed = false;
            var nextScreenCheck = DateTimeOffset.UtcNow.AddMilliseconds(160);
            var nextVisualCheck = DateTimeOffset.UtcNow.AddMilliseconds(
                requireExplicitTargetClick ? 180 : 110);
            var baselineVisualFingerprint = TryCaptureVisualFingerprint(targetBounds);
            var persistentVisualChange = baselineVisualFingerprint is null
                ? null
                : new PersistentTargetVisualChangeDetector(
                    baselineVisualFingerprint,
                    requireExplicitTargetClick ? 3 : 1);
            var baselineInputTick = TryGetLastInputTick();

            while (!timeout.IsCancellationRequested)
            {
                var buttonState = GetAsyncKeyState(VirtualKeyLeftButton);
                var isPressed = (buttonState & KeyPressedMask) != 0;
                var wasClicked = (isPressed && !wasPressed) || (buttonState & KeyClickedMask) != 0;
                wasPressed = isPressed;

                var hookClick = lowLevelClicks.ConsumeClick();
                var dedicatedClick = dedicatedClickPoller.ConsumeClick();
                var polledClick = wasClicked && GetCursorPos(out var cursor) && Contains(targetBounds, cursor);
                if (hookClick || dedicatedClick || polledClick)
                {
                    _interactionDiagnostic?.Invoke(
                        hookClick ? "low_level_click_inside_target"
                        : dedicatedClick ? "dedicated_click_inside_target"
                        : "polled_click_inside_target");
                    await Task.Delay(TimeSpan.FromMilliseconds(650), timeout.Token).ConfigureAwait(false);
                    return await ObserveAfterInteractionAsync(goal, timeout.Token).ConfigureAwait(false);
                }

                if (persistentVisualChange is not null
                    && DateTimeOffset.UtcNow >= nextVisualCheck)
                {
                    nextVisualCheck = DateTimeOffset.UtcNow.AddMilliseconds(110);
                    var currentVisualFingerprint = TryCaptureVisualFingerprint(targetBounds);
                    var visualChangeConfirmed = currentVisualFingerprint is not null
                        && persistentVisualChange.Observe(currentVisualFingerprint);
                    if (visualChangeConfirmed
                        && CanConfirmVisualInteraction(
                            requireExplicitTargetClick,
                            baselineInputTick,
                            TryGetLastInputTick(),
                            unchecked((uint)Environment.TickCount)))
                    {
                        // Touch is delivered straight through the input-transparent overlay.
                        // Canvas kiosks often expose no touch/mouse event to another process,
                        // so confirm the physical activation from a persistent visual outcome
                        // inside this exact target only. Animated banners elsewhere are ignored.
                        _interactionDiagnostic?.Invoke(
                            requireExplicitTargetClick
                                ? "persistent_target_visual_change"
                                : "visual_target_change");
                        await Task.Delay(TimeSpan.FromMilliseconds(300), timeout.Token).ConfigureAwait(false);
                        return await ObserveAfterInteractionAsync(goal, timeout.Token).ConfigureAwait(false);
                    }
                    if (visualChangeConfirmed && requireExplicitTargetClick)
                    {
                        // A changing/hovering control without fresh user input must never
                        // advance the deterministic kiosk flow. Start confirmation over.
                        persistentVisualChange.Reset();
                    }
                }

                if (!requireExplicitTargetClick
                    && !string.IsNullOrWhiteSpace(baselineSnapshotHash)
                    && DateTimeOffset.UtcNow >= nextScreenCheck)
                {
                    nextScreenCheck = DateTimeOffset.UtcNow.AddMilliseconds(280);
                    try
                    {
                        var observation = await _observer.ObserveAsync(goal, timeout.Token).ConfigureAwait(false);
                        if (HasMeaningfulScreenChange(baselineSnapshotHash, observation.SnapshotHash))
                        {
                            _interactionDiagnostic?.Invoke("uia_snapshot_change");
                            return observation;
                        }
                    }
                    catch (WindowsObservationException)
                    {
                        // The kiosk can briefly remove its accessibility tree while navigating.
                        // Keep polling until the new screen becomes observable.
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(12), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _interactionDiagnostic?.Invoke("monitor_timeout");
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private async Task<WindowsObservation> ObserveAfterInteractionAsync(
        string? goal,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                return await _observer.ObserveAsync(goal, cancellationToken).ConfigureAwait(false);
            }
            catch (WindowsObservationException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new WindowsObservationException();
    }

    internal static bool Contains(UiBounds bounds, NativePoint point) =>
        point.X >= bounds.X && point.X <= bounds.X + bounds.Width
        && point.Y >= bounds.Y && point.Y <= bounds.Y + bounds.Height;

    internal static bool HasMeaningfulScreenChange(string? baselineSnapshotHash, string? currentSnapshotHash) =>
        !string.IsNullOrWhiteSpace(baselineSnapshotHash)
        && !string.IsNullOrWhiteSpace(currentSnapshotHash)
        && !string.Equals(baselineSnapshotHash, currentSnapshotHash, StringComparison.Ordinal);

    internal static bool HasMeaningfulVisualChange(byte[] baseline, byte[] current)
    {
        if (baseline.Length == 0 || baseline.Length != current.Length || baseline.Length % 3 != 0)
            return false;

        var changedPixels = 0;
        long totalDifference = 0;
        var pixelCount = baseline.Length / 3;
        for (var offset = 0; offset < baseline.Length; offset += 3)
        {
            var difference = (
                Math.Abs(baseline[offset] - current[offset])
                + Math.Abs(baseline[offset + 1] - current[offset + 1])
                + Math.Abs(baseline[offset + 2] - current[offset + 2])) / 3;
            totalDifference += difference;
            if (difference >= 22) changedPixels++;
        }

        return changedPixels >= Math.Max(12, (int)Math.Ceiling(pixelCount * 0.12))
            && totalDifference / (double)pixelCount >= 8;
    }

    internal static bool CanConfirmVisualInteraction(
        bool requireExplicitTargetClick,
        uint? baselineInputTick,
        uint? lastInputTick,
        uint currentTick,
        uint maximumInputAgeMilliseconds = 1_500)
    {
        if (!requireExplicitTargetClick) return true;
        if (baselineInputTick is null || lastInputTick is null) return false;
        if (lastInputTick.Value == baselineInputTick.Value) return false;
        return unchecked(currentTick - lastInputTick.Value) <= maximumInputAgeMilliseconds;
    }

    private static uint? TryGetLastInputTick()
    {
        var information = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref information) ? information.Time : null;
    }

    internal sealed class PersistentTargetVisualChangeDetector
    {
        private readonly byte[] _baseline;
        private readonly int _requiredConsecutiveSamples;
        private int _consecutiveChangedSamples;

        public PersistentTargetVisualChangeDetector(byte[] baseline, int requiredConsecutiveSamples)
        {
            _baseline = baseline;
            _requiredConsecutiveSamples = Math.Max(1, requiredConsecutiveSamples);
        }

        public bool Observe(byte[] current)
        {
            if (HasMeaningfulVisualChange(_baseline, current))
                _consecutiveChangedSamples++;
            else
                _consecutiveChangedSamples = 0;

            return _consecutiveChangedSamples >= _requiredConsecutiveSamples;
        }

        public void Reset() => _consecutiveChangedSamples = 0;
    }

    private static byte[]? TryCaptureVisualFingerprint(UiBounds targetBounds)
    {
        // Do not sample the red outline itself. The inset keeps the fingerprint on
        // the real kiosk control content and avoids treating overlay redraws as input.
        var insetX = targetBounds.Width >= 24 ? Math.Min(8d, targetBounds.Width * 0.08) : 0;
        var insetY = targetBounds.Height >= 24 ? Math.Min(8d, targetBounds.Height * 0.08) : 0;
        var sourceX = (int)Math.Floor(targetBounds.X + insetX);
        var sourceY = (int)Math.Floor(targetBounds.Y + insetY);
        var sourceWidth = Math.Max(2, (int)Math.Ceiling(targetBounds.Width - insetX * 2));
        var sourceHeight = Math.Max(2, (int)Math.Ceiling(targetBounds.Height - insetY * 2));
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, FingerprintWidth, FingerprintHeight);
        if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
        {
            if (bitmap != IntPtr.Zero) _ = DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) _ = DeleteDC(memoryDc);
            _ = ReleaseDC(IntPtr.Zero, screenDc);
            return null;
        }

        var previous = SelectObject(memoryDc, bitmap);
        try
        {
            _ = SetStretchBltMode(memoryDc, 4); // HALFTONE
            if (!StretchBlt(
                    memoryDc, 0, 0, FingerprintWidth, FingerprintHeight,
                    screenDc, sourceX, sourceY, sourceWidth, sourceHeight,
                    SrcCopy | CaptureBlt)) return null;

            var pixels = new byte[FingerprintWidth * FingerprintHeight * 4];
            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = FingerprintWidth,
                    Height = -FingerprintHeight,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                },
            };
            if (GetDIBits(
                    memoryDc, bitmap, 0, FingerprintHeight, pixels,
                    ref bitmapInfo, DibRgbColors) == 0) return null;

            var fingerprint = new byte[FingerprintWidth * FingerprintHeight * 3];
            for (int sourceOffset = 0, targetOffset = 0;
                 sourceOffset < pixels.Length;
                 sourceOffset += 4, targetOffset += 3)
            {
                fingerprint[targetOffset] = pixels[sourceOffset + 2];
                fingerprint[targetOffset + 1] = pixels[sourceOffset + 1];
                fingerprint[targetOffset + 2] = pixels[sourceOffset];
            }
            return fingerprint;
        }
        finally
        {
            _ = SelectObject(memoryDc, previous);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private sealed class LowLevelTargetClickTracker : IDisposable
    {
        private readonly UiBounds _targetBounds;
        private readonly LowLevelMouseProcedure _procedure;
        private IntPtr _hook;
        private int _clicked;

        public LowLevelTargetClickTracker(UiBounds targetBounds)
        {
            _targetBounds = targetBounds;
            _procedure = HandleMouseEvent;
            _hook = SetWindowsHookEx(
                LowLevelMouseHook,
                _procedure,
                GetModuleHandle(null),
                0);
        }

        public bool ConsumeClick() => Interlocked.Exchange(ref _clicked, 0) != 0;

        private IntPtr HandleMouseEvent(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && message == new IntPtr(LeftButtonDown))
            {
                var mouse = Marshal.PtrToStructure<LowLevelMouseData>(data);
                if (Contains(_targetBounds, mouse.Point))
                    Interlocked.Exchange(ref _clicked, 1);
            }
            return CallNextHookEx(_hook, code, message, data);
        }

        public void Dispose()
        {
            if (_hook == IntPtr.Zero) return;
            _ = UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private sealed class DedicatedTargetClickPoller : IDisposable
    {
        private readonly UiBounds _targetBounds;
        private readonly CancellationTokenSource _cancellation;
        private readonly Task _worker;
        private int _clicked;

        public DedicatedTargetClickPoller(UiBounds targetBounds, CancellationToken cancellationToken)
        {
            _targetBounds = targetBounds;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = Task.Factory.StartNew(
                Poll,
                _cancellation.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        public bool ConsumeClick() => Interlocked.Exchange(ref _clicked, 0) != 0;

        private void Poll()
        {
            _ = GetAsyncKeyState(VirtualKeyLeftButton);
            var wasPressed = false;
            while (!_cancellation.IsCancellationRequested)
            {
                var pressed = (GetAsyncKeyState(VirtualKeyLeftButton) & KeyPressedMask) != 0;
                if (pressed && !wasPressed
                    && GetCursorPos(out var cursor)
                    && Contains(_targetBounds, cursor))
                    Interlocked.Exchange(ref _clicked, 1);
                wasPressed = pressed;
                Thread.Sleep(2);
            }
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            try { _worker.Wait(TimeSpan.FromMilliseconds(100)); }
            catch (AggregateException) { }
            _cancellation.Dispose();
        }
    }

    internal const int VirtualKeyLeftButton = 0x01;
    private const int KeyPressedMask = 0x8000;
    private const int KeyClickedMask = 0x0001;
    private const int LowLevelMouseHook = 14;
    private const int LeftButtonDown = 0x0201;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    private delegate IntPtr LowLevelMouseProcedure(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo information);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

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
    private static extern int SetStretchBltMode(IntPtr deviceContext, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StretchBlt(
        IntPtr destination,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        IntPtr source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int operation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr deviceContext,
        IntPtr bitmap,
        uint startScan,
        int scanLines,
        [Out] byte[] bits,
        ref BitmapInfo bitmapInfo,
        int usage);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelMouseProcedure procedure,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr message,
        IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

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
        CancellationToken cancellationToken);
}

public sealed class WindowsChangeMonitor : IWindowsChangeMonitor
{
    private readonly IWindowsUiObserver _observer;

    public WindowsChangeMonitor(IWindowsUiObserver observer) => _observer = observer;

    public async Task<WindowsObservation?> WaitForTargetInteractionAsync(
        UiBounds targetBounds,
        string? goal,
        string? baselineSnapshotHash,
        TimeSpan maximumWait,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(maximumWait);

        try
        {
            _ = GetAsyncKeyState(VirtualKeyLeftButton);
            var wasPressed = false;
            var nextScreenCheck = DateTimeOffset.UtcNow.AddMilliseconds(180);

            while (!timeout.IsCancellationRequested)
            {
                var buttonState = GetAsyncKeyState(VirtualKeyLeftButton);
                var isPressed = (buttonState & KeyPressedMask) != 0;
                var wasClicked = (isPressed && !wasPressed) || (buttonState & KeyClickedMask) != 0;
                wasPressed = isPressed;

                if (wasClicked && GetCursorPos(out var cursor) && Contains(targetBounds, cursor))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(650), timeout.Token).ConfigureAwait(false);
                    return await ObserveAfterInteractionAsync(goal, timeout.Token).ConfigureAwait(false);
                }

                if (!string.IsNullOrWhiteSpace(baselineSnapshotHash)
                    && DateTimeOffset.UtcNow >= nextScreenCheck)
                {
                    nextScreenCheck = DateTimeOffset.UtcNow.AddMilliseconds(220);
                    try
                    {
                        var observation = await _observer.ObserveAsync(goal, timeout.Token).ConfigureAwait(false);
                        if (HasMeaningfulScreenChange(baselineSnapshotHash, observation.SnapshotHash))
                            return observation;
                    }
                    catch (WindowsObservationException)
                    {
                        // The kiosk can briefly remove its accessibility tree while navigating.
                        // Keep polling until the new screen becomes observable.
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(35), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
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

    internal const int VirtualKeyLeftButton = 0x01;
    private const int KeyPressedMask = 0x8000;
    private const int KeyClickedMask = 0x0001;

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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}

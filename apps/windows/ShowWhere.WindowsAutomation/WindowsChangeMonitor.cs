using System.Windows.Automation;

namespace ShowWhere.WindowsAutomation;

public interface IWindowsChangeMonitor
{
    Task<WindowsObservation?> WaitForMeaningfulChangeAsync(
        WindowsObservation baseline,
        TimeSpan maximumWait,
        CancellationToken cancellationToken);
}

public sealed class WindowsChangeMonitor : IWindowsChangeMonitor
{
    private readonly IWindowsUiObserver _observer;

    public WindowsChangeMonitor(IWindowsUiObserver observer) => _observer = observer;

    public async Task<WindowsObservation?> WaitForMeaningfulChangeAsync(
        WindowsObservation baseline,
        TimeSpan maximumWait,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(maximumWait);
        using var signal = new SemaphoreSlim(0, 1);
        StructureChangedEventHandler structureHandler = (_, _) => Signal(signal);
        AutomationFocusChangedEventHandler focusHandler = (_, _) => Signal(signal);

        try
        {
            Automation.AddStructureChangedEventHandler(
                baseline.Root,
                TreeScope.Subtree,
                structureHandler);
            Automation.AddAutomationFocusChangedEventHandler(focusHandler);

            while (!timeout.IsCancellationRequested)
            {
                try
                {
                    var eventWait = signal.WaitAsync(timeout.Token);
                    var pollWait = Task.Delay(TimeSpan.FromMilliseconds(900), timeout.Token);
                    var completed = await Task.WhenAny(eventWait, pollWait).ConfigureAwait(false);
                    if (completed == eventWait)
                        await Task.Delay(TimeSpan.FromMilliseconds(300), timeout.Token).ConfigureAwait(false);
                    var observation = await _observer.ObserveAsync(timeout.Token).ConfigureAwait(false);
                    if (!string.Equals(observation.SnapshotHash, baseline.SnapshotHash, StringComparison.Ordinal))
                        return observation;
                }
                catch (WindowsObservationException) { }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            try { Automation.RemoveStructureChangedEventHandler(baseline.Root, structureHandler); } catch { }
            try { Automation.RemoveAutomationFocusChangedEventHandler(focusHandler); } catch { }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private static void Signal(SemaphoreSlim signal)
    {
        try { signal.Release(); }
        catch (SemaphoreFullException) { }
    }
}

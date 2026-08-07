using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using ShowWhere.Core;

namespace ShowWhere.WindowsAutomation;

public sealed class WindowsUiObserver : IWindowsUiObserver
{
    private const uint GwHwndNext = 2;
    private const int MaximumTreeNodes = 1_500;
    private const int MaximumForegroundCandidates = 100;
    private const int ReservedTaskbarCandidates = 30;
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly object _windowGate = new();
    private IntPtr _lastExternalForegroundWindow;

    public void RememberCurrentForegroundWindow()
    {
        var handle = GetForegroundWindow();
        if (!IsExternalWindow(handle)) return;
        lock (_windowGate) _lastExternalForegroundWindow = handle;
    }

    public Task<WindowsObservation> ObserveAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Observe(cancellationToken), cancellationToken);

    private WindowsObservation Observe(CancellationToken cancellationToken)
    {
        var windowHandle = FindExternalForegroundWindow();
        if (windowHandle == IntPtr.Zero) throw new WindowsObservationException();

        AutomationElement root;
        try
        {
            root = AutomationElement.FromHandle(windowHandle);
        }
        catch
        {
            throw new WindowsObservationException();
        }

        var processName = GetProcessName(root);
        var windowTitle = Read(() => root.Current.Name);
        var context = new ApplicationContext(
            Platforms.Windows,
            string.IsNullOrWhiteSpace(processName) ? "Windows application" : processName,
            string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle,
            Locale: CultureInfo.CurrentUICulture.Name);

        var elementsBySource = new Dictionary<string, AutomationElement>(StringComparer.Ordinal);
        var foregroundRaw = CollectRawCandidates(root, processName, elementsBySource, cancellationToken);
        var foregroundCandidates = CandidateNormalizer.Normalize(foregroundRaw, MaximumForegroundCandidates);
        var roots = new List<AutomationElement> { root };
        IReadOnlyList<NormalizedAutomationCandidate> taskbarCandidates = [];

        var taskbarHandle = FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle != IntPtr.Zero && taskbarHandle != windowHandle && IsExternalWindow(taskbarHandle))
        {
            try
            {
                var taskbarRoot = AutomationElement.FromHandle(taskbarHandle);
                roots.Add(taskbarRoot);
                var taskbarProcessName = GetProcessName(taskbarRoot);
                var taskbarRaw = CollectRawCandidates(
                    taskbarRoot,
                    taskbarProcessName,
                    elementsBySource,
                    cancellationToken);
                taskbarCandidates = CandidateNormalizer.Normalize(taskbarRaw, ReservedTaskbarCandidates)
                    .Select(MarkAsTaskbarCandidate)
                    .ToArray();
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
        }

        var normalized = CandidateNormalizer.MergeWithReservedSecondaryScope(
            foregroundCandidates,
            taskbarCandidates,
            MaximumForegroundCandidates,
            ReservedTaskbarCandidates);
        var candidates = normalized.Select(item => item.Candidate).ToArray();
        var registryElements = normalized
            .Where(item => elementsBySource.ContainsKey(item.SourceKey))
            .ToDictionary(item => item.Candidate.Id, item => elementsBySource[item.SourceKey], StringComparer.Ordinal);
        var registry = new CandidateRegistry(registryElements);
        var focusedElementKey = GetFocusedElementKey();
        return new WindowsObservation(
            context,
            candidates,
            registry,
            WindowsObservation.ComputeHash(context, candidates, focusedElementKey),
            roots,
            focusedElementKey);
    }

    private List<RawAutomationCandidate> CollectRawCandidates(
        AutomationElement root,
        string processName,
        IDictionary<string, AutomationElement> elementsBySource,
        CancellationToken cancellationToken)
    {
        var result = new List<RawAutomationCandidate>();
        var walker = TreeWalker.ControlViewWalker;
        var queue = new Queue<AutomationElement>();
        EnqueueChildren(root, walker, queue);
        var visited = 0;

        while (queue.Count > 0 && visited++ < MaximumTreeNodes && result.Count < 500)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var element = queue.Dequeue();
            try
            {
                if (element.Current.ProcessId == _ownProcessId) continue;
                EnqueueChildren(element, walker, queue);
                var target = ResolveClickableElement(element, walker) ?? element;
                var sourceKey = GetSourceKey(target);
                if (string.IsNullOrEmpty(sourceKey)) continue;
                var role = MapRole(target.Current.ControlType);
                var rectangle = target.Current.BoundingRectangle;
                var visible = !target.Current.IsOffscreen && !rectangle.IsEmpty;
                var clickable = IsActionable(target, role);
                elementsBySource[sourceKey] = target;
                result.Add(new RawAutomationCandidate(
                    sourceKey,
                    sourceKey,
                    Read(() => target.Current.Name),
                    Read(() => target.Current.HelpText),
                    role,
                    target.Current.IsEnabled,
                    visible,
                    clickable,
                    new UiBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height),
                    Read(() => target.Current.IsPassword),
                    Read(() => target.Current.AutomationId),
                    Read(() => target.Current.ClassName),
                    Read(() => target.Current.ControlType.ProgrammaticName),
                    processName));
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
        }

        return result;
    }

    private AutomationElement? ResolveClickableElement(AutomationElement element, TreeWalker walker)
    {
        var current = element;
        for (var depth = 0; depth < 5 && current is not null; depth++)
        {
            var role = MapRole(Read(() => current.Current.ControlType));
            if (IsActionable(current, role)) return current;
            try { current = walker.GetParent(current); }
            catch (ElementNotAvailableException) { return null; }
        }
        return null;
    }

    private static bool IsActionable(AutomationElement element, string role)
    {
        if (role is "button" or "link" or "edit" or "checkbox" or "radio" or "combobox"
            or "menuitem" or "tab" or "listitem" or "treeitem" or "dataitem") return true;
        return element.TryGetCurrentPattern(InvokePattern.Pattern, out _)
            || element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _)
            || element.TryGetCurrentPattern(TogglePattern.Pattern, out _)
            || element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _);
    }

    private static string MapRole(ControlType? controlType)
    {
        if (controlType == ControlType.Button) return "button";
        if (controlType == ControlType.Hyperlink) return "link";
        if (controlType == ControlType.Edit) return "edit";
        if (controlType == ControlType.CheckBox) return "checkbox";
        if (controlType == ControlType.RadioButton) return "radio";
        if (controlType == ControlType.ComboBox) return "combobox";
        if (controlType == ControlType.MenuItem) return "menuitem";
        if (controlType == ControlType.TabItem) return "tab";
        if (controlType == ControlType.ListItem) return "listitem";
        if (controlType == ControlType.TreeItem) return "treeitem";
        if (controlType == ControlType.DataItem) return "dataitem";
        if (controlType == ControlType.Document) return "document";
        if (controlType == ControlType.Pane) return "pane";
        if (controlType == ControlType.Window) return "window";
        return "other";
    }

    private static void EnqueueChildren(AutomationElement parent, TreeWalker walker, Queue<AutomationElement> queue)
    {
        try
        {
            var child = walker.GetFirstChild(parent);
            while (child is not null)
            {
                queue.Enqueue(child);
                child = walker.GetNextSibling(child);
            }
        }
        catch (ElementNotAvailableException) { }
    }

    private static string GetSourceKey(AutomationElement element)
    {
        try
        {
            var runtimeId = element.GetRuntimeId();
            return runtimeId is { Length: > 0 }
                ? string.Join('.', runtimeId)
                : $"{element.Current.ProcessId}:{element.Current.AutomationId}:{element.Current.NativeWindowHandle}";
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    private static string GetProcessName(AutomationElement root)
    {
        try { return Process.GetProcessById(root.Current.ProcessId).ProcessName; }
        catch { return "Windows application"; }
    }

    private string? GetFocusedElementKey()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            return focused.Current.ProcessId != _ownProcessId ? GetSourceKey(focused) : null;
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private IntPtr FindExternalForegroundWindow()
    {
        var handle = GetForegroundWindow();
        if (IsExternalWindow(handle))
        {
            lock (_windowGate) _lastExternalForegroundWindow = handle;
            return handle;
        }

        IntPtr remembered;
        lock (_windowGate) remembered = _lastExternalForegroundWindow;
        if (IsExternalWindow(remembered)) return remembered;

        for (var index = 0; index < 64 && handle != IntPtr.Zero; index++)
        {
            if (IsExternalWindow(handle)) return handle;
            handle = GetWindow(handle, GwHwndNext);
        }
        return IntPtr.Zero;
    }

    private bool IsExternalWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle) || !IsWindowVisible(handle)) return false;
        _ = GetWindowThreadProcessId(handle, out var processId);
        return processId != 0 && processId != _ownProcessId;
    }

    private static T? Read<T>(Func<T> reader)
    {
        try { return reader(); }
        catch (ElementNotAvailableException) { return default; }
        catch (InvalidOperationException) { return default; }
    }

    private static NormalizedAutomationCandidate MarkAsTaskbarCandidate(NormalizedAutomationCandidate item)
    {
        var attributes = item.Candidate.Attributes is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(item.Candidate.Attributes, StringComparer.Ordinal);
        attributes["sourceScope"] = "windows_taskbar";
        return item with { Candidate = item.Candidate with { Attributes = attributes } };
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out int processId);
}

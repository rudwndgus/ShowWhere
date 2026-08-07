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
    private const int ReservedWindowOverviewCandidates = 10;
    private const int ReservedGlobalCandidates = ReservedTaskbarCandidates + ReservedWindowOverviewCandidates;
    private static readonly HashSet<string> TransientShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost",
        "SearchHost",
        "SearchApp",
        "ShellExperienceHost",
    };
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly object _windowGate = new();
    private IntPtr _lastExternalForegroundWindow;

    public void RememberCurrentForegroundWindow()
    {
        var handle = GetForegroundWindow();
        if (!IsExternalWindow(handle)) return;
        lock (_windowGate) _lastExternalForegroundWindow = handle;
    }

    public Task<WindowsObservation> ObserveAsync(string? goal, CancellationToken cancellationToken) =>
        Task.Run(() => Observe(goal, cancellationToken), cancellationToken);

    private WindowsObservation Observe(string? goal, CancellationToken cancellationToken)
    {
        var windowHandle = WindowsFastPathResolver.IsKnownSystemGoal(goal)
            ? FindVisibleTransientShellSurface()
            : IntPtr.Zero;
        if (windowHandle == IntPtr.Zero) windowHandle = FindExternalForegroundWindow();
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

        var deferForegroundScan = WindowsFastPathResolver.IsKnownSystemGoal(goal)
            && !WindowsFastPathResolver.IsTrustedWindowsProcess(processName);
        var elementsBySource = new Dictionary<string, AutomationElement>(StringComparer.Ordinal);
        IReadOnlyList<NormalizedAutomationCandidate> foregroundCandidates = [];
        if (!deferForegroundScan)
        {
            var foregroundRaw = CollectRawCandidates(root, processName, elementsBySource, cancellationToken);
            foregroundCandidates = CandidateNormalizer.Normalize(foregroundRaw, MaximumForegroundCandidates);
        }
        var roots = new List<AutomationElement> { root };
        var overviewSourceKeys = new HashSet<string>(StringComparer.Ordinal);
        var overviewRaw = CollectWindowOverview(elementsBySource, overviewSourceKeys, cancellationToken);
        var overviewCandidates = CandidateNormalizer.Normalize(overviewRaw, ReservedWindowOverviewCandidates)
            .Select(MarkAsWindowOverviewCandidate)
            .ToArray();
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
            catch (ArgumentException) { }
            catch (COMException) { }
        }

        var globalCandidates = overviewCandidates.Concat(taskbarCandidates).ToArray();
        var normalized = CandidateNormalizer.MergeWithReservedSecondaryScope(
            foregroundCandidates,
            globalCandidates,
            MaximumForegroundCandidates,
            ReservedGlobalCandidates);
        var candidates = normalized.Select(item => item.Candidate).ToArray();
        var registryElements = new Dictionary<string, AutomationElement>(StringComparer.Ordinal);
        foreach (var item in normalized)
        {
            if (elementsBySource.TryGetValue(item.SourceKey, out var element))
                registryElements[item.Candidate.Id] = element;
        }
        var titleBarIds = normalized
            .Where(item => overviewSourceKeys.Contains(item.SourceKey))
            .Select(item => item.Candidate.Id)
            .ToHashSet(StringComparer.Ordinal);
        var registry = new CandidateRegistry(registryElements, titleBarIds);
        var focusedElementKey = GetFocusedElementKey();
        return new WindowsObservation(
            context,
            candidates,
            registry,
            WindowsObservation.ComputeHash(context, candidates, focusedElementKey),
            roots,
            focusedElementKey,
            deferForegroundScan);
    }

    private List<RawAutomationCandidate> CollectWindowOverview(
        IDictionary<string, AutomationElement> elementsBySource,
        ISet<string> overviewSourceKeys,
        CancellationToken cancellationToken)
    {
        var result = new List<RawAutomationCandidate>();
        _ = EnumWindows((handle, _) =>
        {
            if (result.Count >= 40) return false;
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsExternalWindow(handle) || GetWindow(handle, 4) != IntPtr.Zero) return true;
            try
            {
                var element = AutomationElement.FromHandle(handle);
                var title = Read(() => element.Current.Name);
                var rectangle = Read(() => element.Current.BoundingRectangle);
                if (string.IsNullOrWhiteSpace(title) || rectangle.IsEmpty
                    || rectangle.Width < 120 || rectangle.Height < 80) return true;
                var sourceKey = GetSourceKey(element);
                if (string.IsNullOrEmpty(sourceKey)) return true;
                var processName = GetProcessName(element);
                elementsBySource[sourceKey] = element;
                overviewSourceKeys.Add(sourceKey);
                result.Add(new RawAutomationCandidate(
                    sourceKey,
                    sourceKey,
                    title,
                    $"Open window in {processName}",
                    "window",
                    true,
                    true,
                    true,
                    new UiBounds(rectangle.X, rectangle.Y, rectangle.Width, Math.Min(48, rectangle.Height)),
                    false,
                    null,
                    null,
                    "ControlType.Window",
                    processName));
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            catch (COMException) { }
            return true;
        }, IntPtr.Zero);
        return result;
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
                var isOffscreen = target.Current.IsOffscreen;
                var visible = !rectangle.IsEmpty;
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
                    Read(() => target.Current.ControlType?.ProgrammaticName),
                    processName,
                    isOffscreen));
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            catch (COMException) { }
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
            catch (InvalidOperationException) { return null; }
            catch (COMException) { return null; }
        }
        return null;
    }

    private static bool IsActionable(AutomationElement element, string role)
    {
        if (role is "button" or "link" or "edit" or "checkbox" or "radio" or "combobox"
            or "menuitem" or "tab" or "listitem" or "treeitem" or "dataitem" or "slider") return true;
        return element.TryGetCurrentPattern(InvokePattern.Pattern, out _)
            || element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _)
            || element.TryGetCurrentPattern(TogglePattern.Pattern, out _)
            || element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _)
            || element.TryGetCurrentPattern(RangeValuePattern.Pattern, out _);
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
        if (controlType == ControlType.Slider) return "slider";
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
        catch (InvalidOperationException) { }
        catch (COMException) { }
    }

    private static string GetSourceKey(AutomationElement element)
    {
        try
        {
            var processId = element.Current.ProcessId;
            var runtimeId = element.GetRuntimeId();
            return ComposeSourceKey(
                processId,
                runtimeId,
                element.Current.AutomationId,
                element.Current.NativeWindowHandle);
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException) { return string.Empty; }
        catch (COMException) { return string.Empty; }
    }

    internal static string ComposeSourceKey(
        int processId,
        IReadOnlyList<int>? runtimeId,
        string? automationId,
        int nativeWindowHandle) =>
        runtimeId is { Count: > 0 }
            ? $"{processId}:{string.Join('.', runtimeId)}"
            : $"{processId}:{automationId}:{nativeWindowHandle}";

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
        catch (COMException) { return null; }
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

    private IntPtr FindVisibleTransientShellSurface()
    {
        var bestHandle = IntPtr.Zero;
        var bestArea = 0d;
        _ = EnumWindows((handle, parameter) =>
        {
            if (!IsExternalWindow(handle) || IsCloaked(handle)) return true;
            _ = GetWindowThreadProcessId(handle, out var processId);
            string processName;
            try { processName = Process.GetProcessById(processId).ProcessName; }
            catch { return true; }
            if (!TransientShellProcesses.Contains(processName)) return true;
            try
            {
                var element = AutomationElement.FromHandle(handle);
                var rectangle = element.Current.BoundingRectangle;
                if (element.Current.IsOffscreen || rectangle.IsEmpty
                    || rectangle.Width < 240 || rectangle.Height < 180) return true;
                var area = rectangle.Width * rectangle.Height;
                if (area <= bestArea) return true;
                bestArea = area;
                bestHandle = handle;
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            catch (COMException) { }
            return true;
        }, IntPtr.Zero);
        return bestHandle;
    }

    private static bool IsCloaked(IntPtr handle)
    {
        try
        {
            return DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
        }
        catch (DllNotFoundException) { return false; }
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
        catch (COMException) { return default; }
    }

    private static NormalizedAutomationCandidate MarkAsTaskbarCandidate(NormalizedAutomationCandidate item)
    {
        var attributes = item.Candidate.Attributes is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(item.Candidate.Attributes, StringComparer.Ordinal);
        attributes["sourceScope"] = "windows_taskbar";
        return item with { Candidate = item.Candidate with { Attributes = attributes } };
    }

    private static NormalizedAutomationCandidate MarkAsWindowOverviewCandidate(NormalizedAutomationCandidate item)
    {
        var attributes = item.Candidate.Attributes is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(item.Candidate.Attributes, StringComparer.Ordinal);
        attributes["sourceScope"] = "windows_window_overview";
        return item with { Candidate = item.Candidate with { Attributes = attributes } };
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        out int value,
        int valueSize);
}

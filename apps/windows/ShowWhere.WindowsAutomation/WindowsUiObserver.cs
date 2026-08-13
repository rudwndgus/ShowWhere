using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using ShowWhere.Core;

namespace ShowWhere.WindowsAutomation;

public sealed class WindowsUiObserver : IWindowsUiObserver
{
    private const uint GwHwndNext = 2;
    private const uint GaRoot = 2;
    private const int MaximumTreeNodes = 1_500;
    private const int MaximumForegroundCandidates = 180;
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
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "bravebrowser", "firefox", "opera", "opera_gx", "vivaldi", "arc",
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
        var windowHandle = FindVisibleTransientShellSurface();
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
        var browserUrl = TryReadBrowserUrl(root, processName);
        var context = new ApplicationContext(
            Platforms.Windows,
            string.IsNullOrWhiteSpace(processName) ? "Windows application" : processName,
            string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle,
            browserUrl,
            Locale: CultureInfo.CurrentUICulture.Name);

        const bool deferForegroundScan = false;
        var elementsBySource = new Dictionary<string, AutomationElement>(StringComparer.Ordinal);
        IReadOnlyList<NormalizedAutomationCandidate> foregroundCandidates = [];
        if (!deferForegroundScan)
        {
            var foregroundRaw = CollectRawCandidates(root, processName, elementsBySource, cancellationToken);
            var normalizedForeground = CandidateNormalizer.Normalize(foregroundRaw, MaximumTreeNodes);
            foregroundCandidates = CandidatePrioritizer.Prioritize(
                normalizedForeground,
                goal,
                MaximumForegroundCandidates);
        }
        var roots = new List<AutomationElement> { root };
        var overviewSourceKeys = new HashSet<string>(StringComparer.Ordinal);
        var overviewRaw = CollectWindowOverview(elementsBySource, overviewSourceKeys, cancellationToken);
        var overviewCandidates = CandidateNormalizer.Normalize(overviewRaw, ReservedWindowOverviewCandidates)
            .Select(MarkAsWindowOverviewCandidate)
            .ToArray();
        var taskbarRawCandidates = new List<RawAutomationCandidate>();

        foreach (var taskbarHandle in FindTaskbarWindows())
        {
            if (taskbarHandle == windowHandle || !IsExternalWindow(taskbarHandle)) continue;
            try
            {
                var taskbarRoot = AutomationElement.FromHandle(taskbarHandle);
                roots.Add(taskbarRoot);
                var taskbarProcessName = GetProcessName(taskbarRoot);
                taskbarRawCandidates.AddRange(CollectRawCandidates(
                    taskbarRoot,
                    taskbarProcessName,
                    elementsBySource,
                    cancellationToken));
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            catch (COMException) { }
        }
        var taskbarCandidates = CandidateNormalizer.Normalize(
                taskbarRawCandidates,
                ReservedTaskbarCandidates)
            .Select(MarkAsTaskbarCandidate)
            .ToArray();

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
        var registry = new CandidateRegistry(
            registryElements,
            candidates.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal),
            titleBarIds);
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
            if (!IsExternalWindow(handle) || GetWindow(handle, 4) != IntPtr.Zero
                || IsIconic(handle) || IsCloaked(handle)) return true;
            try
            {
                var element = AutomationElement.FromHandle(handle);
                var title = Read(() => element.Current.Name);
                var rectangle = Read(() => element.Current.BoundingRectangle);
                if (string.IsNullOrWhiteSpace(title) || rectangle.IsEmpty
                    || rectangle.Width < 120 || rectangle.Height < 80) return true;
                if (!IsTitleBarFullyVisible(handle, rectangle)) return true;
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

    private static IReadOnlyList<IntPtr> FindTaskbarWindows()
    {
        var result = new List<IntPtr>();
        _ = EnumWindows((handle, _) =>
        {
            var className = new char[256];
            var length = GetClassName(handle, className, className.Length);
            if (length <= 0) return true;
            var value = new string(className, 0, length);
            if (value is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") result.Add(handle);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static bool IsTitleBarFullyVisible(IntPtr windowHandle, System.Windows.Rect rectangle)
    {
        var inset = Math.Min(16d, rectangle.Width / 10);
        var left = rectangle.Left + inset;
        var right = rectangle.Right - inset;
        var y = rectangle.Top + Math.Min(24d, Math.Max(4d, rectangle.Height / 4));
        var samplePoints = new[]
        {
            left,
            left + (right - left) * 0.25,
            left + (right - left) * 0.5,
            left + (right - left) * 0.75,
            right,
        };

        foreach (var x in samplePoints)
        {
            var point = new NativePoint((int)Math.Round(x), (int)Math.Round(y));
            var hit = WindowFromPoint(point);
            if (hit == IntPtr.Zero || GetAncestor(hit, GaRoot) != windowHandle) return false;
        }
        return true;
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

        // Dense web pages can expose hundreds of containers before important header
        // controls. Scan the bounded tree, then rank the actionable results for the goal.
        while (queue.Count > 0 && visited++ < MaximumTreeNodes)
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
                var bounds = new UiBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                var isOffscreen = target.Current.IsOffscreen || !WindowsScreenGeometry.ContainsCenter(bounds);
                var visible = !rectangle.IsEmpty;
                var clickable = IsActionable(target, role);
                var candidateScope = ClassifyCandidateScope(target, walker, processName);
                var label = ClickableParentResolver.PreferAccessibleText(
                    Read(() => target.Current.Name),
                    Read(() => element.Current.Name));
                var description = ClickableParentResolver.PreferAccessibleText(
                    Read(() => target.Current.HelpText),
                    Read(() => element.Current.HelpText));
                elementsBySource[sourceKey] = target;
                result.Add(new RawAutomationCandidate(
                    sourceKey,
                    sourceKey,
                    label,
                    description,
                    role,
                    target.Current.IsEnabled,
                    visible,
                    clickable,
                    bounds,
                    Read(() => target.Current.IsPassword),
                    Read(() => target.Current.AutomationId),
                    Read(() => target.Current.ClassName),
                    Read(() => target.Current.ControlType?.ProgrammaticName),
                    processName,
                    isOffscreen,
                    candidateScope.SourceScope,
                    candidateScope.ContainerLabel));
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            catch (COMException) { }
        }

        return result;
    }

    private static BrowserCandidateScope ClassifyCandidateScope(
        AutomationElement element,
        TreeWalker walker,
        string processName)
    {
        var descriptors = new List<AutomationScopeDescriptor>();
        var current = element;
        for (var depth = 0; depth < 16 && current is not null; depth++)
        {
            var role = MapRole(Read(() => current.Current.ControlType));
            descriptors.Add(new AutomationScopeDescriptor(
                role,
                Read(() => current.Current.ClassName),
                Read(() => current.Current.Name)));
            try { current = walker.GetParent(current); }
            catch (ElementNotAvailableException) { break; }
            catch (InvalidOperationException) { break; }
            catch (COMException) { break; }
        }
        return BrowserCandidateScopeClassifier.Classify(processName, descriptors);
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

    private static string? TryReadBrowserUrl(AutomationElement root, string processName)
    {
        if (!BrowserProcesses.Contains(processName)) return null;
        var walker = TreeWalker.ControlViewWalker;
        var queue = new Queue<AutomationElement>();
        EnqueueChildren(root, walker, queue);
        for (var visited = 0; queue.Count > 0 && visited++ < 500;)
        {
            var element = queue.Dequeue();
            try
            {
                EnqueueChildren(element, walker, queue);
                if (element.Current.ControlType != ControlType.Edit
                    || !element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObject)
                    || patternObject is not ValuePattern valuePattern) continue;
                var raw = valuePattern.Current.Value?.Trim();
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) continue;
                var sanitized = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty, Query = string.Empty, Fragment = string.Empty };
                return sanitized.Uri.AbsoluteUri;
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            catch (COMException) { }
        }
        return null;
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
        var foreground = GetForegroundWindow();
        if (IsVisibleTransientShellSurface(foreground)) return foreground;

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

    private bool IsVisibleTransientShellSurface(IntPtr handle)
    {
        if (!IsExternalWindow(handle) || IsCloaked(handle)) return false;
        _ = GetWindowThreadProcessId(handle, out var processId);
        string processName;
        try { processName = Process.GetProcessById(processId).ProcessName; }
        catch { return false; }
        if (!TransientShellProcesses.Contains(processName)) return false;
        try
        {
            var element = AutomationElement.FromHandle(handle);
            var rectangle = element.Current.BoundingRectangle;
            return !element.Current.IsOffscreen && !rectangle.IsEmpty
                && rectangle.Width >= 240 && rectangle.Height >= 180;
        }
        catch (ElementNotAvailableException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (ArgumentException) { return false; }
        catch (COMException) { return false; }
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

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr windowHandle, [Out] char[] className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

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

namespace ShowWhere.WindowsAutomation;

public sealed record AutomationScopeDescriptor(string? Role, string? ClassName, string? Name);

public sealed record BrowserCandidateScope(string SourceScope, string? ContainerLabel = null);

public static class BrowserCandidateScopeClassifier
{
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "brave",
        "bravebrowser",
        "firefox",
        "opera",
        "opera_gx",
        "vivaldi",
        "arc",
    };

    public static BrowserCandidateScope Classify(
        string? processName,
        IEnumerable<AutomationScopeDescriptor> elementAndAncestors)
    {
        if (string.IsNullOrWhiteSpace(processName) || !BrowserProcesses.Contains(processName))
            return new BrowserCandidateScope("foreground_application");

        string? containerLabel = null;
        foreach (var descriptor in elementAndAncestors)
        {
            if (string.Equals(descriptor.Role, "document", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(descriptor.Name)) containerLabel = descriptor.Name;
                return new BrowserCandidateScope("browser_content", containerLabel);
            }

            var className = descriptor.ClassName ?? string.Empty;
            if (className.Contains("RenderWidgetHost", StringComparison.OrdinalIgnoreCase)
                || className.Contains("MozillaContentWindow", StringComparison.OrdinalIgnoreCase))
                return new BrowserCandidateScope("browser_content", containerLabel);
        }

        return new BrowserCandidateScope("browser_chrome");
    }
}

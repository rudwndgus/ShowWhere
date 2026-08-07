using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ShowWhere.Core;

namespace ShowWhere.WindowsAutomation;

public sealed record RawAutomationCandidate(
    string SourceKey,
    string ClickableSourceKey,
    string? Label,
    string? Description,
    string Role,
    bool Enabled,
    bool Visible,
    bool Clickable,
    UiBounds Bounds,
    bool IsPassword,
    string? AutomationId,
    string? ClassName,
    string? ControlType,
    string? ProcessName,
    bool IsOffscreen = false);

public sealed record NormalizedAutomationCandidate(UiCandidate Candidate, string SourceKey);

public static class CandidateNormalizer
{
    private static readonly HashSet<string> UsefulRoles =
    [
        "button", "link", "edit", "checkbox", "radio", "combobox", "menuitem",
        "tab", "listitem", "treeitem", "dataitem", "slider", "window", "document", "pane",
    ];

    public static IReadOnlyList<NormalizedAutomationCandidate> Normalize(
        IEnumerable<RawAutomationCandidate> source,
        int maximumCandidates = 100)
    {
        var result = new List<NormalizedAutomationCandidate>();
        var clickableSources = new HashSet<string>(StringComparer.Ordinal);
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in source)
        {
            if (result.Count >= maximumCandidates) break;
            if (!raw.Visible || !raw.Enabled || raw.Bounds.Width <= 1 || raw.Bounds.Height <= 1) continue;
            if (!UsefulRoles.Contains(raw.Role)) continue;
            var label = NormalizeText(raw.IsPassword ? "Password field" : raw.Label, 500);
            var description = raw.IsPassword ? null : NormalizeText(raw.Description, 1_000);
            if (raw.Role is "pane" or "document" && string.IsNullOrWhiteSpace(label)) continue;
            if (!raw.Clickable && raw.Role is not ("edit" or "document" or "pane")) continue;
            if (!clickableSources.Add(raw.ClickableSourceKey)) continue;

            var fingerprint = string.Join('|',
                raw.ProcessName,
                raw.AutomationId,
                raw.ClassName,
                raw.ControlType,
                raw.Role,
                label,
                Math.Round(raw.Bounds.X),
                Math.Round(raw.Bounds.Y),
                Math.Round(raw.Bounds.Width),
                Math.Round(raw.Bounds.Height));
            if (!fingerprints.Add(fingerprint)) continue;

            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
            AddAttribute(attributes, "automationId", raw.AutomationId);
            AddAttribute(attributes, "className", raw.ClassName);
            AddAttribute(attributes, "controlType", raw.ControlType);
            AddAttribute(attributes, "processName", raw.ProcessName);
            attributes["inViewport"] = !raw.IsOffscreen;
            if (raw.IsPassword) attributes["isPassword"] = true;

            var id = $"win-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..20].ToLowerInvariant()}";
            var candidate = new UiCandidate(
                id,
                label,
                description,
                raw.Role,
                raw.Enabled,
                raw.Visible,
                raw.Clickable,
                raw.Bounds,
                attributes.Count == 0 ? null : attributes);
            result.Add(new NormalizedAutomationCandidate(candidate, raw.ClickableSourceKey));
        }

        return result;
    }

    public static IReadOnlyList<NormalizedAutomationCandidate> MergeWithReservedSecondaryScope(
        IEnumerable<NormalizedAutomationCandidate> primary,
        IEnumerable<NormalizedAutomationCandidate> secondary,
        int maximumCandidates = 100,
        int reservedSecondaryCandidates = 30)
    {
        if (maximumCandidates <= 0) return [];
        var secondaryLimit = Math.Clamp(reservedSecondaryCandidates, 0, maximumCandidates);
        var secondaryItems = secondary
            .DistinctBy(item => item.Candidate.Id, StringComparer.Ordinal)
            .Take(secondaryLimit)
            .ToList();
        var seen = secondaryItems.Select(item => item.Candidate.Id).ToHashSet(StringComparer.Ordinal);
        var primaryItems = primary
            .Where(item => seen.Add(item.Candidate.Id))
            .Take(maximumCandidates - secondaryItems.Count)
            .ToList();
        return [.. primaryItems, .. secondaryItems];
    }

    private static string? NormalizeText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static void AddAttribute(IDictionary<string, object?> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) target[key] = value;
    }
}

public sealed record AutomationAncestorDescriptor(string Key, string Role, bool SupportsAction);

public static class ClickableParentResolver
{
    public static string? Resolve(IEnumerable<AutomationAncestorDescriptor> elementAndAncestors, int maximumDepth = 5)
    {
        foreach (var descriptor in elementAndAncestors.Take(maximumDepth))
        {
            if (descriptor.SupportsAction || descriptor.Role is "button" or "link" or "checkbox" or "radio"
                or "combobox" or "menuitem" or "tab" or "listitem" or "treeitem" or "dataitem" or "edit" or "slider")
                return descriptor.Key;
        }
        return null;
    }
}

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
    bool IsOffscreen = false,
    string? SourceScope = null,
    string? ContainerLabel = null);

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

        var preferredSources = source
            .Select((candidate, index) => new { Candidate = candidate, Index = index })
            .GroupBy(item => item.Candidate.ClickableSourceKey, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(item => AccessibleTextScore(item.Candidate))
                .ThenBy(item => item.Index)
                .First())
            .OrderBy(item => item.Index)
            .Select(item => item.Candidate);

        foreach (var raw in preferredSources)
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
            AddAttribute(attributes, "sourceScope", raw.SourceScope);
            AddAttribute(attributes, "containerLabel", raw.ContainerLabel);
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

    private static int AccessibleTextScore(RawAutomationCandidate candidate) =>
        (string.IsNullOrWhiteSpace(candidate.Label) ? 0 : 10_000 + candidate.Label.Length)
        + (string.IsNullOrWhiteSpace(candidate.Description) ? 0 : 1_000 + candidate.Description.Length);

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

public static class CandidatePrioritizer
{
    private static readonly string[] ActionRoles =
    [
        "button", "link", "edit", "checkbox", "radio", "combobox", "menuitem",
        "tab", "listitem", "treeitem", "dataitem", "slider",
    ];

    public static IReadOnlyList<NormalizedAutomationCandidate> Prioritize(
        IEnumerable<NormalizedAutomationCandidate> source,
        string? goal,
        int maximumCandidates)
    {
        if (maximumCandidates <= 0) return [];
        var goalText = Normalize(goal);
        var goalWords = goalText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return source.Select((item, index) => new
            {
                Item = item,
                Index = index,
                Score = Score(item.Candidate, goalText, goalWords),
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(maximumCandidates)
            .Select(item => item.Item)
            .ToArray();
    }

    private static double Score(UiCandidate candidate, string goal, IReadOnlyList<string> goalWords)
    {
        var text = Normalize($"{candidate.Label} {candidate.Description}");
        double score = candidate.Clickable ? 500 : 0;
        if (ActionRoles.Contains(candidate.Role, StringComparer.Ordinal)) score += 400;
        if (!string.IsNullOrWhiteSpace(candidate.Label)) score += 150;
        if (candidate.Attributes?.TryGetValue("inViewport", out var viewport) == true && viewport is true)
            score += 600;
        if (candidate.Attributes?.TryGetValue("sourceScope", out var scope) == true
            && string.Equals(Convert.ToString(scope), "browser_content", StringComparison.Ordinal))
            score += 250;

        score += goalWords.Count(word => text.Contains(word, StringComparison.Ordinal)) * 1_500;
        if (MatchesConcept(goal, text, "로그인", "login", "log in", "sign in", "account", "계정")) score += 12_000;
        if (MatchesConcept(goal, text, "검색", "search", "찾아", "find")) score += 10_000;
        if (MatchesConcept(goal, text, "설정", "settings", "setting", "preferences", "환경설정")) score += 10_000;
        if (MatchesConcept(goal, text, "장바구니", "cart", "basket")) score += 10_000;
        if (MatchesConcept(goal, text, "주문", "order", "orders", "구매")) score += 8_000;

        var area = candidate.Bounds.Width * candidate.Bounds.Height;
        if (candidate.Role is "pane" or "document" or "window") score -= 1_000;
        if (area > 0) score -= Math.Min(300, Math.Log10(area + 1) * 35);
        return score;
    }

    private static bool MatchesConcept(string goal, string candidate, params string[] aliases)
    {
        var goalMatches = aliases.Any(alias => goal.Contains(alias, StringComparison.Ordinal));
        return goalMatches && aliases.Any(alias => candidate.Contains(alias, StringComparison.Ordinal));
    }

    private static string Normalize(string? value) => string.Join(' ',
        (value ?? string.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public sealed record AutomationAncestorDescriptor(string Key, string Role, bool SupportsAction);

public static class ClickableParentResolver
{
    public static string? PreferAccessibleText(string? clickableParentText, string? childText) =>
        !string.IsNullOrWhiteSpace(clickableParentText) ? clickableParentText
        : !string.IsNullOrWhiteSpace(childText) ? childText
        : null;

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

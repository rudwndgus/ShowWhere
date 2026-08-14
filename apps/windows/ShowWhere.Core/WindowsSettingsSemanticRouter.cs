using System.Text;
using System.Text.RegularExpressions;

namespace ShowWhere.Core;

/// <summary>
/// Resolves Windows Settings navigation concepts against live UIA candidates.
/// Semantics are shared between PCs; physical coordinates are never shared.
/// </summary>
public static class WindowsSettingsSemanticRouter
{
    private sealed record Route(string[] Targets, string[] Aliases);

    private static readonly Route[] Routes =
    [
        new(["System", "시스템"], ["system", "시스템"]),
        new(["Bluetooth & devices", "Bluetooth 및 장치", "블루투스 및 장치"],
            ["bluetooth", "devices", "블루투스", "장치"]),
        new(["Network & internet", "네트워크 및 인터넷"],
            ["network", "internet", "wifi", "wi-fi", "네트워크", "인터넷", "와이파이"]),
        new(["Personalization", "개인 설정"],
            ["personalization", "personalize", "개인 설정", "배경", "테마"]),
        new(["Apps", "앱"], ["apps", "app settings", "앱 설정"]),
        new(["Accounts", "계정"], ["accounts", "account settings", "계정"]),
        new(["Time & language", "시간 및 언어"],
            ["time & language", "time and language", "language settings", "시간 및 언어", "언어 설정"]),
        new(["Gaming", "게임"], ["gaming", "game settings", "게임 설정"]),
        new(["Accessibility", "접근성"], ["accessibility", "접근성"]),
        new(["Privacy & security", "개인 정보 및 보안", "개인정보 및 보안"],
            ["privacy & security", "privacy and security", "privacy", "security settings", "개인 정보", "개인정보", "보안 설정"]),
        new(["Windows Update", "Windows 업데이트", "윈도우 업데이트"],
            ["windows update", "윈도우 업데이트", "windows 업데이트"]),
    ];

    private static readonly string[] PrinterAliases =
        ["printer", "printers", "printing", "프린터", "인쇄 장치", "인쇄장치"];
    private static readonly string[] PrinterTargets =
        ["Printers & scanners", "Printers and scanners", "프린터 및 스캐너"];

    public static bool TryResolve(
        string goal,
        ApplicationContext context,
        IReadOnlyList<UiCandidate> candidates,
        out UiCandidate target)
    {
        target = null!;
        if (!IsSettings(context, candidates)) return false;

        if (ContainsAny(goal, PrinterAliases))
        {
            target = FindLive(candidates, PrinterTargets)
                ?? FindLive(candidates, Routes[1].Targets)!;
            return target is not null;
        }

        foreach (var route in Routes)
        {
            if (!ContainsAny(goal, route.Aliases)) continue;
            var match = FindLive(candidates, route.Targets);
            if (match is null) continue;
            target = match;
            return true;
        }
        return false;
    }

    private static bool IsSettings(ApplicationContext context, IReadOnlyList<UiCandidate> candidates) =>
        IsSettingsApplication(context.ApplicationName)
        && (context.WindowTitle?.Contains("Settings", StringComparison.OrdinalIgnoreCase) == true
            || context.WindowTitle?.Contains("설정", StringComparison.OrdinalIgnoreCase) == true
            || candidates.Any(item => RouteTargetMatches(item.Label, Routes[0].Targets)));

    private static bool IsSettingsApplication(string applicationName) =>
        string.Equals(applicationName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(applicationName, "SystemSettings", StringComparison.OrdinalIgnoreCase)
        || string.Equals(applicationName, "SystemSettings.exe", StringComparison.OrdinalIgnoreCase);

    private static UiCandidate? FindLive(IEnumerable<UiCandidate> candidates, IEnumerable<string> labels) =>
        candidates.FirstOrDefault(candidate =>
            candidate.Visible && candidate.Enabled && candidate.Clickable
            && RouteTargetMatches(candidate.Label, labels));

    private static bool RouteTargetMatches(string? candidateLabel, IEnumerable<string> labels) =>
        !string.IsNullOrWhiteSpace(candidateLabel)
        && labels.Any(label => string.Equals(
            Normalize(candidateLabel), Normalize(label), StringComparison.Ordinal));

    private static bool ContainsAny(string goal, IEnumerable<string> aliases)
    {
        var normalized = Normalize(goal);
        return aliases.Any(alias =>
        {
            var value = Normalize(alias);
            if (value.Length <= 3 && value.All(character => character <= 127))
                return Regex.IsMatch(normalized, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}])");
            return normalized.Contains(value, StringComparison.Ordinal);
        });
    }

    private static string Normalize(string? value) => string.Join(' ',
        (value ?? string.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

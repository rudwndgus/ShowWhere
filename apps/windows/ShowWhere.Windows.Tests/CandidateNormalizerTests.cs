using ShowWhere.Core;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class CandidateNormalizerTests
{
    [Fact]
    public void Keeps_actionable_windows_setting_sliders()
    {
        var normalized = CandidateNormalizer.Normalize([
            Candidate("brightness", "brightness", true, true, new UiBounds(10, 10, 220, 30)) with
            {
                Label = "Brightness",
                Role = "slider",
                ControlType = "ControlType.Slider",
            },
        ]);

        Assert.Single(normalized);
        Assert.Equal("slider", normalized[0].Candidate.Role);
    }

    [Fact]
    public void Normalize_filters_invisible_disabled_and_zero_size_controls()
    {
        var source = new[]
        {
            Candidate("visible", "visible", true, true, new UiBounds(10, 10, 100, 30)),
            Candidate("hidden", "hidden", true, false, new UiBounds(10, 10, 100, 30)),
            Candidate("disabled", "disabled", false, true, new UiBounds(10, 10, 100, 30)),
            Candidate("zero", "zero", true, true, new UiBounds(10, 10, 0, 30)),
        };

        var result = CandidateNormalizer.Normalize(source);

        Assert.Single(result);
        Assert.Equal("Settings", result[0].Candidate.Label);
    }

    [Fact]
    public void Normalize_deduplicates_children_resolving_to_the_same_clickable_parent()
    {
        var source = new[]
        {
            Candidate("text-child", "button-parent", true, true, new UiBounds(10, 10, 100, 30)),
            Candidate("icon-child", "button-parent", true, true, new UiBounds(10, 10, 100, 30)),
        };

        Assert.Single(CandidateNormalizer.Normalize(source));
    }

    [Fact]
    public void Normalize_generates_a_stable_candidate_id()
    {
        var source = new[] { Candidate("button", "button", true, true, new UiBounds(10, 10, 100, 30)) };

        var first = CandidateNormalizer.Normalize(source)[0].Candidate.Id;
        var second = CandidateNormalizer.Normalize(source)[0].Candidate.Id;

        Assert.Equal(first, second);
        Assert.StartsWith("win-", first);
    }

    [Fact]
    public void Normalize_never_exposes_password_descriptions()
    {
        var password = Candidate("password", "password", true, true, new UiBounds(10, 10, 100, 30)) with
        {
            Role = "edit",
            Label = "secret-value",
            Description = "sensitive help",
            IsPassword = true,
        };

        var result = CandidateNormalizer.Normalize([password])[0].Candidate;

        Assert.Equal("Password field", result.Label);
        Assert.Null(result.Description);
    }

    [Fact]
    public void Clickable_parent_resolution_uses_the_first_actionable_ancestor()
    {
        var result = ClickableParentResolver.Resolve([
            new AutomationAncestorDescriptor("text", "other", false),
            new AutomationAncestorDescriptor("button", "button", true),
            new AutomationAncestorDescriptor("pane", "pane", false),
        ]);

        Assert.Equal("button", result);
    }

    [Fact]
    public void Merge_reserves_space_for_global_taskbar_candidates()
    {
        var primary = Enumerable.Range(0, 100)
            .Select(index => Candidate($"primary-{index}", $"primary-{index}", true, true, new UiBounds(index, 10, 20, 20)))
            .ToArray();
        var taskbar = new[]
        {
            Candidate("network", "network", true, true, new UiBounds(1800, 1040, 40, 40)) with
            {
                Label = "Network ebluu.com",
                ProcessName = "explorer",
            },
        };

        var merged = CandidateNormalizer.MergeWithReservedSecondaryScope(
            CandidateNormalizer.Normalize(primary),
            CandidateNormalizer.Normalize(taskbar),
            maximumCandidates: 100,
            reservedSecondaryCandidates: 30);

        Assert.Equal(100, merged.Count);
        Assert.Contains(merged, item => item.Candidate.Label == "Network ebluu.com");
    }

    private static RawAutomationCandidate Candidate(
        string sourceKey,
        string clickableSourceKey,
        bool enabled,
        bool visible,
        UiBounds bounds) => new(
            sourceKey,
            clickableSourceKey,
            "Settings",
            null,
            "button",
            enabled,
            visible,
            true,
            bounds,
            false,
            "SettingsButton",
            "Button",
            "ControlType.Button",
            "notepad");
}

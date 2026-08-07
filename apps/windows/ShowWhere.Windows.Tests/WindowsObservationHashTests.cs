using ShowWhere.Core;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Windows.Tests;

public sealed class WindowsObservationHashTests
{
    [Fact]
    public void Taskbar_clock_changes_do_not_complete_the_current_guidance_step()
    {
        var context = new ApplicationContext(Platforms.Windows, "Code", "Visual Studio Code", Locale: "ko-KR");
        var first = WindowsObservation.ComputeHash(context, [
            Candidate("editor", "Editor", "foreground"),
            Candidate("clock", "Clock 11:30 AM", "windows_taskbar"),
        ], null);
        var second = WindowsObservation.ComputeHash(context, [
            Candidate("editor", "Editor", "foreground"),
            Candidate("clock", "Clock 11:31 AM", "windows_taskbar"),
        ], null);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Foreground_application_changes_remain_meaningful()
    {
        var context = new ApplicationContext(Platforms.Windows, "Code", "Visual Studio Code", Locale: "ko-KR");
        var first = WindowsObservation.ComputeHash(context, [Candidate("editor", "Editor", "foreground")], null);
        var second = WindowsObservation.ComputeHash(context, [Candidate("dialog", "Network settings", "foreground")], null);

        Assert.NotEqual(first, second);
    }

    private static UiCandidate Candidate(string id, string label, string scope) => new(
        id,
        label,
        null,
        "button",
        true,
        true,
        true,
        new UiBounds(10, 10, 100, 30),
        new Dictionary<string, object?> { ["sourceScope"] = scope });
}

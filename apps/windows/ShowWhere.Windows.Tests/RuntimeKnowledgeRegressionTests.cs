using System.Diagnostics;
using ShowWhere.Core;

namespace ShowWhere.Windows.Tests;

public sealed class RuntimeKnowledgeRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "showwhere-runtime-regression-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Repository_printer_gold_rejects_bad_targets_and_replays_the_verified_destination()
    {
        CopyTrainingFixture();
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var context = new ApplicationContext(Platforms.Windows, "ApplicationFrameHost", "Settings", Locale: "en-US");
        var candidates = new[]
        {
            Candidate("start-live", "Start", "Start", "explorer", "windows_taskbar"),
            Candidate("unknown-live", "unknown target", null, "ApplicationFrameHost", "foreground_application"),
            Candidate("printers-live", "Printers & scanners", null, "ApplicationFrameHost", "foreground_application"),
        };

        var filtered = store.FilterRejectedCandidates("프린터 설정은 어디서해?", context, candidates);
        Assert.DoesNotContain(filtered, item => item.Label == "Start");
        Assert.True(store.TryResolveTarget(
            "프린터 설정은 어디서해?", context, filtered, out var target, out var knowledge));
        Assert.Equal("Printers & scanners", target.Label);
        Assert.NotEqual("8ca45bd9-6048-4cab-a617-d2e80716b98f", knowledge.Id);
        Assert.NotEqual("adeacfe1-872a-4a98-b7f5-c080dc661cbc", knowledge.Id);
    }

    [Theory]
    [InlineData("윈도우 프린터 어디서해?")]
    [InlineData("프린터 설정 어디서해?")]
    [InlineData("프린터 설정 어디야?")]
    [InlineData("윈도우에서 프린터 어디서 설정해?")]
    public void Repository_printer_gold_resolves_the_live_Settings_home_menu(
        string goal)
    {
        CopyTrainingFixture();
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var context = new ApplicationContext(
            Platforms.Windows, "ApplicationFrameHost", "Settings", Locale: "en-US");
        var bluetooth = new UiCandidate(
            "live-settings-bluetooth",
            "Bluetooth & devices",
            null,
            "listitem",
            true,
            true,
            true,
            new UiBounds(16, 223, 280, 36),
            new Dictionary<string, object?>
            {
                ["className"] = "Microsoft.UI.Xaml.Controls.NavigationViewItem",
                ["controlType"] = "ControlType.ListItem",
                ["processName"] = "ApplicationFrameHost",
                ["sourceScope"] = "foreground_application",
            });

        Assert.True(store.TryResolveTarget(
            goal, context, [bluetooth], out var target, out var knowledge));
        Assert.Equal(bluetooth.Id, target.Id);
        Assert.Equal("bluetooth_&_devices", knowledge.HumanGold?.TargetConcept);
        Assert.Equal(bluetooth.Bounds, target.Bounds);
    }

    [Fact]
    public void Repository_Amazon_cart_gold_handles_paraphrase_quickly_but_rejects_order_history()
    {
        CopyTrainingFixture();
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var context = new ApplicationContext(
            Platforms.Windows, "chrome", "Amazon.com. Spend less. Smile more. - Google Chrome", "https://www.amazon.com/", "ko-KR");
        var cart = new UiCandidate(
            "cart-live", "0 items in cart", null, "link", true, true, true,
            new UiBounds(1700, 10, 120, 60),
            new Dictionary<string, object?>
            {
                ["automationId"] = "nav-cart",
                ["className"] = "nav-a nav-a-2 nav-progressive-attribute",
                ["controlType"] = "ControlType.Hyperlink",
                ["processName"] = "chrome",
                ["sourceScope"] = "browser_content",
                ["containerLabel"] = "Amazon.com. Spend less. Smile more.",
            });

        foreach (var goal in new[] { "장바구니 어디서 확인해?", "내 카트 보여줘" })
        {
            var timer = Stopwatch.StartNew();
            Assert.True(store.TryResolveTarget(goal, context, [cart], out var target, out var knowledge));
            timer.Stop();
            Assert.Equal("cart-live", target.Id);
            Assert.NotNull(knowledge.HumanGold);
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"Local Gold took {timer.Elapsed.TotalMilliseconds:N0}ms");
        }

        Assert.False(store.TryResolveTarget("주문 내역 어디서 봐?", context, [cart], out _, out _));
    }

    private void CopyTrainingFixture()
    {
        if (Directory.Exists(_directory)) return;
        Directory.CreateDirectory(_directory);
        var source = FindRepositoryTrainingDirectory();
        foreach (var name in new[] { "corrections.jsonl", "answer-feedback.jsonl", "completions.jsonl", "learning-status.jsonl", "learning-edits.jsonl" })
        {
            var path = Path.Combine(source, name);
            if (File.Exists(path)) File.Copy(path, Path.Combine(_directory, name));
        }
    }

    private static string FindRepositoryTrainingDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "package.json")))
                return Path.Combine(current.FullName, "training");
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository training directory was not found.");
    }

    private static UiCandidate Candidate(
        string id, string label, string? automationId, string processName, string sourceScope) => new(
        id, label, null, "button", true, true, true, new UiBounds(10, 10, 100, 40),
        new Dictionary<string, object?>
        {
            ["automationId"] = automationId,
            ["processName"] = processName,
            ["sourceScope"] = sourceScope,
        });

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

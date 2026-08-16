using System.Text.Json;
using ShowWhere.Core;

namespace ShowWhere.Windows.Tests;

public sealed class CentralLearningImportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "showwhere-central-import-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Central_records_import_once_and_are_immediately_available_locally()
    {
        var store = new JsonlDeveloperCorrectionStore(_directory);
        var feedback = new AnswerFeedbackRecord(
            1, "feedback-central", DateTimeOffset.UtcNow, "correct", "answer-1", "설정을 누르세요",
            "프린터 설정", "프린터 설정", null, null, "highlight", null, null, null);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(feedback, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.True(await store.ImportCentralRecordAsync("feedback", document.RootElement));
        Assert.False(await store.ImportCentralRecordAsync("feedback", document.RootElement));
        Assert.Single(store.GetHistory());

        var restored = new JsonlDeveloperCorrectionStore(_directory);
        Assert.Single(restored.GetHistory());
    }

    [Fact]
    public async Task Central_revocation_replaces_active_gold_and_cannot_reappear_after_restart()
    {
        var context = new ApplicationContext(Platforms.Windows, "chrome", "Amazon", "https://www.amazon.com/");
        var candidate = new UiCandidate(
            "cart", "Cart", null, "link", true, true, true, new UiBounds(10, 10, 100, 40),
            new Dictionary<string, object?>
            {
                ["automationId"] = "nav-cart",
                ["processName"] = "chrome",
                ["sourceScope"] = "browser_content",
                ["containerLabel"] = "Amazon",
            });
        var feedback = new AnswerFeedbackRecord(
            1, "feedback-cart", DateTimeOffset.UtcNow, "correct", "answer-cart", "Open Cart",
            "Show my cart", "Show my cart", context, "snapshot", GuideActions.Highlight,
            candidate.Id, candidate.Label, candidate.Bounds);
        var active = DeveloperPositiveFeedback.Create(
            feedback, DeveloperCorrectionMatcher.CreateSignature(candidate))! with { Id = "correction-cart" };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var store = new JsonlDeveloperCorrectionStore(_directory);

        Assert.True(await store.ImportCentralRecordAsync("correction", JsonSerializer.SerializeToElement(active, options)));
        Assert.True(store.TryResolveTarget("Show my cart", context, [candidate], out _, out _));

        var revoked = active with
        {
            HumanGold = null,
            KnowledgeStatus = DeveloperKnowledgePolicy.Revoked,
            InvalidReason = "central_revocation_test",
        };
        Assert.True(await store.ImportCentralRecordAsync("correction", JsonSerializer.SerializeToElement(revoked, options)));
        Assert.False(store.TryResolveTarget("Show my cart", context, [candidate], out _, out _));

        var restarted = new JsonlDeveloperCorrectionStore(_directory);
        Assert.False(restarted.TryResolveTarget("Show my cart", context, [candidate], out _, out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

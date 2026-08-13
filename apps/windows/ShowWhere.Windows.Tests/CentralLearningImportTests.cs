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

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

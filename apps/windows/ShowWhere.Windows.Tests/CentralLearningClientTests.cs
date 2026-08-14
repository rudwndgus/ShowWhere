using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ShowWhere.ApiClient;
using ShowWhere.Core;
using ShowWhere.Desktop;

namespace ShowWhere.Windows.Tests;

public sealed class CentralLearningClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "showwhere-central-client-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Corrupt_outbox_lines_are_quarantined_without_blocking_valid_uploads()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "central-sync-outbox.jsonl"), "{broken-json" + Environment.NewLine);
        var uploaded = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new DelegateHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            uploaded.TrySetResult(body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"cursor\":1,\"accepted\":1}") };
        }));
        using var client = CreateClient(http, "developer-token-at-least-24-characters");
        var feedback = CreateFeedback("feedback-upload");

        client.QueueUpload("feedback", feedback.Id, feedback.CreatedAtUtc, feedback);
        var requestBody = await uploaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => File.Exists(Path.Combine(_directory, "central-sync-outbox.jsonl"))
            && string.IsNullOrWhiteSpace(File.ReadAllText(Path.Combine(_directory, "central-sync-outbox.jsonl"))));

        using var document = JsonDocument.Parse(requestBody);
        Assert.Equal("feedback-upload", document.RootElement.GetProperty("records")[0].GetProperty("id").GetString());
        Assert.Contains("broken-json", await File.ReadAllTextAsync(Path.Combine(_directory, "central-sync-quarantine.jsonl")));
    }

    [Fact]
    public async Task Invalid_downloaded_record_does_not_block_later_valid_record_or_cursor()
    {
        var feedback = CreateFeedback("feedback-valid");
        var validPayload = JsonSerializer.SerializeToElement(feedback, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var responseBody = JsonSerializer.Serialize(new
        {
            cursor = 3,
            records = new object[]
            {
                new { id = "bad-envelope", kind = "feedback", version = 1, updatedAt = DateTimeOffset.UtcNow, payload = new { schemaVersion = 1, id = "wrong-id" } },
                new { id = "health-check", kind = "status", version = 2, updatedAt = DateTimeOffset.UtcNow, payload = new { schemaVersion = 1, id = "health-check", feedbackId = "__central_sync_health__", active = true } },
                new { id = feedback.Id, kind = "feedback", version = 3, updatedAt = feedback.CreatedAtUtc, payload = validPayload },
            },
        });
        using var http = new HttpClient(new DelegateHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        })));
        using var client = CreateClient(http);
        var store = new JsonlDeveloperCorrectionStore(_directory);

        await client.PullAsync(store);

        Assert.Single(store.GetHistory());
        Assert.Contains("\"cursor\":3", await File.ReadAllTextAsync(Path.Combine(_directory, "central-sync-state.json")));
    }

    [Fact]
    public async Task Server_cursor_rollback_resets_and_refetches_from_zero()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "central-sync-state.json"), "{\"cursor\":9}");
        var requestedUris = new List<string>();
        using var http = new HttpClient(new DelegateHandler(request =>
        {
            requestedUris.Add(request.RequestUri!.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"cursor\":0,\"records\":[]}", Encoding.UTF8, "application/json"),
            });
        }));
        using var client = CreateClient(http);
        var store = new JsonlDeveloperCorrectionStore(_directory);

        await client.PullAsync(store);

        Assert.Equal(["?cursor=9", "?cursor=0"], requestedUris);
        Assert.Contains("\"cursor\":0", await File.ReadAllTextAsync(Path.Combine(_directory, "central-sync-state.json")));
    }

    private CentralLearningClient CreateClient(HttpClient http, string? writeToken = null) => new(
        http,
        new GuideApiClientOptions(new Uri("https://showwhere.example/api/guide"), TimeSpan.FromSeconds(5), 1, "client-token-at-least-24-characters"),
        _directory,
        writeToken);

    private static AnswerFeedbackRecord CreateFeedback(string id) => new(
        1, id, DateTimeOffset.UtcNow, "correct", "answer-1", "correct answer",
        "printer settings", "printer settings", null, null, "highlight", null, null, null);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Condition was not reached.");
            await Task.Delay(25);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}

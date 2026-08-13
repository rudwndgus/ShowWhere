using System.Net.Http.Headers;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShowWhere.ApiClient;
using ShowWhere.Core;

namespace ShowWhere.Desktop;

internal sealed record CentralRecordEnvelope(
    string Id, string Kind, long Version, DateTimeOffset UpdatedAt, JsonElement Payload);

internal sealed record CentralSyncResponse(long Cursor, IReadOnlyList<CentralRecordEnvelope> Records);

internal sealed class CentralLearningClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string? _readToken;
    private readonly string? _writeToken;
    private readonly string _statePath;
    private readonly string _outboxPath;
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly object _outboxGate = new();
    private readonly CancellationTokenSource _lifetime = new();

    public CentralLearningClient(HttpClient http, GuideApiClientOptions options, string dataDirectory)
    {
        _http = http;
        _baseUri = new Uri(options.Endpoint.GetLeftPart(UriPartial.Authority));
        _readToken = options.ClientToken;
        _writeToken = (Environment.GetEnvironmentVariable("SHOWWHERE_SYNC_ACCESS_TOKEN")
            ?? Environment.GetEnvironmentVariable("SHOWWHERE_SYNC_ACCESS_TOKEN", EnvironmentVariableTarget.User))?.Trim();
        _statePath = Path.Combine(dataDirectory, "central-sync-state.json");
        _outboxPath = Path.Combine(dataDirectory, "central-sync-outbox.jsonl");
    }

    public async Task PullAsync(JsonlDeveloperCorrectionStore store, CancellationToken cancellationToken = default)
    {
        try
        {
            var cursor = ReadCursor();
            using var request = CreateRequest(HttpMethod.Get, $"api/knowledge/sync?cursor={cursor}", _readToken);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var changes = await JsonSerializer.DeserializeAsync<CentralSyncResponse>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidDataException("Empty central sync response.");
            var imported = 0;
            foreach (var record in changes.Records.OrderBy(item => item.Version))
                if (await store.ImportCentralRecordAsync(record.Kind, record.Payload, cancellationToken).ConfigureAwait(false)) imported++;
            WriteCursor(changes.Cursor);
            DesktopDiagnostics.WriteEvent("central_sync_pull", ("cursor", changes.Cursor), ("imported", imported));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DesktopDiagnostics.WriteEvent("central_sync_offline", ("reason", exception.GetType().Name));
        }
    }

    public void Start(JsonlDeveloperCorrectionStore store) => _ = Task.Run(async () =>
    {
        await PullAsync(store, _lifetime.Token).ConfigureAwait(false);
        await FlushAsync().ConfigureAwait(false);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
            {
                await PullAsync(store, _lifetime.Token).ConfigureAwait(false);
                await FlushAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    });

    public void QueueUpload<T>(string kind, string id, DateTimeOffset updatedAt, T payload)
    {
        if (string.IsNullOrWhiteSpace(_writeToken)) return;
        var envelope = new
        {
            id,
            kind,
            updatedAt = updatedAt.ToUniversalTime(),
            payload,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_outboxPath)!);
        lock (_outboxGate)
            File.AppendAllText(_outboxPath, JsonSerializer.Serialize(envelope, JsonOptions) + Environment.NewLine);
        _ = Task.Run(FlushAsync);
    }

    public async Task FlushAsync()
    {
        if (string.IsNullOrWhiteSpace(_writeToken) || !File.Exists(_outboxPath)) return;
        await _flushGate.WaitAsync().ConfigureAwait(false);
        try
        {
            string[] lines;
            lock (_outboxGate)
                lines = File.ReadAllLines(_outboxPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            if (lines.Length == 0) return;
            var records = lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line, JsonOptions)).ToArray();
            using var request = CreateRequest(HttpMethod.Post, "api/knowledge/records", _writeToken);
            request.Content = new StringContent(JsonSerializer.Serialize(new { records }, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            lock (_outboxGate)
            {
                var current = File.ReadAllLines(_outboxPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                File.WriteAllLines(_outboxPath, current.Skip(lines.Length));
            }
            DesktopDiagnostics.WriteEvent("central_sync_upload", ("records", lines.Length));
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.WriteEvent("central_sync_upload_deferred", ("reason", exception.GetType().Name));
        }
        finally { _flushGate.Release(); }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? token)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private long ReadCursor()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(_statePath), JsonOptions)?["cursor"] ?? 0; }
        catch { return 0; }
    }

    private void WriteCursor(long cursor)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(new { cursor }, JsonOptions));
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

internal sealed class SynchronizedDeveloperCorrectionStore(
    JsonlDeveloperCorrectionStore local,
    CentralLearningClient central) : IDeveloperCorrectionStore
{
    public string DataDirectory => local.DataDirectory;
    public string ResolveIntent(string originalGoal) => local.ResolveIntent(originalGoal);
    public bool TryResolveTarget(string goal, ApplicationContext context, IReadOnlyList<UiCandidate> candidates, out UiCandidate target, out DeveloperCorrectionRecord correction) => local.TryResolveTarget(goal, context, candidates, out target, out correction);
    public bool TryResolveVisualTarget(string goal, ApplicationContext context, string snapshotHash, out VisualTarget target, out DeveloperCorrectionRecord correction) => local.TryResolveVisualTarget(goal, context, snapshotHash, out target, out correction);
    public bool TryResolveCompletion(string goal, ApplicationContext context, IReadOnlyList<UiCandidate> candidates, out DeveloperCompletionRecord completion) => local.TryResolveCompletion(goal, context, candidates, out completion);
    public IReadOnlyList<UiCandidate> FilterRejectedCandidates(string goal, ApplicationContext context, IReadOnlyList<UiCandidate> candidates) => local.FilterRejectedCandidates(goal, context, candidates);
    public IReadOnlyList<DeveloperLearningHistoryRecord> GetHistory() => local.GetHistory();

    public async Task<DeveloperCorrectionRecord> SaveAsync(DeveloperCorrectionRecord record, string? screenshot, CancellationToken cancellationToken = default)
    { var saved = await local.SaveAsync(record, screenshot, cancellationToken); central.QueueUpload("correction", saved.Id, saved.CreatedAtUtc, saved); return saved; }
    public async Task<AnswerFeedbackRecord> SaveFeedbackAsync(AnswerFeedbackRecord record, CancellationToken cancellationToken = default)
    { var saved = await local.SaveFeedbackAsync(record, cancellationToken); central.QueueUpload("feedback", saved.Id, saved.CreatedAtUtc, saved); return saved; }
    public async Task<DeveloperCompletionRecord> SaveCompletionAsync(DeveloperCompletionRecord record, CancellationToken cancellationToken = default)
    { var saved = await local.SaveCompletionAsync(record, cancellationToken); central.QueueUpload("completion", saved.Id, saved.CreatedAtUtc, saved); return saved; }
    public async Task<DeveloperLearningEditRecord> SaveHistoryEditAsync(DeveloperLearningEditRecord record, CancellationToken cancellationToken = default)
    { var saved = await local.SaveHistoryEditAsync(record, cancellationToken); central.QueueUpload("edit", saved.Id, saved.CreatedAtUtc, saved); return saved; }
    public async Task SetFeedbackActiveAsync(string feedbackId, bool active, string? reason = null, CancellationToken cancellationToken = default)
    {
        await local.SetFeedbackActiveAsync(feedbackId, active, reason, cancellationToken);
        var history = local.GetHistory().First(item => item.FeedbackId == feedbackId);
        var record = new DeveloperLearningStatusRecord(1, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow, feedbackId, history.Active, reason);
        central.QueueUpload("status", record.Id, record.CreatedAtUtc, record);
    }
}

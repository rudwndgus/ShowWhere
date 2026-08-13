using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShowWhere.ApiClient;
using ShowWhere.Core;

namespace ShowWhere.Desktop;

internal sealed record CentralRecordEnvelope(
    string Id, string Kind, long Version, DateTimeOffset UpdatedAt, JsonElement Payload);

internal sealed record CentralSyncResponse(long Cursor, IReadOnlyList<CentralRecordEnvelope>? Records);

internal sealed class CentralLearningClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal)
    {
        "feedback", "correction", "completion", "status", "edit",
    };
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string? _readToken;
    private readonly string? _writeToken;
    private readonly string _statePath;
    private readonly string _outboxPath;
    private readonly string _quarantinePath;
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly object _outboxGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _backgroundTask;
    private bool _disposed;

    public CentralLearningClient(
        HttpClient http,
        GuideApiClientOptions options,
        string dataDirectory,
        string? writeTokenOverride = null)
    {
        _http = http;
        _baseUri = new Uri(options.Endpoint.GetLeftPart(UriPartial.Authority));
        _readToken = options.ClientToken;
        _writeToken = (writeTokenOverride
            ?? Environment.GetEnvironmentVariable("SHOWWHERE_SYNC_ACCESS_TOKEN")
            ?? Environment.GetEnvironmentVariable("SHOWWHERE_SYNC_ACCESS_TOKEN", EnvironmentVariableTarget.User))?.Trim();
        _statePath = Path.Combine(dataDirectory, "central-sync-state.json");
        _outboxPath = Path.Combine(dataDirectory, "central-sync-outbox.jsonl");
        _quarantinePath = Path.Combine(dataDirectory, "central-sync-quarantine.jsonl");
    }

    public async Task PullAsync(JsonlDeveloperCorrectionStore store, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            timeout.CancelAfter(RequestTimeout);
            var cursor = ReadCursor();
            using var request = CreateRequest(HttpMethod.Get, $"api/knowledge/sync?cursor={cursor}", _readToken);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var changes = await JsonSerializer.DeserializeAsync<CentralSyncResponse>(stream, JsonOptions, timeout.Token)
                .ConfigureAwait(false) ?? throw new InvalidDataException("Empty central sync response.");
            if (changes.Cursor < 0) throw new InvalidDataException("Central cursor is invalid.");
            if (changes.Cursor < cursor)
            {
                // A restored/replaced server volume may legitimately restart its sequence.
                // Reset once and fetch the new server history instead of remaining offline forever.
                WriteCursor(0);
                DesktopDiagnostics.WriteEvent("central_sync_cursor_reset", ("local", cursor), ("server", changes.Cursor));
                await PullAsync(store, cancellationToken).ConfigureAwait(false);
                return;
            }

            var imported = 0;
            var skipped = 0;
            foreach (var record in (changes.Records ?? Array.Empty<CentralRecordEnvelope>()).OrderBy(item => item.Version))
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (!IsValidEnvelope(record, changes.Cursor))
                {
                    skipped++;
                    continue;
                }
                try
                {
                    if (await store.ImportCentralRecordAsync(record.Kind, record.Payload, timeout.Token).ConfigureAwait(false)) imported++;
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidDataException or ArgumentException)
                {
                    skipped++;
                    DesktopDiagnostics.WriteEvent("central_sync_invalid_record", ("kind", record.Kind), ("reason", exception.GetType().Name));
                }
            }
            WriteCursor(changes.Cursor);
            DesktopDiagnostics.WriteEvent("central_sync_pull", ("cursor", changes.Cursor), ("imported", imported), ("skipped", skipped));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            DesktopDiagnostics.WriteEvent("central_sync_offline", ("reason", "Timeout"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DesktopDiagnostics.WriteEvent("central_sync_offline", ("reason", exception.GetType().Name));
        }
    }

    public void Start(JsonlDeveloperCorrectionStore store)
    {
        if (_backgroundTask is not null || _disposed) return;
        _backgroundTask = Task.Run(async () =>
        {
            await PullAsync(store, _lifetime.Token).ConfigureAwait(false);
            await FlushAsync(_lifetime.Token).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            try
            {
                while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
                {
                    await PullAsync(store, _lifetime.Token).ConfigureAwait(false);
                    await FlushAsync(_lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        });
        Observe(_backgroundTask);
    }

    public void QueueUpload<T>(string kind, string id, DateTimeOffset updatedAt, T payload)
    {
        if (string.IsNullOrWhiteSpace(_writeToken) || _disposed) return;
        try
        {
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
            Observe(Task.Run(() => FlushAsync(_lifetime.Token)));
        }
        catch (Exception exception)
        {
            // The verified local record is authoritative; a sync disk failure must never undo that save.
            DesktopDiagnostics.WriteEvent("central_sync_queue_failed", ("reason", exception.GetType().Name));
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_writeToken) || !File.Exists(_outboxPath) || _disposed) return;
        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] validLines;
            lock (_outboxGate)
            {
                var allLines = File.ReadAllLines(_outboxPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                var valid = new List<string>(allLines.Length);
                var invalid = new List<string>();
                foreach (var line in allLines)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(line);
                        if (IsValidQueuedRecord(document.RootElement)) valid.Add(line);
                        else invalid.Add(line);
                    }
                    catch (JsonException) { invalid.Add(line); }
                }
                if (invalid.Count > 0)
                {
                    File.AppendAllLines(_quarantinePath, invalid);
                    AtomicWriteAllLines(_outboxPath, valid);
                    DesktopDiagnostics.WriteEvent("central_sync_quarantined", ("records", invalid.Count));
                }
                validLines = valid.Take(100).ToArray();
            }
            if (validLines.Length == 0) return;
            var records = validLines.Select(line => JsonSerializer.Deserialize<JsonElement>(line, JsonOptions)).ToArray();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            timeout.CancelAfter(RequestTimeout);
            using var request = CreateRequest(HttpMethod.Post, "api/knowledge/records", _writeToken);
            request.Content = new StringContent(JsonSerializer.Serialize(new { records }, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            lock (_outboxGate)
            {
                var pending = File.Exists(_outboxPath)
                    ? File.ReadAllLines(_outboxPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToList()
                    : [];
                foreach (var uploaded in validLines)
                {
                    var index = pending.FindIndex(line => string.Equals(line, uploaded, StringComparison.Ordinal));
                    if (index >= 0) pending.RemoveAt(index);
                }
                AtomicWriteAllLines(_outboxPath, pending);
            }
            DesktopDiagnostics.WriteEvent("central_sync_upload", ("records", validLines.Length));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            DesktopDiagnostics.WriteEvent("central_sync_upload_deferred", ("reason", "Timeout"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
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

    private static bool IsValidEnvelope(CentralRecordEnvelope record, long responseCursor)
    {
        if (string.IsNullOrWhiteSpace(record.Id) || !KnownKinds.Contains(record.Kind)
            || record.Version <= 0 || record.Version > responseCursor || record.Payload.ValueKind != JsonValueKind.Object)
            return false;
        if (record.Payload.TryGetProperty("feedbackId", out var feedbackId)
            && feedbackId.ValueKind == JsonValueKind.String
            && feedbackId.GetString()?.StartsWith("__", StringComparison.Ordinal) == true)
            return false;
        return record.Payload.TryGetProperty("schemaVersion", out var schemaVersion)
            && schemaVersion.TryGetInt32(out var version) && version == 1
            && record.Payload.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String && string.Equals(id.GetString(), record.Id, StringComparison.Ordinal);
    }

    private static bool IsValidQueuedRecord(JsonElement record)
    {
        if (record.ValueKind != JsonValueKind.Object
            || !record.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())
            || !record.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || !KnownKinds.Contains(kind.GetString() ?? string.Empty)
            || !record.TryGetProperty("updatedAt", out var updatedAt) || updatedAt.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(updatedAt.GetString(), out _)
            || !record.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            return false;
        return payload.TryGetProperty("schemaVersion", out var schemaVersion)
            && schemaVersion.TryGetInt32(out var version) && version == 1
            && payload.TryGetProperty("id", out var payloadId) && payloadId.ValueKind == JsonValueKind.String
            && string.Equals(payloadId.GetString(), id.GetString(), StringComparison.Ordinal);
    }

    private long ReadCursor()
    {
        try
        {
            var cursor = JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(_statePath), JsonOptions)?["cursor"] ?? 0;
            return Math.Max(0, cursor);
        }
        catch { return 0; }
    }

    private void WriteCursor(long cursor)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        AtomicWriteText(_statePath, JsonSerializer.Serialize(new { cursor }, JsonOptions));
    }

    private static void AtomicWriteAllLines(string path, IEnumerable<string> lines)
    {
        var materialized = lines as IReadOnlyCollection<string> ?? lines.ToArray();
        AtomicWriteText(path, string.Join(Environment.NewLine, materialized)
            + (materialized.Count > 0 ? Environment.NewLine : string.Empty));
    }

    private static void AtomicWriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async void Observe(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { DesktopDiagnostics.Write(exception); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
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
        var record = new DeveloperLearningStatusRecord(
            1,
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow,
            feedbackId,
            history.Active,
            DeveloperLearningPrivacy.Redact(reason?.Trim()));
        central.QueueUpload("status", record.Id, record.CreatedAtUtc, record);
    }
}

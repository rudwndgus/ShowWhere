using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ShowWhere.Core;

namespace ShowWhere.ApiClient;

public sealed record TeachingInput(
    string UserQuestion,
    string CurrentStepDescription,
    string DeveloperCorrection,
    ApplicationContext? Context,
    IReadOnlyList<TeachingCandidate> Candidates);

public sealed record TeachingCandidate(string Label, string Role, bool Enabled);
public sealed record TeachingValidationIssue(string Code, string Path, string Message, string Severity);
public sealed record TeachingValidationResult(
    bool Valid,
    double Confidence,
    IReadOnlyList<TeachingValidationIssue> Issues,
    string ShortReason);

public interface ITeachingApiClient
{
    Task<string> AnalyzeAsync(TeachingInput input, CancellationToken cancellationToken);
    Task<TeachingValidationResult> ValidateAsync(string editableJson, CancellationToken cancellationToken);
    Task<string> SaveApprovedAsync(string editableJson, string approvedBy, CancellationToken cancellationToken);
}

public sealed class TeachingApiClient : ITeachingApiClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _analyzeEndpoint;
    private readonly Uri _validateEndpoint;
    private readonly Uri _goldEndpoint;
    private readonly TimeSpan _timeout;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public TeachingApiClient(HttpClient httpClient, GuideApiClientOptions options)
    {
        _httpClient = httpClient;
        var origin = new Uri(options.Endpoint.GetLeftPart(UriPartial.Authority));
        _analyzeEndpoint = new Uri(origin, "/api/teaching/analyze");
        _validateEndpoint = new Uri(origin, "/api/teaching/validate");
        _goldEndpoint = new Uri(origin, "/api/teaching/gold");
        _timeout = options.Timeout;
    }

    public async Task<string> AnalyzeAsync(TeachingInput input, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var response = await _httpClient.PostAsJsonAsync(_analyzeEndpoint, input, _jsonOptions, timeout.Token);
        return await ReadPrettyJsonAsync(response, timeout.Token);
    }

    public async Task<TeachingValidationResult> ValidateAsync(string editableJson, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var document = JsonDocument.Parse(editableJson);
        using var content = new StringContent(document.RootElement.GetRawText(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(_validateEndpoint, content, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new GuideApiException();
        var result = await response.Content.ReadFromJsonAsync<TeachingValidationResult>(_jsonOptions, timeout.Token);
        return result ?? throw new GuideApiException();
    }

    public async Task<string> SaveApprovedAsync(string editableJson, string approvedBy, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var document = JsonDocument.Parse(editableJson);
        var body = JsonSerializer.Serialize(new { record = document.RootElement, approvedBy }, _jsonOptions);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(_goldEndpoint, content, timeout.Token);
        return await ReadPrettyJsonAsync(response, timeout.Token);
    }

    private async Task<string> ReadPrettyJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode) throw new GuideApiException();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return JsonSerializer.Serialize(document.RootElement, _jsonOptions);
    }
}

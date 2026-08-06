using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShowWhere.Core;

namespace ShowWhere.ApiClient;

public sealed record GuideApiClientOptions(Uri Endpoint, TimeSpan Timeout, int MaxRetries = 1)
{
    public static GuideApiClientOptions FromEnvironment()
    {
        var endpoint = Environment.GetEnvironmentVariable("SHOWWHERE_BACKEND_URL")
            ?? "http://127.0.0.1:8787/api/guide";
        var timeoutText = Environment.GetEnvironmentVariable("SHOWWHERE_BACKEND_TIMEOUT_SECONDS");
        var seconds = int.TryParse(timeoutText, out var parsed) ? Math.Clamp(parsed, 5, 180) : 75;
        return new GuideApiClientOptions(new Uri(endpoint, UriKind.Absolute), TimeSpan.FromSeconds(seconds));
    }
}

public sealed class GuideApiException : Exception
{
    public GuideApiException() : base("지금은 안내 서비스에 연결할 수 없어요. 잠시 후 다시 시도해 주세요.") { }
}

public interface IGuideApiClient
{
    Task<GuideDecision> DecideNextActionAsync(GuideRequest request, CancellationToken cancellationToken);
}

public sealed class GuideApiClient : IGuideApiClient
{
    private readonly HttpClient _httpClient;
    private readonly GuideApiClientOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GuideApiClient(HttpClient httpClient, GuideApiClientOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<GuideDecision> DecideNextActionAsync(
        GuideRequest request,
        CancellationToken cancellationToken)
    {
        ContractValidator.Validate(request);
        var serializedRequest = JsonSerializer.Serialize(request, _jsonOptions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                using var content = new StringContent(serializedRequest, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(_options.Endpoint, content, timeout.Token).ConfigureAwait(false);
                if (IsTransient(response.StatusCode) && attempt < _options.MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), timeout.Token).ConfigureAwait(false);
                    continue;
                }

                await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                var decision = await JsonSerializer.DeserializeAsync<GuideDecision>(responseStream, _jsonOptions, timeout.Token)
                    .ConfigureAwait(false);
                if (decision is null) throw new GuideApiException();
                var validated = ContractValidator.ValidateDecision(decision, request);
                if (!response.IsSuccessStatusCode && validated.Action != GuideActions.AskUser)
                    throw new GuideApiException();
                return validated;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (GuideApiException)
            {
                throw;
            }
            catch when (attempt < _options.MaxRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                throw new GuideApiException();
            }
        }

        throw new GuideApiException();
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 || (int)statusCode >= 500;
}

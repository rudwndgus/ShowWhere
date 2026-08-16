using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShowWhere.Core;

namespace ShowWhere.ApiClient;

public sealed record GuideApiClientOptions(Uri Endpoint, TimeSpan Timeout, int MaxRetries = 1, string? ClientToken = null)
{
    public static GuideApiClientOptions FromEnvironment()
    {
        var endpoint = Environment.GetEnvironmentVariable("SHOWWHERE_BACKEND_URL")
            ?? ReadDeploymentSetting("ShowWhereBackendUrl")
            ?? "https://api-production-6901.up.railway.app/api/guide";
        var timeoutText = Environment.GetEnvironmentVariable("SHOWWHERE_BACKEND_TIMEOUT_SECONDS");
        var seconds = int.TryParse(timeoutText, out var parsed) ? Math.Clamp(parsed, 5, 180) : 75;
        var clientToken = Environment.GetEnvironmentVariable("SHOWWHERE_CLIENT_TOKEN")
            ?? ReadDeploymentSetting("ShowWhereClientToken");
        return new GuideApiClientOptions(
            new Uri(endpoint, UriKind.Absolute),
            TimeSpan.FromSeconds(seconds),
            ClientToken: string.IsNullOrWhiteSpace(clientToken) ? null : clientToken.Trim());
    }

    private static string? ReadDeploymentSetting(string key) => Assembly.GetEntryAssembly()?
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .LastOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal))?.Value;
}

public sealed class GuideApiException : Exception
{
    public GuideApiException() : base("The guidance service is unavailable right now. Please try again shortly.") { }
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
                using var message = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
                {
                    Content = new StringContent(serializedRequest, Encoding.UTF8, "application/json"),
                };
                if (!string.IsNullOrWhiteSpace(_options.ClientToken))
                    message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ClientToken);
                using var response = await _httpClient.SendAsync(message, timeout.Token).ConfigureAwait(false);
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
                if (!response.IsSuccessStatusCode
                    && validated.Action != GuideActions.AskUser
                    && validated.Status != GuideStatuses.Blocked)
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

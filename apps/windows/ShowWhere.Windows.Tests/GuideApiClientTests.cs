using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ShowWhere.ApiClient;
using ShowWhere.Core;

namespace ShowWhere.Windows.Tests;

public sealed class GuideApiClientTests
{
    [Fact]
    public async Task Sends_configured_client_token_as_bearer_authorization()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new GuideDecision(
                GuideStatuses.InProgress, GuideActions.Highlight, "next", 0.9, "candidate-1")),
        });
        var client = new GuideApiClient(
            new HttpClient(handler),
            new GuideApiClientOptions(new Uri("https://showwhere.example/api/guide"), TimeSpan.FromSeconds(5), 0, "client-token"));

        await client.DecideNextActionAsync(CoreContractTests.CreateRequest(), CancellationToken.None);

        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("client-token", handler.Authorization?.Parameter);
    }

    [Fact]
    public async Task Client_serializes_windows_contract_and_validates_response()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"status":"in_progress","action":"highlight","targetId":"candidate-1","message":"Select Settings.","confidence":0.9}""",
                Encoding.UTF8,
                "application/json"),
        });
        var client = new GuideApiClient(
            new HttpClient(handler),
            new GuideApiClientOptions(new Uri("http://localhost/api/guide"), TimeSpan.FromSeconds(5), 0));

        var decision = await client.DecideNextActionAsync(CoreContractTests.CreateRequest(), CancellationToken.None);

        Assert.Equal("candidate-1", decision.TargetId);
        using var requestJson = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("windows", requestJson.RootElement.GetProperty("context").GetProperty("platform").GetString());
        Assert.False(requestJson.RootElement.TryGetProperty("screenshot", out _));
    }

    [Fact]
    public async Task Client_rejects_unknown_target_from_backend()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"status":"in_progress","action":"highlight","targetId":"made-up","message":"Select it.","confidence":0.99}""",
                Encoding.UTF8,
                "application/json"),
        });
        var client = new GuideApiClient(
            new HttpClient(handler),
            new GuideApiClientOptions(new Uri("http://localhost/api/guide"), TimeSpan.FromSeconds(5), 0));

        await Assert.ThrowsAsync<GuideApiException>(() =>
            client.DecideNextActionAsync(CoreContractTests.CreateRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Client_surfaces_safe_blocked_server_errors()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(
                """{"status":"blocked","action":"explain","message":"요청이 너무 많아요. 잠시 후 다시 시도해 주세요.","confidence":1}""",
                Encoding.UTF8,
                "application/json"),
        });
        var client = new GuideApiClient(
            new HttpClient(handler),
            new GuideApiClientOptions(new Uri("https://showwhere.example/api/guide"), TimeSpan.FromSeconds(5), 0));

        var decision = await client.DecideNextActionAsync(CoreContractTests.CreateRequest(), CancellationToken.None);

        Assert.Equal(GuideStatuses.Blocked, decision.Status);
        Assert.Contains("요청이 너무 많아요", decision.Message);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization;
            return responseFactory(request);
        }
    }
}

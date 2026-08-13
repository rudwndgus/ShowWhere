using System.Collections.Specialized;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShowWhere.ApiClient;

namespace ShowWhere.Desktop;

public sealed class MobileRemoteCoordinator : INotifyPropertyChanged, IDisposable
{
    private sealed record PairingResponse(
        string SessionId, string DesktopSecret, string Code, DateTimeOffset ExpiresAt, string QrDataUrl);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string? _clientToken;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly DispatcherTimer _timer;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _sessionCancellation;
    private PairingWindow? _window;
    private string? _sessionId;
    private DateTimeOffset _expiresAt;
    private string _pairingCode = "------";
    private string _pairingStatus = "연결 준비 중…";
    private int _remainingSeconds;
    private BitmapImage? _qrImage;
    private bool _isConnected;
    private bool _refreshing;
    private GuidanceViewModel? _viewModel;

    public MobileRemoteCoordinator(HttpClient http, GuideApiClientOptions options, Dispatcher dispatcher)
    {
        _http = http;
        _baseUri = new Uri(options.Endpoint.GetLeftPart(UriPartial.Authority));
        _clientToken = options.ClientToken;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnTimer, dispatcher);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value); }
    public string ConnectionText => IsConnected ? "모바일 연결됨" : "모바일 연결";
    public string PairingCode { get => _pairingCode; private set => Set(ref _pairingCode, value); }
    public string PairingStatus { get => _pairingStatus; private set => Set(ref _pairingStatus, value); }
    public int RemainingSeconds { get => _remainingSeconds; private set => Set(ref _remainingSeconds, value); }
    public double RemainingProgress => Math.Clamp(RemainingSeconds / 60d * 100d, 0, 100);
    public BitmapImage? QrImage { get => _qrImage; private set => Set(ref _qrImage, value); }

    public void Attach(GuidanceViewModel viewModel)
    {
        _viewModel = viewModel;
        viewModel.Messages.CollectionChanged += OnMessagesChanged;
        foreach (var message in viewModel.Messages) message.PropertyChanged += OnMessageChanged;
    }

    public async Task ShowPairingAsync()
    {
        if (IsConnected)
        {
            PairingStatus = "이미 휴대폰과 연결되어 있어요.";
            return;
        }
        if (_window is null)
        {
            _window = new PairingWindow { DataContext = this };
            _window.Closed += (_, _) =>
            {
                _window = null;
                if (!IsConnected) _ = DisconnectAsync();
            };
            _window.Show();
        }
        else { _window.Show(); _window.Activate(); }
        await RefreshSessionAsync().ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        var id = _sessionId;
        _sessionCancellation?.Cancel();
        if (_socket is not null)
        {
            try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "desktop_disconnect", CancellationToken.None); }
            catch { }
            _socket.Dispose();
            _socket = null;
        }
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                using var request = CreateRequest(HttpMethod.Delete, $"api/pairing/sessions/{Uri.EscapeDataString(id)}");
                using var _ = await _http.SendAsync(request).ConfigureAwait(false);
            }
            catch { }
        }
        await _dispatcher.InvokeAsync(() =>
        {
            IsConnected = false;
            OnPropertyChanged(nameof(ConnectionText));
            PairingStatus = "연결이 종료됐어요.";
            _timer.Stop();
        });
    }

    private async Task RefreshSessionAsync()
    {
        if (_refreshing || IsConnected) return;
        _refreshing = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);
            using var request = CreateRequest(HttpMethod.Post, "api/pairing/sessions");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var pairing = await JsonSerializer.DeserializeAsync<PairingResponse>(stream, JsonOptions).ConfigureAwait(false)
                ?? throw new InvalidDataException("Empty pairing response.");
            _sessionId = pairing.SessionId;
            _expiresAt = pairing.ExpiresAt;
            var image = DecodeImage(pairing.QrDataUrl);
            await _dispatcher.InvokeAsync(() =>
            {
                PairingCode = pairing.Code;
                QrImage = image;
                PairingStatus = "휴대폰으로 QR을 스캔한 뒤 이 번호를 입력하세요.";
                UpdateCountdown();
                _timer.Start();
            });
            _sessionCancellation = new CancellationTokenSource();
            _socket = new ClientWebSocket();
            var wsScheme = _baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
            var socketUri = new UriBuilder(_baseUri)
            {
                Scheme = wsScheme,
                Path = "/api/pairing/ws",
                Query = $"role=desktop&sessionId={Uri.EscapeDataString(pairing.SessionId)}&secret={Uri.EscapeDataString(pairing.DesktopSecret)}",
            }.Uri;
            await _socket.ConnectAsync(socketUri, _sessionCancellation.Token).ConfigureAwait(false);
            _ = ReceiveLoopAsync(_socket, _sessionCancellation.Token);
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            await _dispatcher.InvokeAsync(() => PairingStatus = "모바일 연결 서버에 닿지 못했어요. 잠시 후 다시 눌러 주세요.");
        }
        finally { _refreshing = false; }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[32_768];
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var memory = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    memory.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                using var document = JsonDocument.Parse(memory.ToArray());
                var root = document.RootElement;
                var type = root.GetProperty("type").GetString();
                if (type == "connection_status")
                {
                    var connected = root.GetProperty("connected").GetBoolean();
                    await _dispatcher.InvokeAsync(() =>
                    {
                        IsConnected = connected;
                        OnPropertyChanged(nameof(ConnectionText));
                        PairingStatus = connected ? "휴대폰과 연결됐어요." : "휴대폰 연결을 기다리는 중…";
                        if (connected) { _timer.Stop(); _window?.Close(); }
                    });
                }
                else if (type == "user_message" && _viewModel is not null)
                {
                    var text = root.GetProperty("text").GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        await _dispatcher.InvokeAsync(() => _viewModel.SubmitRemoteAsync(text)).Task.Unwrap();
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException) { }
        finally
        {
            await _dispatcher.InvokeAsync(() =>
            {
                IsConnected = false;
                OnPropertyChanged(nameof(ConnectionText));
            });
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.NewItems is null) return;
        foreach (ChatMessageItem message in eventArgs.NewItems)
        {
            message.PropertyChanged += OnMessageChanged;
            _ = SendChatAsync(message);
        }
    }

    private void OnMessageChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is ChatMessageItem message && eventArgs.PropertyName is nameof(ChatMessageItem.Text) or nameof(ChatMessageItem.IsPending))
            _ = SendChatAsync(message);
    }

    private async Task SendChatAsync(ChatMessageItem message)
    {
        var socket = _socket;
        if (!IsConnected || socket?.State != WebSocketState.Open) return;
        var json = JsonSerializer.Serialize(new
        {
            type = "chat_message",
            id = message.Id,
            role = message.Role,
            text = message.Text,
            isPending = message.IsPending,
        }, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendGate.WaitAsync().ConfigureAwait(false);
        try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false); }
        catch (WebSocketException) { }
        finally { _sendGate.Release(); }
    }

    private void OnTimer(object? sender, EventArgs eventArgs)
    {
        UpdateCountdown();
        if (RemainingSeconds <= 0 && !IsConnected) _ = RefreshSessionAsync();
    }

    private void UpdateCountdown()
    {
        RemainingSeconds = Math.Max(0, (int)Math.Ceiling((_expiresAt - DateTimeOffset.UtcNow).TotalSeconds));
        OnPropertyChanged(nameof(RemainingProgress));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        if (!string.IsNullOrWhiteSpace(_clientToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _clientToken);
        return request;
    }

    private static BitmapImage DecodeImage(string dataUrl)
    {
        var separator = dataUrl.IndexOf(',');
        var bytes = Convert.FromBase64String(dataUrl[(separator + 1)..]);
        using var memory = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = memory; image.EndInit(); image.Freeze();
        return image;
    }

    public void Dispose()
    {
        if (_viewModel is not null) _viewModel.Messages.CollectionChanged -= OnMessagesChanged;
        _sessionCancellation?.Cancel();
        _socket?.Dispose();
        _timer.Stop();
        _sendGate.Dispose();
        _sessionCancellation?.Dispose();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(propertyName); return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new(propertyName));
}

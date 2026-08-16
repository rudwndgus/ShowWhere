using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
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
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(5);
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string? _clientToken;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Channel<string> _remoteMessages = Channel.CreateBounded<string>(new BoundedChannelOptions(20)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = true,
    });
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _sessionCancellation;
    private CancellationTokenSource? _operationCancellation;
    private PairingWindow? _window;
    private string? _sessionId;
    private string? _desktopSecret;
    private DateTimeOffset _expiresAt;
    private int _pairingLifetimeSeconds = 60;
    private string _pairingCode = "------";
    private string _pairingStatus = "Preparing connection…";
    private int _remainingSeconds;
    private BitmapImage? _qrImage;
    private bool _isConnected;
    private bool _disposed;
    private int _refreshing;
    private GuidanceViewModel? _viewModel;

    public MobileRemoteCoordinator(HttpClient http, GuideApiClientOptions options, Dispatcher dispatcher)
    {
        _http = http;
        _baseUri = new Uri(options.Endpoint.GetLeftPart(UriPartial.Authority));
        _clientToken = options.ClientToken;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnTimer, dispatcher);
        Observe(ProcessRemoteMessagesAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value); }
    public string ConnectionText => IsConnected ? "Phone connected" : "Connect phone";
    public string PairingCode { get => _pairingCode; private set => Set(ref _pairingCode, value); }
    public string PairingStatus { get => _pairingStatus; private set => Set(ref _pairingStatus, value); }
    public int RemainingSeconds { get => _remainingSeconds; private set => Set(ref _remainingSeconds, value); }
    public double RemainingProgress => Math.Clamp(RemainingSeconds / (double)Math.Max(1, _pairingLifetimeSeconds) * 100d, 0, 100);
    public BitmapImage? QrImage { get => _qrImage; private set => Set(ref _qrImage, value); }

    public void Attach(GuidanceViewModel viewModel)
    {
        _viewModel = viewModel;
        viewModel.Messages.CollectionChanged += OnMessagesChanged;
        foreach (var message in viewModel.Messages) message.PropertyChanged += OnMessageChanged;
    }

    public async Task ShowPairingAsync()
    {
        if (_disposed) return;
        if (IsConnected)
        {
            await DisconnectAsync();
            if (_disposed) return;
        }
        if (_window is null)
        {
            _window = new PairingWindow { DataContext = this };
            _window.Closed += OnPairingWindowClosed;
            _window.Show();
        }
        else
        {
            _window.Show();
            _window.Activate();
        }
        if (!string.IsNullOrWhiteSpace(_sessionId)
            && _socket?.State == WebSocketState.Open
            && RemainingSeconds > 0)
            return;
        await RefreshSessionAsync().ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        CancelCurrentOperation();
        await UpdateDisconnectedUiAsync("The connection has ended.").ConfigureAwait(false);
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try { await CleanupSessionCoreAsync(notifyServer: true).ConfigureAwait(false); }
        finally { _lifecycleGate.Release(); }
        DesktopDiagnostics.WriteEvent("mobile_pairing_disconnected");
    }

    private void OnPairingWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (_window is not null) _window.Closed -= OnPairingWindowClosed;
        _window = null;
        if (IsConnected) return;
        CancelCurrentOperation();
        Observe(DisconnectAsync());
    }

    private async Task RefreshSessionAsync()
    {
        if (_disposed || IsConnected) return;
        if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        var acquired = false;
        try
        {
            await _lifecycleGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
            acquired = true;
            if (_disposed || IsConnected) return;
            await CleanupSessionCoreAsync(notifyServer: true).ConfigureAwait(false);
            if (!await HasOpenPairingWindowAsync().ConfigureAwait(false)) return;

            using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            operation.CancelAfter(RequestTimeout);
            _operationCancellation = operation;
            DesktopDiagnostics.WriteEvent("mobile_pairing_create_started", ("server", _baseUri.Host));
            using var request = CreateRequest(HttpMethod.Post, "api/pairing/sessions");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, operation.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(operation.Token).ConfigureAwait(false);
            var pairing = await JsonSerializer.DeserializeAsync<PairingResponse>(stream, JsonOptions, operation.Token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Empty pairing response.");
            ValidatePairingResponse(pairing);

            _sessionId = pairing.SessionId;
            _desktopSecret = pairing.DesktopSecret;
            _expiresAt = pairing.ExpiresAt;
            _pairingLifetimeSeconds = Math.Max(1, (int)Math.Ceiling((_expiresAt - DateTimeOffset.UtcNow).TotalSeconds));
            var image = DecodeImage(pairing.QrDataUrl);
            if (!await HasOpenPairingWindowAsync().ConfigureAwait(false))
            {
                await CleanupSessionCoreAsync(notifyServer: true).ConfigureAwait(false);
                return;
            }
            await _dispatcher.InvokeAsync(() =>
            {
                PairingCode = pairing.Code;
                QrImage = image;
                PairingStatus = "Scan the QR code with your phone and enter the verification code.";
                UpdateCountdown();
                _timer.Start();
            });

            _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol("showwhere-v1");
            socket.Options.AddSubProtocol(pairing.DesktopSecret);
            _socket = socket;
            var wsScheme = _baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
            var socketUri = new UriBuilder(_baseUri)
            {
                Scheme = wsScheme,
                Path = "/api/pairing/ws",
                Query = $"role=desktop&sessionId={Uri.EscapeDataString(pairing.SessionId)}",
            }.Uri;
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellation.Token);
            connectTimeout.CancelAfter(RequestTimeout);
            await socket.ConnectAsync(socketUri, connectTimeout.Token).ConfigureAwait(false);
            DesktopDiagnostics.WriteEvent("mobile_pairing_socket_ready");
            Observe(ReceiveLoopAsync(socket, _sessionCancellation.Token));
        }
        catch (OperationCanceledException) when (!_lifetimeCancellation.IsCancellationRequested)
        {
            await SetPairingFailureAsync("The connection timed out. Please try again shortly.").ConfigureAwait(false);
            await CleanupSessionCoreAsync(notifyServer: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            await SetPairingFailureAsync("The mobile connection server could not be reached. Please try again shortly.").ConfigureAwait(false);
            await CleanupSessionCoreAsync(notifyServer: true).ConfigureAwait(false);
        }
        finally
        {
            _operationCancellation = null;
            if (acquired) _lifecycleGate.Release();
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8_192];
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
                    if (memory.Length + result.Count > 32_768) throw new InvalidDataException("Mobile message exceeded limit.");
                    memory.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                if (result.MessageType != WebSocketMessageType.Text) continue;
                JsonDocument document;
                try { document = JsonDocument.Parse(memory.ToArray()); }
                catch (JsonException)
                {
                    DesktopDiagnostics.WriteEvent("mobile_pairing_invalid_message");
                    continue;
                }
                using (document)
                {
                    var root = document.RootElement;
                    if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String) continue;
                    var type = typeElement.GetString();
                    if (type == "connection_status"
                        && root.TryGetProperty("connected", out var connectedElement)
                        && connectedElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        var connected = connectedElement.GetBoolean();
                        await _dispatcher.InvokeAsync(() =>
                        {
                            IsConnected = connected;
                            OnPropertyChanged(nameof(ConnectionText));
                            PairingStatus = connected ? "Your phone is connected." : "Waiting for the phone to connect…";
                            if (connected)
                            {
                                _timer.Stop();
                                _window?.Close();
                            }
                        });
                        DesktopDiagnostics.WriteEvent("mobile_pairing_status", ("connected", connected));
                    }
                    else if (type == "user_message"
                        && root.TryGetProperty("text", out var textElement)
                        && textElement.ValueKind == JsonValueKind.String)
                    {
                        var text = textElement.GetString();
                        if (!string.IsNullOrWhiteSpace(text) && !_remoteMessages.Writer.TryWrite(text))
                            DesktopDiagnostics.WriteEvent("mobile_message_queue_full");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException exception) { DesktopDiagnostics.WriteEvent("mobile_pairing_socket_closed", ("code", exception.WebSocketErrorCode)); }
        catch (Exception exception) { DesktopDiagnostics.Write(exception); }
        finally
        {
            if (ReferenceEquals(_socket, socket))
                await UpdateDisconnectedUiAsync("The phone connection has ended.").ConfigureAwait(false);
        }
    }

    private async Task ProcessRemoteMessagesAsync()
    {
        try
        {
            await foreach (var text in _remoteMessages.Reader.ReadAllAsync(_lifetimeCancellation.Token).ConfigureAwait(false))
            {
                var viewModel = _viewModel;
                if (viewModel is null) continue;
                await _dispatcher.InvokeAsync(
                    () => viewModel.SubmitRemoteAsync(text, _lifetimeCancellation.Token)).Task.Unwrap().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { DesktopDiagnostics.Write(exception); }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.OldItems is not null)
            foreach (ChatMessageItem message in eventArgs.OldItems) message.PropertyChanged -= OnMessageChanged;
        if (eventArgs.NewItems is null) return;
        foreach (ChatMessageItem message in eventArgs.NewItems)
        {
            message.PropertyChanged += OnMessageChanged;
            Observe(SendChatAsync(message));
        }
    }

    private void OnMessageChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is ChatMessageItem message && eventArgs.PropertyName is nameof(ChatMessageItem.Text) or nameof(ChatMessageItem.IsPending))
            Observe(SendChatAsync(message));
    }

    private async Task SendChatAsync(ChatMessageItem message)
    {
        var socket = _socket;
        var cancellationToken = _sessionCancellation?.Token ?? CancellationToken.None;
        if (!IsConnected || socket?.State != WebSocketState.Open || cancellationToken.IsCancellationRequested) return;
        var json = JsonSerializer.Serialize(new
        {
            type = "chat_message",
            id = message.Id,
            role = message.Role,
            text = message.Text,
            isPending = message.IsPending,
        }, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_socket, socket) && socket.State == WebSocketState.Open)
                    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally { _sendGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException exception) { DesktopDiagnostics.WriteEvent("mobile_chat_send_failed", ("code", exception.WebSocketErrorCode)); }
        catch (Exception exception) { DesktopDiagnostics.Write(exception); }
    }

    private void OnTimer(object? sender, EventArgs eventArgs)
    {
        UpdateCountdown();
        if (RemainingSeconds <= 0 && !IsConnected) Observe(RefreshSessionAsync());
    }

    private void UpdateCountdown()
    {
        RemainingSeconds = Math.Max(0, (int)Math.Ceiling((_expiresAt - DateTimeOffset.UtcNow).TotalSeconds));
        OnPropertyChanged(nameof(RemainingProgress));
    }

    private async Task CleanupSessionCoreAsync(bool notifyServer)
    {
        var sessionCancellation = _sessionCancellation;
        var socket = _socket;
        var id = _sessionId;
        var secret = _desktopSecret;
        _sessionCancellation = null;
        _socket = null;
        _sessionId = null;
        _desktopSecret = null;
        sessionCancellation?.Cancel();
        if (socket is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "desktop_disconnect", timeout.Token).ConfigureAwait(false);
            }
            catch { }
            socket.Dispose();
        }
        sessionCancellation?.Dispose();
        if (!notifyServer || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            timeout.CancelAfter(DisconnectTimeout);
            using var request = CreateRequest(HttpMethod.Delete, $"api/pairing/sessions/{Uri.EscapeDataString(id)}");
            request.Headers.TryAddWithoutValidation("X-ShowWhere-Pairing-Secret", secret);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            DesktopDiagnostics.WriteEvent("mobile_pairing_server_cleanup", ("status", (int)response.StatusCode));
        }
        catch (OperationCanceledException) { DesktopDiagnostics.WriteEvent("mobile_pairing_server_cleanup_timeout"); }
        catch (Exception exception) { DesktopDiagnostics.Write(exception); }
    }

    private Task<bool> HasOpenPairingWindowAsync() => _dispatcher.InvokeAsync(() => _window is { IsVisible: true }).Task;

    private Task SetPairingFailureAsync(string message) => _dispatcher.InvokeAsync(() =>
    {
        PairingStatus = message;
        PairingCode = "------";
        QrImage = null;
        _timer.Stop();
    }).Task;

    private Task UpdateDisconnectedUiAsync(string message) => _dispatcher.InvokeAsync(() =>
    {
        IsConnected = false;
        OnPropertyChanged(nameof(ConnectionText));
        PairingStatus = message;
        _timer.Stop();
    }).Task;

    private void CancelCurrentOperation()
    {
        try { _operationCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        try { _sessionCancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        if (!string.IsNullOrWhiteSpace(_clientToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _clientToken);
        return request;
    }

    private static void ValidatePairingResponse(PairingResponse pairing)
    {
        if (string.IsNullOrWhiteSpace(pairing.SessionId) || string.IsNullOrWhiteSpace(pairing.DesktopSecret)
            || pairing.Code.Length != 6 || pairing.Code.Any(character => !char.IsAsciiDigit(character))
            || pairing.ExpiresAt <= DateTimeOffset.UtcNow || !pairing.QrDataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid pairing response.");
    }

    private static BitmapImage DecodeImage(string dataUrl)
    {
        var separator = dataUrl.IndexOf(',');
        if (separator < 0) throw new InvalidDataException("Invalid QR image.");
        var bytes = Convert.FromBase64String(dataUrl[(separator + 1)..]);
        using var memory = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = memory;
        image.EndInit();
        image.Freeze();
        return image;
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
        if (_viewModel is not null)
        {
            _viewModel.Messages.CollectionChanged -= OnMessagesChanged;
            foreach (var message in _viewModel.Messages) message.PropertyChanged -= OnMessageChanged;
        }
        _remoteMessages.Writer.TryComplete();
        CancelCurrentOperation();
        _lifetimeCancellation.Cancel();
        _timer.Stop();
        _socket?.Dispose();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

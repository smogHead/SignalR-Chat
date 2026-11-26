using System.Net.Http.Json;
using Chat.Shared;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Chat.Client.Services;

/// <summary>
/// Owns the single SignalR connection for the app. Raises events on a background
/// thread; view models marshal to the UI thread.
/// </summary>
public sealed class ChatService(ILogger<ChatService> log) : IAsyncDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HashSet<string> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private HubConnection? _connection;
    private Uri? _baseUri;
    private string? _token;
    private DateTimeOffset _tokenExpires;

    public string? UserName { get; private set; }
    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    public event Action<ChatMessage>? MessageReceived;
    public event Action<PresenceEvent>? PresenceChanged;
    public event Action<string, string>? UserTyping; // room, user
    public event Action<HubConnectionState, string?>? StateChanged; // state, detail

    /// <summary>Sensible default server address for the current platform.</summary>
    public static string DefaultServerUrl =>
        DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual
            ? "http://10.0.2.2:8080" // Android emulator -> host machine
            : "http://localhost:8080"; // iOS simulator, Mac, Windows

    public async Task ConnectAsync(string serverUrl, string userName, CancellationToken ct = default)
    {
        await DisconnectAsync();

        _baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
        UserName = userName;
        await RefreshTokenAsync(ct);

        _connection = new HubConnectionBuilder()
                      .WithUrl(new Uri(_baseUri, ChatRoutes.Hub.TrimStart('/')), o =>
                      {
                          // WebSockets-only + skip negotiation => no sticky sessions needed behind a load balancer.
                          o.Transports = HttpTransportType.WebSockets;
                          o.SkipNegotiation = true;
                          o.AccessTokenProvider = GetTokenAsync;
                      })
                      .WithAutomaticReconnect(new ForeverRetryPolicy())
                      .WithKeepAliveInterval(TimeSpan.FromSeconds(15))
                      .WithServerTimeout(TimeSpan.FromSeconds(30))
                      .Build();

        _connection.On<ChatMessage>(nameof(IChatClient.ReceiveMessage), m => MessageReceived?.Invoke(m));
        _connection.On<PresenceEvent>(nameof(IChatClient.UserJoined), e => PresenceChanged?.Invoke(e));
        _connection.On<PresenceEvent>(nameof(IChatClient.UserLeft), e => PresenceChanged?.Invoke(e));
        _connection.On<string, string>(nameof(IChatClient.UserTyping), (r, u) => UserTyping?.Invoke(r, u));

        _connection.Reconnecting += ex =>
        {
            StateChanged?.Invoke(HubConnectionState.Reconnecting, ex?.Message);
            return Task.CompletedTask;
        };
        _connection.Reconnected += async _ =>
        {
            // Group membership lives on the old connection id; re-join after reconnect.
            foreach (var room in _rooms.ToArray())
            {
                try
                {
                    await _connection.InvokeAsync<IReadOnlyList<ChatMessage>>(HubMethods.JoinRoom, room);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Re-join of {Room} failed", room);
                }
            }

            StateChanged?.Invoke(HubConnectionState.Connected, "Reconnected");
        };
        _connection.Closed += ex =>
        {
            StateChanged?.Invoke(HubConnectionState.Disconnected, ex?.Message);
            return Task.CompletedTask;
        };

        await _connection.StartAsync(ct);
        StateChanged?.Invoke(HubConnectionState.Connected, null);
    }

    public async Task<IReadOnlyList<ChatMessage>> JoinRoomAsync(string room)
    {
        var history = await Hub.InvokeAsync<IReadOnlyList<ChatMessage>>(HubMethods.JoinRoom, room);
        _rooms.Add(room);
        return history;
    }

    public async Task LeaveRoomAsync(string room)
    {
        _rooms.Remove(room);
        if (State == HubConnectionState.Connected)
            await Hub.InvokeAsync(HubMethods.LeaveRoom, room);
    }

    public Task<ChatMessage> SendAsync(string room, string text) =>
        Hub.InvokeAsync<ChatMessage>(HubMethods.SendMessage, room, text);

    public Task NotifyTypingAsync(string room) =>
        State == HubConnectionState.Connected ? Hub.SendAsync(HubMethods.Typing, room) : Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetOnlineUsersAsync(string room) =>
        Hub.InvokeAsync<IReadOnlyList<string>>(HubMethods.GetOnlineUsers, room);

    public async Task DisconnectAsync()
    {
        _rooms.Clear();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _http.Dispose();
    }

    private HubConnection Hub => _connection ?? throw new InvalidOperationException("Not connected.");

    private async Task<string?> GetTokenAsync()
    {
        if (_token is null || DateTimeOffset.UtcNow > _tokenExpires.AddMinutes(-5))
            await RefreshTokenAsync(CancellationToken.None);
        return _token;
    }

    private async Task RefreshTokenAsync(CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(
                                                   new Uri(_baseUri!, ChatRoutes.Token.TrimStart('/')),
                                                   new TokenRequest(UserName!), ct);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                    ?? throw new InvalidOperationException("Empty token response.");
        _token = token.AccessToken;
        _tokenExpires = token.ExpiresAt;
    }

    /// <summary>0s, 2s, 5s, 10s, then every 30s — never gives up (mobile networks come and go).</summary>
    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
            [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

        public TimeSpan? NextRetryDelay(RetryContext ctx) =>
            ctx.PreviousRetryCount < Delays.Length ? Delays[ctx.PreviousRetryCount] : TimeSpan.FromSeconds(30);
    }
}
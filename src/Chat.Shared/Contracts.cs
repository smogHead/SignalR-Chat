namespace Chat.Shared;

/// <summary>A chat message as sent over the wire and stored in history.</summary>
public sealed record ChatMessage(
    string Id,
    string Room,
    string User,
    string Text,
    DateTimeOffset SentAt);

/// <summary>Sent to a room when someone joins or leaves.</summary>
public sealed record PresenceEvent(string Room, string User, bool Joined, DateTimeOffset At);

/// <summary>Request body for POST /api/auth/token.</summary>
public sealed record TokenRequest(string UserName);

/// <summary>Response body for POST /api/auth/token.</summary>
public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Methods the server calls on clients. Used as the strongly-typed hub client
/// interface on the server, and as the list of handler names on the client.
/// </summary>
public interface IChatClient
{
    Task ReceiveMessage(ChatMessage message);
    Task UserJoined(PresenceEvent evt);
    Task UserLeft(PresenceEvent evt);
    Task UserTyping(string room, string user);
}

/// <summary>Hub method names clients invoke on the server.</summary>
public static class HubMethods
{
    public const string JoinRoom = nameof(JoinRoom);
    public const string LeaveRoom = nameof(LeaveRoom);
    public const string SendMessage = nameof(SendMessage);
    public const string Typing = nameof(Typing);
    public const string GetHistory = nameof(GetHistory);
    public const string GetOnlineUsers = nameof(GetOnlineUsers);
}

public static class ChatRoutes
{
    public const string Hub = "/hubs/chat";
    public const string Token = "/api/auth/token";
    public const string Health = "/health";
}

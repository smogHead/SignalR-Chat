using System.Text.RegularExpressions;
using Chat.Server.Services;
using Chat.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Server.Hubs;

[Authorize]
public sealed partial class ChatHub(
    IMessageStore store,
    IPresenceTracker presence,
    ILogger<ChatHub> log) : Hub<IChatClient>
{
    private const int MaxMessageLength = 4000;

    private string UserName => Context.User?.Identity?.Name
        ?? throw new HubException("Unauthenticated");

    public override async Task OnConnectedAsync()
    {
        log.LogInformation("{User} connected ({ConnectionId})", UserName, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Leave every room this connection was in.
        foreach (var room in await presence.RoomsForConnectionAsync(Context.ConnectionId))
        {
            var userGone = await presence.LeaveAsync(room, UserName, Context.ConnectionId);
            if (userGone)
                await Clients.Group(room).UserLeft(new PresenceEvent(room, UserName, false, DateTimeOffset.UtcNow));
        }
        log.LogInformation("{User} disconnected ({ConnectionId})", UserName, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<IReadOnlyList<ChatMessage>> JoinRoom(string room)
    {
        room = NormaliseRoom(room);
        await Groups.AddToGroupAsync(Context.ConnectionId, room);

        // Only announce the user once, even if they're connected from several devices.
        var firstConnection = await presence.JoinAsync(room, UserName, Context.ConnectionId);
        if (firstConnection)
            await Clients.OthersInGroup(room).UserJoined(new PresenceEvent(room, UserName, true, DateTimeOffset.UtcNow));

        return await store.GetRecentAsync(room, 50);
    }

    public async Task LeaveRoom(string room)
    {
        room = NormaliseRoom(room);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, room);
        if (await presence.LeaveAsync(room, UserName, Context.ConnectionId))
            await Clients.Group(room).UserLeft(new PresenceEvent(room, UserName, false, DateTimeOffset.UtcNow));
    }

    public async Task<ChatMessage> SendMessage(string room, string text)
    {
        room = NormaliseRoom(room);
        text = (text ?? "").Trim();
        if (text.Length == 0) throw new HubException("Message is empty.");
        if (text.Length > MaxMessageLength) throw new HubException($"Message exceeds {MaxMessageLength} characters.");
        if (!await presence.IsInRoomAsync(room, Context.ConnectionId))
            throw new HubException("Join the room before sending.");

        var msg = new ChatMessage(Guid.NewGuid().ToString("N"), room, UserName, text, DateTimeOffset.UtcNow);
        await store.AddAsync(msg);
        await Clients.Group(room).ReceiveMessage(msg);
        return msg;
    }

    public Task Typing(string room) =>
        Clients.OthersInGroup(NormaliseRoom(room)).UserTyping(room, UserName);

    public Task<IReadOnlyList<ChatMessage>> GetHistory(string room, int count) =>
        store.GetRecentAsync(NormaliseRoom(room), Math.Clamp(count, 1, 200));

    public Task<IReadOnlyList<string>> GetOnlineUsers(string room) =>
        presence.UsersInRoomAsync(NormaliseRoom(room));

    private static string NormaliseRoom(string room)
    {
        room = (room ?? "").Trim().ToLowerInvariant();
        if (!RoomName().IsMatch(room))
            throw new HubException("Room names are 1-32 chars: letters, digits, '-' or '_'.");
        return room;
    }

    [GeneratedRegex("^[a-z0-9_-]{1,32}$")]
    private static partial Regex RoomName();
}

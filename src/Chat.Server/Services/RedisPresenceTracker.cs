using StackExchange.Redis;

namespace Chat.Server.Services;

public interface IPresenceTracker
{
    /// <returns>true if this is the user's first connection in the room.</returns>
    Task<bool> JoinAsync(string room, string user, string connectionId);
    /// <returns>true if the user has no connections left in the room.</returns>
    Task<bool> LeaveAsync(string room, string user, string connectionId);
    Task<bool> IsInRoomAsync(string room, string connectionId);
    Task<IReadOnlyList<string>> RoomsForConnectionAsync(string connectionId);
    Task<IReadOnlyList<string>> UsersInRoomAsync(string room);
}

/// <summary>
/// Presence stored in Redis so it's consistent across every server replica.
///   chat:room:{room}:users            HASH  user -> connection count
///   chat:room:{room}:conns            SET   connection ids
///   chat:conn:{connectionId}:rooms    SET   rooms
/// </summary>
public sealed class RedisPresenceTracker(IConnectionMultiplexer redis) : IPresenceTracker
{
    private IDatabase Db => redis.GetDatabase();
    private static RedisKey Users(string room) => $"chat:room:{room}:users";
    private static RedisKey Conns(string room) => $"chat:room:{room}:conns";
    private static RedisKey ConnRooms(string id) => $"chat:conn:{id}:rooms";

    public async Task<bool> JoinAsync(string room, string user, string connectionId)
    {
        if (!await Db.SetAddAsync(Conns(room), connectionId))
            return false; // already joined from this connection
        await Db.SetAddAsync(ConnRooms(connectionId), room);
        await Db.KeyExpireAsync(ConnRooms(connectionId), TimeSpan.FromDays(1));
        var count = await Db.HashIncrementAsync(Users(room), user);
        return count == 1;
    }

    public async Task<bool> LeaveAsync(string room, string user, string connectionId)
    {
        if (!await Db.SetRemoveAsync(Conns(room), connectionId))
            return false;
        await Db.SetRemoveAsync(ConnRooms(connectionId), room);
        var count = await Db.HashDecrementAsync(Users(room), user);
        if (count <= 0)
        {
            await Db.HashDeleteAsync(Users(room), user);
            return true;
        }
        return false;
    }

    public Task<bool> IsInRoomAsync(string room, string connectionId) =>
        Db.SetContainsAsync(Conns(room), connectionId);

    public async Task<IReadOnlyList<string>> RoomsForConnectionAsync(string connectionId) =>
        (await Db.SetMembersAsync(ConnRooms(connectionId))).Select(v => v.ToString()).ToList();

    public async Task<IReadOnlyList<string>> UsersInRoomAsync(string room) =>
        (await Db.HashKeysAsync(Users(room))).Select(v => v.ToString()).Order().ToList();
}

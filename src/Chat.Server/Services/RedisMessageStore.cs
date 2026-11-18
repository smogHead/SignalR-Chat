using System.Text.Json;
using Chat.Shared;
using StackExchange.Redis;

namespace Chat.Server.Services;

public interface IMessageStore
{
    Task AddAsync(ChatMessage message);
    Task<IReadOnlyList<ChatMessage>> GetRecentAsync(string room, int count);
}

/// <summary>
/// Keeps the most recent N messages per room in a capped Redis list.
/// Swap for a database-backed store if you need full history/search.
/// </summary>
public sealed class RedisMessageStore(IConnectionMultiplexer redis, IConfiguration config) : IMessageStore
{
    private readonly int _maxPerRoom = config.GetValue("Chat:HistoryPerRoom", 500);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static RedisKey Key(string room) => $"chat:history:{room}";

    public async Task AddAsync(ChatMessage message)
    {
        var db = redis.GetDatabase();
        var key = Key(message.Room);
        var tx = db.CreateTransaction();
        _ = tx.ListRightPushAsync(key, JsonSerializer.Serialize(message, Json));
        _ = tx.ListTrimAsync(key, -_maxPerRoom, -1);
        await tx.ExecuteAsync();
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentAsync(string room, int count)
    {
        var values = await redis.GetDatabase().ListRangeAsync(Key(room), -count, -1);
        return values
            .Select(v => JsonSerializer.Deserialize<ChatMessage>(v.ToString(), Json))
            .OfType<ChatMessage>()
            .ToList();
    }
}

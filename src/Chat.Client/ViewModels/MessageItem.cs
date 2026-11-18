using Chat.Shared;

namespace Chat.Client.ViewModels;

/// <summary>Display wrapper around a <see cref="ChatMessage"/> or a system notice.</summary>
public sealed class MessageItem
{
    public required string Id { get; init; }
    public required string User { get; init; }
    public required string Text { get; init; }
    public required DateTimeOffset SentAt { get; init; }
    public bool IsMine { get; init; }
    public bool IsSystem { get; init; }

    public bool IsTheirs => !IsMine && !IsSystem;
    public bool IsChat => !IsSystem;
    public string Time => SentAt.ToLocalTime().ToString("HH:mm");
    public LayoutOptions Alignment => IsSystem ? LayoutOptions.Center : IsMine ? LayoutOptions.End : LayoutOptions.Start;

    public static MessageItem From(ChatMessage m, string? me) => new()
    {
        Id = m.Id,
        User = m.User,
        Text = m.Text,
        SentAt = m.SentAt,
        IsMine = string.Equals(m.User, me, StringComparison.OrdinalIgnoreCase),
    };

    public static MessageItem Notice(string text) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        User = "",
        Text = text,
        SentAt = DateTimeOffset.Now,
        IsSystem = true,
    };
}

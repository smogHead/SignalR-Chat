using System.Collections.ObjectModel;
using Chat.Client.Services;
using Chat.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.Client.ViewModels;

public partial class ChatViewModel : ObservableObject, IQueryAttributable
{
    private readonly ChatService _chat;
    private readonly HashSet<string> _seen = [];
    private readonly Dictionary<string, DateTime> _typing = new(StringComparer.OrdinalIgnoreCase);
    private IDispatcherTimer? _typingTimer;
    private DateTime _lastTypingSent = DateTime.MinValue;

    public ChatViewModel(ChatService chat) => _chat = chat;

    public ObservableCollection<MessageItem> Messages { get; } = [];
    public ObservableCollection<string> OnlineUsers { get; } = [];

    [ObservableProperty] private string _room = "general";
    [ObservableProperty] private string _draft = "";
    [ObservableProperty] private string _status = "Connected";
    [ObservableProperty] private bool _isConnected = true;
    [ObservableProperty] private string? _typingText;

    public string Title => $"#{Room}";

    /// <summary>Raised when a new message is appended so the view can scroll.</summary>
    public event Action<MessageItem>? MessageAdded;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("room", out var r) && r is string room)
        {
            Room = room;
            OnPropertyChanged(nameof(Title));
        }
    }

    public async Task StartAsync()
    {
        _chat.MessageReceived += OnMessage;
        _chat.PresenceChanged += OnPresence;
        _chat.UserTyping += OnTyping;
        _chat.StateChanged += OnState;

        _typingTimer = Application.Current!.Dispatcher.CreateTimer();
        _typingTimer.Interval = TimeSpan.FromSeconds(1);
        _typingTimer.Tick += (_, _) => RefreshTypingText();
        _typingTimer.Start();

        try
        {
            var history = await _chat.JoinRoomAsync(Room);
            foreach (var m in history) Add(MessageItem.From(m, _chat.UserName));
            Add(MessageItem.Notice($"You joined #{Room}"));
            await RefreshUsersAsync();
        }
        catch (Exception ex)
        {
            Add(MessageItem.Notice($"Failed to join: {ex.Message}"));
        }
    }

    public async Task StopAsync()
    {
        _chat.MessageReceived -= OnMessage;
        _chat.PresenceChanged -= OnPresence;
        _chat.UserTyping -= OnTyping;
        _chat.StateChanged -= OnState;
        _typingTimer?.Stop();

        try { await _chat.LeaveRoomAsync(Room); } catch { /* best effort */ }
        await _chat.DisconnectAsync();
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = Draft?.Trim();
        if (string.IsNullOrEmpty(text) || !IsConnected) return;
        Draft = "";
        try
        {
            // The server echoes the message to the whole group (including us),
            // so we add it from the returned value and de-dupe by id.
            var sent = await _chat.SendAsync(Room, text);
            Add(MessageItem.From(sent, _chat.UserName));
        }
        catch (Exception ex)
        {
            Draft = text;
            Add(MessageItem.Notice($"Not sent: {ex.Message}"));
        }
    }

    partial void OnDraftChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (DateTime.UtcNow - _lastTypingSent < TimeSpan.FromSeconds(2)) return;
        _lastTypingSent = DateTime.UtcNow;
        _ = _chat.NotifyTypingAsync(Room);
    }

    // ---------- Hub events (background thread) ----------

    private void OnMessage(ChatMessage m)
    {
        if (!string.Equals(m.Room, Room, StringComparison.OrdinalIgnoreCase)) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _typing.Remove(m.User);
            RefreshTypingText();
            Add(MessageItem.From(m, _chat.UserName));
        });
    }

    private void OnPresence(PresenceEvent e)
    {
        if (!string.Equals(e.Room, Room, StringComparison.OrdinalIgnoreCase)) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Add(MessageItem.Notice(e.Joined ? $"{e.User} joined" : $"{e.User} left"));
            if (e.Joined && !OnlineUsers.Contains(e.User)) OnlineUsers.Add(e.User);
            if (!e.Joined) OnlineUsers.Remove(e.User);
        });
    }

    private void OnTyping(string room, string user)
    {
        if (!string.Equals(room, Room, StringComparison.OrdinalIgnoreCase)) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _typing[user] = DateTime.UtcNow;
            RefreshTypingText();
        });
    }

    private void OnState(HubConnectionState state, string? detail)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            IsConnected = state == HubConnectionState.Connected;
            Status = state switch
            {
                HubConnectionState.Connected => "Connected",
                HubConnectionState.Reconnecting => "Reconnecting…",
                HubConnectionState.Connecting => "Connecting…",
                _ => "Disconnected",
            };
            if (state == HubConnectionState.Connected && detail == "Reconnected")
                await RefreshUsersAsync();
        });
    }

    // ---------- Helpers ----------

    private void Add(MessageItem item)
    {
        if (!_seen.Add(item.Id)) return;
        Messages.Add(item);
        MessageAdded?.Invoke(item);
    }

    private async Task RefreshUsersAsync()
    {
        try
        {
            var users = await _chat.GetOnlineUsersAsync(Room);
            OnlineUsers.Clear();
            foreach (var u in users) OnlineUsers.Add(u);
        }
        catch { /* non-critical */ }
    }

    private void RefreshTypingText()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-3);
        foreach (var stale in _typing.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
            _typing.Remove(stale);

        TypingText = _typing.Count switch
        {
            0 => null,
            1 => $"{_typing.Keys.First()} is typing…",
            _ => $"{string.Join(", ", _typing.Keys)} are typing…",
        };
    }
}

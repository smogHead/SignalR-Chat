// Minimal terminal client for smoke-testing the server.
//   dotnet run --project src/Chat.Cli -- alice general http://localhost:8080

using System.Net.Http.Json;
using Chat.Shared;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

var user = args.ElementAtOrDefault(0) ?? $"cli-{Random.Shared.Next(1000)}";
var room = args.ElementAtOrDefault(1) ?? "general";
var server = (args.ElementAtOrDefault(2) ?? "http://localhost:8080").TrimEnd('/');

using var http = new HttpClient();
var tokenResp = await http.PostAsJsonAsync($"{server}{ChatRoutes.Token}", new TokenRequest(user));
tokenResp.EnsureSuccessStatusCode();
var token = (await tokenResp.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;

await using var conn = new HubConnectionBuilder()
                       .WithUrl($"{server}{ChatRoutes.Hub}", o =>
                       {
                           o.Transports = HttpTransportType.WebSockets;
                           o.SkipNegotiation = true;
                           o.AccessTokenProvider = () => Task.FromResult<string?>(token);
                       })
                       .WithAutomaticReconnect()
                       .Build();

conn.On<ChatMessage>(nameof(IChatClient.ReceiveMessage), m =>
{
    if (m.User != user) Console.WriteLine($"[{m.SentAt.ToLocalTime():HH:mm}] {m.User}: {m.Text}");
});
conn.On<PresenceEvent>(nameof(IChatClient.UserJoined), e => Console.WriteLine($"* {e.User} joined"));
conn.On<PresenceEvent>(nameof(IChatClient.UserLeft), e => Console.WriteLine($"* {e.User} left"));
conn.Reconnected += async _ => await conn.InvokeAsync<IReadOnlyList<ChatMessage>>(HubMethods.JoinRoom, room);

await conn.StartAsync();
var history = await conn.InvokeAsync<IReadOnlyList<ChatMessage>>(HubMethods.JoinRoom, room);
foreach (var m in history) Console.WriteLine($"[{m.SentAt.ToLocalTime():HH:mm}] {m.User}: {m.Text}");
Console.WriteLine($"-- {user} in #{room}. Type a message, or /quit --");

while (Console.ReadLine() is { } line && line != "/quit")
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    try
    {
        await conn.InvokeAsync<ChatMessage>(HubMethods.SendMessage, room, line);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"! {ex.Message}");
    }
}
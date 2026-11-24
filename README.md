# SignalR Chat — containerised server + .NET MAUI clients

Real-time chat with rooms, history, presence and typing indicators.

- **Server**: ASP.NET Core 11 + SignalR, runs in Docker, scales horizontally via a Redis backplane behind nginx.
- **Clients**: one .NET MAUI 11 app for **iOS, Android, macOS (Mac Catalyst) and Windows**.
- **CLI**: a small console client for testing the server without a device.

```
 ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐
 │   iOS    │ │ Android  │ │  macOS   │ │ Windows  │   ← Chat.Client (MAUI)
 └────┬─────┘ └────┬─────┘ └────┬─────┘ └────┬─────┘
      └────────────┴─ WebSocket ┴────────────┘
                         │  (JWT in access_token)
                  ┌──────▼──────┐
                  │    nginx    │  :8080, round-robin
                  └──────┬──────┘
          ┌──────────────┼──────────────┐
   ┌──────▼─────┐ ┌──────▼─────┐ ┌──────▼─────┐
   │chat-server │ │chat-server │ │chat-server │   ← Chat.Server (N replicas)
   └──────┬─────┘ └──────┬─────┘ └──────┬─────┘
          └──────────────┼──────────────┘
                  ┌──────▼──────┐
                  │    Redis    │  backplane + history + presence
                  └─────────────┘
```

## Layout

```
SignalRChat.slnx
docker-compose.yml          redis + chat-server + nginx
deploy/nginx.conf           WebSocket-aware reverse proxy
src/
  Chat.Shared/              DTOs, hub method names, IChatClient contract (shared by server & clients)
  Chat.Server/              ASP.NET Core SignalR hub, JWT auth, Redis stores, Dockerfile
  Chat.Client/              .NET MAUI app (Android / iOS / MacCatalyst / Windows)
  Chat.Cli/                 Console test client
```

## Prerequisites

- Docker (Desktop or Engine with Compose v2)
- .NET 11 SDK with the MAUI workload: `dotnet workload install maui`
- iOS / Mac Catalyst builds need a Mac with Xcode; Windows builds need Windows. Android builds anywhere with the Android SDK.
- The project picks targets per OS automatically: **Windows** → Android + Windows, **macOS** → Android + iOS + Mac Catalyst.
  To build iOS from Windows via a paired Mac (Visual Studio "Pair to Mac"), set `IncludeAppleTargets=true` (environment
  variable, `Directory.Build.props`, or `-p:` on the command line). Mac Catalyst can only be built on a Mac.

## 1. Run the server

```bash
cp .env.example .env          # then edit JWT_SIGNING_KEY (32+ chars)
docker compose up --build
curl http://localhost:8080/health      # -> Healthy
```

Scale out to prove the backplane works:

```bash
docker compose up --build --scale chat-server=3
```

Messages sent to any replica reach users connected to every other replica.

**Run without Docker** (for debugging): start Redis (`docker run -p 6379:6379 redis:7-alpine`), then
`dotnet run --project src/Chat.Server --urls http://localhost:8080` — Development settings supply a dev signing key.

## 2. Smoke-test with the CLI

In two terminals:

```bash
dotnet run --project src/Chat.Cli -- alice general
dotnet run --project src/Chat.Cli -- bob   general
```

## 3. Run the MAUI clients

```bash
cd src/Chat.Client
dotnet build -t:Run -f net11.0-android                 # Android emulator/device
dotnet build -t:Run -f net11.0-ios                     # iOS simulator (on a Mac)
dotnet build -t:Run -f net11.0-maccatalyst             # macOS app (on a Mac)
dotnet build -t:Run -f net11.0-windows10.0.19041.0     # Windows (on Windows)
dotnet build -t:Run -f net11.0-maccatalyst -p:RuntimeIdentifier=maccatalyst-x64
dotnet build -t:Run -f net11.0-maccatalyst -r maccatalyst-x64
```

Or open `SignalRChat.slnx` in Visual Studio / VS Code (C# Dev Kit + .NET MAUI extension) / Rider and pick the target.

### Which server URL to enter

| Where the app runs          | Server URL                    |
|-----------------------------|-------------------------------|
| Windows / macOS (same box)  | `http://localhost:8080`       |
| iOS simulator               | `http://localhost:8080`       |
| Android emulator            | `http://10.0.2.2:8080` (pre-filled) |
| Physical phone              | `http://<your-PC-LAN-IP>:8080` |

For a physical Android device on plain HTTP, add your LAN IP to
`Platforms/Android/Resources/xml/network_security_config.xml`. iOS/macOS allow local-network HTTP via
`NSAllowsLocalNetworking`. **In production use HTTPS/WSS and remove these exceptions.**

## Hub API

Clients → server (`/hubs/chat`, requires JWT):

| Method | Returns | Notes |
|---|---|---|
| `JoinRoom(room)` | last 50 `ChatMessage` | Adds to SignalR group; announces presence once per user |
| `LeaveRoom(room)` | — | |
| `SendMessage(room, text)` | `ChatMessage` | Max 4000 chars; must have joined |
| `Typing(room)` | — | Broadcast to others in room |
| `GetHistory(room, count)` | `ChatMessage[]` | 1–200 |
| `GetOnlineUsers(room)` | `string[]` | Across all replicas |

Server → clients (`IChatClient`): `ReceiveMessage`, `UserJoined`, `UserLeft`, `UserTyping`.

Room names: 1–32 chars, `a-z 0-9 - _` (normalised to lower case).

## Design notes

- **No sticky sessions.** Clients use WebSockets with `SkipNegotiation = true`, so the whole session is one
  long-lived socket and nginx can round-robin freely. If you need SSE/long-polling fallbacks (e.g. restrictive
  proxies), remove `SkipNegotiation` in the clients and enable sticky sessions (`ip_hash` or a cookie) in nginx.
- **Auth.** `POST /api/auth/token {"userName":"alice"}` issues a JWT for *any* name — it's a dev stand-in.
  Replace it with your identity provider (Entra ID, Auth0, Keycloak…) and keep the `OnMessageReceived`
  hook, which reads `access_token` from the query string for WebSocket requests.
- **Multi-device.** Presence is counted per connection, so a user on phone + laptop is announced once and
  only "leaves" when the last device disconnects.
- **Reconnects.** The MAUI client retries forever (0s, 2s, 5s, 10s, then every 30s), refreshes the token if
  it's close to expiry, and re-joins its rooms after reconnecting (group membership is per connection id).
- **History.** A capped Redis list per room (`Chat:HistoryPerRoom`, default 500). Swap `IMessageStore` for
  an EF Core/Postgres implementation if you need permanent history or search.

## Production checklist

- [ ] Terminate TLS (nginx, a cloud load balancer or ingress) and use `https://` URLs in clients
- [ ] Real identity provider instead of the dev token endpoint
- [ ] Managed Redis (Azure Cache, ElastiCache…) with auth/TLS: `Redis__ConnectionString=host:6380,password=…,ssl=true`
- [ ] Remove the cleartext / ATS exceptions from the client platform folders
- [ ] Push notifications (APNs / FCM / WNS) for users who aren't connected — SignalR only reaches live connections
- [ ] Rate-limit `SendMessage` / `Typing` per user
- [ ] Kubernetes: the same image works; run N replicas behind an ingress with WebSocket support

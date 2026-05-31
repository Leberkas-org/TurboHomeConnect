# TurboHomeConnect

Channel-based streaming client for the [Home Connect API](https://developer.home-connect.com/), built on Akka.Streams and [TurboHTTP](https://github.com/Leberkas-org/TurboHTTP).

Follows the [Turbo Client Pattern](./turbo-client-pattern.md): commands flow into a channel, responses and events flow back out through another channel, and the materialized Akka.Streams flow is owned by an actor for clean lifecycle management.

## Requirements

- **.NET 10** (TurboHTTP requires it)
- A Home Connect developer account — register at https://developer.home-connect.com/

## Install

```sh
dotnet add package TurboHomeConnect
```

## Quick start with Docker

The sample app ships with a Dockerfile + `docker-compose.yml` + `.env`. Three steps:

1. Register an app at https://developer.home-connect.com (one for the simulator is enough to start). Pick `http://localhost:7878/oauth/callback` as a redirect URI.
2. Copy your client id/secret into `.env`:
   ```sh
   cp .env.example .env
   $EDITOR .env   # fill HOMECONNECT_CLIENT_ID and HOMECONNECT_CLIENT_SECRET
   ```
3. Run it:
   ```sh
   docker compose up --build
   ```

The sample prints the authorize URL on first run — click it, authorize in your browser, the redirect lands back on the container's `/oauth/callback`, and the refresh token is persisted to `./.tokens/token.json` on the host. Subsequent runs skip the interactive step.

`.env` knobs:

| Variable                              | Purpose                                                              |
|---------------------------------------|----------------------------------------------------------------------|
| `HOMECONNECT_CLIENT_ID` / `_SECRET`   | From the developer portal.                                           |
| `HOMECONNECT_USE_SIMULATOR`           | `1` for simulator, `0` for production.                               |
| `HOMECONNECT_BIND_ALL_INTERFACES`     | `1` inside Docker so the listener accepts the port-forwarded traffic. |
| `HOMECONNECT_OPEN_BROWSER`            | `0` for headless — URL is printed instead.                           |
| `HOMECONNECT_TOKEN_FILE`              | Where to persist tokens (inside the container, mounted as a volume). |
| `HOMECONNECT_STREAM_SECONDS`          | How long to keep the SSE subscription open.                          |

## Quick start — full surface in five lines

```csharp
using Akka.Actor;
using TurboHomeConnect;
using TurboHomeConnect.Commands;

using var system = ActorSystem.Create("home-connect");
using var client = HomeConnectBuilder.Create()
    .UseProduction()                              // or .UseSimulator()
    .StaticAccessToken(myAccessToken)             // or .TokenProvider(...)
    .Build(system);

var appliances = (AppliancesResponse)await client.RequestAsync(new GetAppliancesCommand());
foreach (var a in appliances.Appliances) Console.WriteLine($"{a.HaId} — {a.Type}");
```

## Three usage patterns

### 1. RequestAsync — REST-style request/response

```csharp
var status = (StatusResponse)await client.RequestAsync(new GetStatusCommand(haId));
foreach (var s in status.Status) Console.WriteLine($"{s.Key} = {s.Value}");
```

### 2. SendAsync — fire-and-forget, response arrives on the channel

```csharp
await client.SendAsync(new SetSettingCommand(haId, "BSH.Common.Setting.PowerState",
    JsonSerializer.SerializeToElement("BSH.Common.EnumType.PowerState.On")));

await foreach (var msg in client.Responses.ReadAllAsync())
{
    if (msg is SettingUpdatedResponse u) Console.WriteLine($"set {u.SettingKey}");
}
```

### 3. Subscribe — live SSE event stream

```csharp
await client.SendAsync(new SubscribeEventsCommand());   // or scope to one haId

await foreach (var msg in client.Responses.ReadAllAsync())
{
    switch (msg)
    {
        case HomeConnectEventMessage e:
            foreach (var item in e.Items) Console.WriteLine($"{e.HaId}: {item.Key} = {item.Value}");
            break;
        case SubscriptionDisconnectedMessage drop:
            Console.WriteLine($"reconnecting after: {drop.Reason?.Message}");
            break;
    }
}
```

The SSE stream is wrapped in `RestartSource.WithBackoff`, so transient disconnects reconnect automatically with backoff. Tune via `.SseRestartSettings(...)` on the builder.

## OAuth — two ways

### A. Bring your own token (simplest)

Use any OAuth library you like (e.g. `Duende.IdentityModel`) and hand the builder a function that returns a current access token. The function is called for every REST request and every (re)subscribe, so it's the right place to refresh on expiry.

```csharp
.TokenProvider(ct => myAuthClient.GetAccessTokenAsync(ct))
```

### B. Built-in Authorization Code helper

```csharp
using var oauth = new HomeConnectAuthorizationCodeFlow(new HomeConnectOAuthOptions
{
    ClientId      = "...",
    ClientSecret  = "...",
    RedirectUri   = new Uri("http://localhost:7878/oauth/callback"),
    TokenStore    = new InMemoryTokenStore(),     // implement IPersistedTokenStore for disk-backed
});

await oauth.AuthorizeInteractiveAsync();          // launches browser once
// then plug in:
.TokenProvider(oauth.GetAccessTokenAsync)
```

The helper hosts a local `HttpListener` for the OAuth callback and refreshes tokens automatically.

## Configuration knobs

| Builder method                    | What it does                                                  | Default  |
|-----------------------------------|---------------------------------------------------------------|----------|
| `UseProduction()` / `UseSimulator()` | Sets base address.                                         | (required) |
| `BaseAddress(uri)`                | Custom base address (e.g. test proxy).                        | —        |
| `TokenProvider(func)`             | Bearer token source. Called per request and per (re)connect.  | (required) |
| `StaticAccessToken(s)`            | Convenience for short-lived demos.                            | —        |
| `UseHttpClient(client)`           | Bring your own `ITurboHttpClient` (DI-friendly).              | internal |
| `ConfigureHttpClient(action)`     | Tweak the internal `TurboClientOptions`.                      | defaults |
| `LoggerFactory(factory)`          | Bridges Akka logs to MEL.                                     | —        |
| `CommandCapacity(n)`              | Bounded input channel capacity.                               | 64       |
| `RestParallelism(n)`              | In-flight REST requests.                                      | 4        |
| `MaxConcurrentSubscriptions(n)`   | Parallel SSE streams.                                         | 4        |
| `SseRestartSettings(s)`           | Backoff + max-restart policy for SSE.                         | 1s/60s/0.2 |

## Architecture

```
 user code                channels                StreamOwnerActor
 ─────────                ────────                ─────────────────
 SendAsync(cmd) ─────► commandChannel ─────►   Partition ┬─► RestFlow ──► HttpClient ─┐
                                                         │                            │
                                                         └─► SseFlow ──► SseSource   ─┤
                                                                                      ▼
 client.Responses  ◄── userChannel ◄── Dispatch ◄── streamOutput ◄── Merge ◄── messages
                          │
                  ┌───────┴───────┐
                  │  correlated?  │
                  └───┬───────┬───┘
                  yes │       │ no
                      ▼       ▼
              pending TCS    user channel
                      │       │
                      ▼       ▼
             RequestAsync()  Responses.ReadAllAsync()
```

See [`turbo-client-pattern.md`](./turbo-client-pattern.md) for the full pattern this library implements.

## Coverage

REST endpoints covered:

- Appliances: list, get
- Status: list, single
- Settings: list, single, set
- Programs: active (get/start/stop), selected (get/select), available (list/get)
- Program options: active/selected (list/get/set)
- Commands: list, execute
- Images: list, fetch bytes

Plus the SSE `/events` stream — both per-appliance and account-wide.

## License

MIT

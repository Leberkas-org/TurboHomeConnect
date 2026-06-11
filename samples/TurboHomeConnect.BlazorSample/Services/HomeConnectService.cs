using System.Collections.Concurrent;
using System.Text.Json;
using Akka.Actor;
using TurboHomeConnect;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Commands;
using TurboHomeConnect.Model;
using TurboHomeConnect.OAuth;

namespace TurboHomeConnect.BlazorSample.Services;

/// <summary>
/// Singleton wrapper around <see cref="IHomeConnectClient"/>. Owns the OAuth flow, the appliance
/// list cache, and a bounded ring of recent SSE events. Blazor components subscribe to
/// <see cref="OnStateChanged"/> and re-render on change.
/// </summary>
public sealed class HomeConnectService : IAsyncDisposable
{
    private const int EventBufferSize = 200;
    private static readonly TimeSpan StatusRequestTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<HomeConnectService> _logger;
    private readonly HomeConnectOAuthOptions _oauthOptions;
    private readonly bool _useSimulator;
    private readonly IPersistedTokenStore _tokenStore;

    private Func<Uri, CancellationToken, Task<AuthorizationCode>>? _callbackReceiver;
    private HomeConnectAuthorizationCodeFlow? _connectedOAuth;
    private ActorSystem? _system;
    private IHomeConnectClient? _client;
    private Task? _eventPump;
    private readonly SemaphoreSlim _startGate = new(1, 1);

    private readonly ConcurrentDictionary<string, IReadOnlyList<StatusValue>> _statusByHaId = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<SettingValue>> _settingsByHaId = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<Program>> _availableProgramsByHaId = new();
    private readonly ConcurrentDictionary<string, Program?> _activeProgramByHaId = new();
    private readonly ConcurrentDictionary<string, Program?> _selectedProgramByHaId = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<HomeConnectCommandDescriptor>> _commandsByHaId = new();
    // Full program specs (including option constraints) — fetched lazily on top of /programs/available
    // which only returns keys. /selected and /active return options without constraints, so we
    // overlay the spec's constraints when rendering editors.
    private readonly ConcurrentDictionary<(string HaId, string ProgramKey), TurboHomeConnect.Model.Program> _programSpecByKey = new();
    private readonly LinkedList<HomeConnectEventMessage> _events = new();
    private readonly object _eventsGate = new();

    public HomeConnectService(ILogger<HomeConnectService> logger, HomeConnectOAuthOptions oauthOptions)
    {
        _logger = logger;
        _oauthOptions = oauthOptions;
        _tokenStore = oauthOptions.TokenStore ?? throw new InvalidOperationException("TokenStore is required.");
        _useSimulator = oauthOptions.AuthorizeEndpoint == HomeConnectEndpoints.SimulatorAuthorizeEndpoint;
    }

    /// <summary>Latest appliance list (empty until <see cref="StartAsync"/> succeeds).</summary>
    public IReadOnlyList<HomeAppliance> Appliances { get; private set; } = [];

    public Uri? PendingAuthorizeUrl { get; private set; }
    public bool IsAuthorized { get; private set; }
    public bool IsConnected => _client is not null;
    public string? LastError { get; private set; }

    public void ClearLastError()
    {
        if (LastError is null) return;
        LastError = null;
        NotifyChange();
    }

    public event Action? OnStateChanged;

    public IReadOnlyList<HomeConnectEventMessage> SnapshotEvents()
    {
        lock (_eventsGate) return _events.ToArray();
    }

    public IReadOnlyList<StatusValue>? GetStatusFor(string haId) =>
        _statusByHaId.TryGetValue(haId, out var status) ? status : null;

    public IReadOnlyList<SettingValue>? GetSettingsFor(string haId) =>
        _settingsByHaId.TryGetValue(haId, out var settings) ? settings : null;

    public IReadOnlyList<Program>? GetAvailableProgramsFor(string haId) =>
        _availableProgramsByHaId.TryGetValue(haId, out var p) ? p : null;

    public Program? GetActiveProgramFor(string haId) =>
        _activeProgramByHaId.TryGetValue(haId, out var p) ? p : null;

    public Program? GetSelectedProgramFor(string haId) =>
        _selectedProgramByHaId.TryGetValue(haId, out var p) ? p : null;

    public IReadOnlyList<HomeConnectCommandDescriptor>? GetCommandsFor(string haId) =>
        _commandsByHaId.TryGetValue(haId, out var c) ? c : null;

    public TurboHomeConnect.Model.Program? GetProgramSpec(string haId, string programKey) =>
        _programSpecByKey.TryGetValue((haId, programKey), out var p) ? p : null;

    /// <summary>Lookup option constraints from the cached program spec.</summary>
    public ValueConstraints? GetOptionConstraints(string haId, string programKey, string optionKey) =>
        GetProgramSpec(haId, programKey)?.Options?.FirstOrDefault(o => o.Key == optionKey)?.Constraints;

    /// <summary>
    /// Idempotent startup. If a cached token exists, connects immediately. Otherwise waits for
    /// <see cref="AuthorizeAsync"/> to be called (the UI shows the authorize banner).
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null) return;

            var cached = await _tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            IsAuthorized = cached?.RefreshToken is not null;
            if (!IsAuthorized)
            {
                NotifyChange();
                return;
            }

            await ConnectInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <summary>Runs the interactive Authorization Code grant, then connects.</summary>
    public async Task AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        using var oauth = CreateOAuthFlow();
        try
        {
            await oauth.AuthorizeInteractiveAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            PendingAuthorizeUrl = null;
        }

        IsAuthorized = true;
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is null)
            {
                await ConnectInternalAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <summary>Manually re-fetch status for one appliance and update the cache.</summary>
    public async Task RefreshStatusAsync(string haId, CancellationToken cancellationToken = default)
    {
        if (_client is null) return;
        var r = await TryRequestAsync($"GetStatus({haId})", new GetStatusCommand(haId), cancellationToken).ConfigureAwait(false);
        if (r is null) return;
        _statusByHaId[haId] = r.Status;
        NotifyChange();
    }

    public async Task RefreshSettingsAsync(string haId, CancellationToken cancellationToken = default)
    {
        if (_client is null) return;
        var r = await TryRequestAsync($"GetSettings({haId})", new GetSettingsCommand(haId), cancellationToken).ConfigureAwait(false);
        if (r is null) return;

        // The list endpoint returns only {key, value}. Render that immediately, then enrich each
        // setting via GET /settings/{key} for type/unit/constraints. Sequential — not parallel —
        // to stay friendly to the Home Connect ~50 req/min rate limit.
        _settingsByHaId[haId] = r.Settings;
        NotifyChange();

        var enriched = r.Settings.ToList();
        for (var i = 0; i < enriched.Count; i++)
        {
            var single = await TryRequestAsync($"GetSingleSetting({haId},{enriched[i].Key})", new GetSingleSettingCommand(haId, enriched[i].Key), cancellationToken).ConfigureAwait(false);
            if (single is not null)
            {
                enriched[i] = single.Setting;
                _settingsByHaId[haId] = enriched.ToList();   // snapshot so subscribers see partial progress
                NotifyChange();
            }
        }
    }

    public async Task RefreshProgramsAsync(string haId, CancellationToken cancellationToken = default)
    {
        if (_client is null) return;
        // Available + selected + active in parallel.
        var availableTask = TryRequestAsync($"GetAvailablePrograms({haId})", new GetAvailableProgramsCommand(haId), cancellationToken);
        var selectedTask  = TryRequestAsync($"GetSelectedProgram({haId})",  new GetSelectedProgramCommand(haId),  cancellationToken);
        var activeTask    = TryRequestAsync($"GetActiveProgram({haId})",    new GetActiveProgramCommand(haId),    cancellationToken);
        await Task.WhenAll(availableTask, selectedTask, activeTask).ConfigureAwait(false);

        // Cache an empty list for appliance types that have no programs at all (Fridges, etc.)
        // so the UI shows "No available programs" instead of "Loading…" forever.
        var availableResult = await availableTask.ConfigureAwait(false);
        _availableProgramsByHaId[haId] = availableResult?.Programs ?? [];

        // Selected/Active can legitimately 404 when nothing is selected/running — treat as null.
        _selectedProgramByHaId[haId] = (await selectedTask.ConfigureAwait(false))?.Program;
        _activeProgramByHaId[haId]   = (await activeTask  .ConfigureAwait(false))?.Program;

        // Kick off spec fetches in the background so option-editor constraints (min/max/step)
        // are available the next time the page renders.
        if (_selectedProgramByHaId[haId]?.Key is string selKey)
            _ = EnsureProgramSpecAsync(haId, selKey, CancellationToken.None);
        if (_activeProgramByHaId[haId]?.Key is string actKey)
            _ = EnsureProgramSpecAsync(haId, actKey, CancellationToken.None);

        NotifyChange();
    }

    public async Task EnsureProgramSpecAsync(string haId, string programKey, CancellationToken cancellationToken = default)
    {
        if (_programSpecByKey.ContainsKey((haId, programKey))) return;
        var r = await TryRequestAsync($"GetAvailableProgram({haId},{programKey})", new GetAvailableProgramCommand(haId, programKey), cancellationToken).ConfigureAwait(false);
        if (r is not null)
        {
            _programSpecByKey[(haId, programKey)] = r.Program;
            NotifyChange();
        }
        // Don't surface errors here — the editor falls back to no constraints if spec isn't loaded.
    }

    public async Task RefreshCommandsAsync(string haId, CancellationToken cancellationToken = default)
    {
        if (_client is null) return;
        var r = await TryRequestAsync($"GetCommands({haId})", new GetCommandsCommand(haId), cancellationToken).ConfigureAwait(false);
        // Cache an empty list on non-success so the UI shows "No commands" instead of "Loading…".
        _commandsByHaId[haId] = r?.Commands ?? [];
        NotifyChange();
    }

    public async Task SetSettingAsync(string haId, string key, JsonElement value, string? unit, CancellationToken cancellationToken = default)
    {
        await TryRequestAsync($"SetSetting({haId},{key})", new SetSettingCommand(haId, key, value, unit), cancellationToken).ConfigureAwait(false);

        // Re-read just this one setting (full record incl. constraints) and patch it into the cache —
        // avoids losing the enriched constraints by re-fetching the bare list.
        var single = await TryRequestAsync($"GetSingleSetting({haId},{key})", new GetSingleSettingCommand(haId, key), cancellationToken).ConfigureAwait(false);
        if (single is not null && _settingsByHaId.TryGetValue(haId, out var current))
        {
            var updated = current.ToList();
            var pos = updated.FindIndex(s => s.Key == key);
            if (pos >= 0) updated[pos] = single.Setting; else updated.Add(single.Setting);
            _settingsByHaId[haId] = updated;
            NotifyChange();
        }
    }

    public async Task SelectProgramAsync(string haId, string programKey, CancellationToken cancellationToken = default)
    {
        await TryRequestAsync($"SelectProgram({haId},{programKey})", new SelectProgramCommand(haId, programKey), cancellationToken).ConfigureAwait(false);
        await RefreshProgramsAsync(haId, cancellationToken).ConfigureAwait(false);
    }

    public async Task StartProgramAsync(string haId, string programKey, CancellationToken cancellationToken = default)
    {
        await TryRequestAsync($"StartProgram({haId},{programKey})", new StartProgramCommand(haId, programKey), cancellationToken).ConfigureAwait(false);
        await RefreshProgramsAsync(haId, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopActiveProgramAsync(string haId, CancellationToken cancellationToken = default)
    {
        await TryRequestAsync($"StopActiveProgram({haId})", new StopActiveProgramCommand(haId), cancellationToken).ConfigureAwait(false);
        await RefreshProgramsAsync(haId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetSelectedProgramOptionAsync(string haId, string key, JsonElement value, string? unit, CancellationToken cancellationToken = default)
    {
        await TryRequestAsync($"SetSelectedProgramOption({haId},{key})", new SetSelectedProgramOptionCommand(haId, key, value, unit), cancellationToken).ConfigureAwait(false);
        await RefreshProgramsAsync(haId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetActiveProgramOptionAsync(string haId, string key, JsonElement value, string? unit, CancellationToken cancellationToken = default)
    {
        await TryRequestAsync($"SetActiveProgramOption({haId},{key})", new SetActiveProgramOptionCommand(haId, key, value, unit), cancellationToken).ConfigureAwait(false);
        await RefreshProgramsAsync(haId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteCommandAsync(string haId, string commandKey, CancellationToken cancellationToken = default)
    {
        await TryRequestAsync($"ExecuteCommand({haId},{commandKey})", new ExecuteCommandCommand(haId, commandKey), cancellationToken).ConfigureAwait(false);
        NotifyChange();
    }

    private void ReportError(string op, HomeConnectApiException ex)
    {
        LastError = $"{op} failed: HTTP {(int?)ex.StatusCode} [{ex.ErrorKey}] {ex.Message}";
        _logger.LogWarning("{Op} failed: HTTP {Status} [{Key}] {Desc}",
            op, ex.StatusCode, ex.ErrorKey, ex.Message);
        NotifyChange();
    }

    private async Task<TResponse?> TryRequestAsync<TResponse>(
        string op, RestCommand<TResponse> command, CancellationToken cancellationToken)
        where TResponse : HomeConnectResponse
    {
        if (_client is null) throw new InvalidOperationException("Client not connected.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(StatusRequestTimeout);
        try
        {
            return await _client.RequestAsync(command, cts.Token).ConfigureAwait(false);
        }
        catch (HomeConnectApiException ex)
        {
            ReportError(op, ex);
            return null;
        }
    }

    public void SetCallbackReceiver(Func<Uri, CancellationToken, Task<AuthorizationCode>> receiver)
        => _callbackReceiver = receiver;

    private HomeConnectAuthorizationCodeFlow CreateOAuthFlow()
    {
        var options = _callbackReceiver is not null
            ? _oauthOptions with
            {
                CallbackReceiver = _callbackReceiver,
                OnAuthorizeUrlReady = url =>
                {
                    PendingAuthorizeUrl = url;
                    NotifyChange();
                },
            }
            : _oauthOptions;
        return new HomeConnectAuthorizationCodeFlow(options);
    }

    private async Task ConnectInternalAsync(CancellationToken cancellationToken)
    {
        _system = ActorSystem.Create("home-connect-blazor");

        _connectedOAuth = CreateOAuthFlow();
        var builder = HomeConnectBuilder.Create().TokenProvider(_connectedOAuth.GetAccessTokenAsync);
        if (_useSimulator) builder.UseSimulator(); else builder.UseProduction();
        _client = builder.Build(_system);

        // Initial appliance list.
        try
        {
            var appliances = await _client.RequestAsync(new GetAppliancesCommand(), cancellationToken).ConfigureAwait(false);
            Appliances = appliances.Appliances;
            // Best-effort: prefetch status for connected appliances so the UI isn't empty on first click.
            foreach (var a in appliances.Appliances.Where(a => a.Connected))
            {
                _ = RefreshStatusAsync(a.HaId, CancellationToken.None);
            }
        }
        catch (HomeConnectApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Cached token is invalid/revoked — clearing auth state so the user can re-authorize.");
            await _tokenStore.ClearAsync(CancellationToken.None).ConfigureAwait(false);
            _client.Dispose();
            _client = null;
            _connectedOAuth?.Dispose();
            _connectedOAuth = null;
            if (_system is not null) { await _system.Terminate(); _system.Dispose(); _system = null; }
            IsAuthorized = false;
            LastError = "Session expired — please authorize again.";
            NotifyChange();
            return;
        }
        catch (HomeConnectApiException ex)
        {
            LastError = $"GetAppliances failed: HTTP {(int?)ex.StatusCode} [{ex.ErrorKey}] {ex.Message}";
        }

        // Subscribe to live events for all appliances and pump them into the buffer.
        await _client.SendAsync(new SubscribeEventsCommand(), cancellationToken).ConfigureAwait(false);
        _eventPump = Task.Run(PumpEventsAsync, CancellationToken.None);

        NotifyChange();
    }

    private async Task PumpEventsAsync()
    {
        if (_client is null) return;
        try
        {
            await foreach (var message in _client.Responses.ReadAllAsync().ConfigureAwait(false))
            {
                switch (message)
                {
                    case HomeConnectEventMessage evt:
                        var listeners = OnStateChanged?.GetInvocationList().Length ?? 0;
                        _logger.LogInformation("← SSE {Type} from {HaId} ({Items} items, {Listeners} listeners)",
                            evt.Type, evt.HaId ?? "(all)", evt.Items.Count, listeners);
                        ApplyConnectionState(evt);
                        ApplyEventItemsToCaches(evt);
                        RecordEvent(evt);
                        NotifyChange();
                        break;
                    case SubscriptionDisconnectedMessage drop:
                        _logger.LogWarning("SSE subscription dropped (haId={HaId}): {Reason}",
                            drop.HaId, drop.Reason?.Message);
                        NotifyChange();
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event pump terminated unexpectedly.");
            LastError = ex.Message;
            NotifyChange();
        }
    }

    /// <summary>
    /// STATUS events carry status-key changes; NOTIFY events carry setting / option changes.
    /// Patch them into the relevant caches so the UI reflects what the SSE stream is carrying
    /// without waiting for a manual refresh.
    /// </summary>
    private void ApplyEventItemsToCaches(HomeConnectEventMessage evt)
    {
        if (evt.HaId is null || evt.Items.Count == 0) return;

        if (evt.Type == HomeConnectEventType.Status)
        {
            _statusByHaId[evt.HaId] = MergeStatus(_statusByHaId.TryGetValue(evt.HaId, out var s) ? s : [], evt.Items);
        }
        else if (evt.Type == HomeConnectEventType.Notify)
        {
            // NOTIFY carries both setting updates (e.g. PowerState) AND program-option updates
            // (RemainingProgramTime, ProgramProgress, …) — patch each into the right cache.
            _settingsByHaId[evt.HaId] = MergeSettings(_settingsByHaId.TryGetValue(evt.HaId, out var s) ? s : [], evt.Items);

            // Patch the active program's options for any "*.Option.*" keys.
            if (_activeProgramByHaId.TryGetValue(evt.HaId, out var active) && active is not null)
            {
                var merged = MergeProgramOptions(active.Options ?? [], evt.Items);
                _activeProgramByHaId[evt.HaId] = active with { Options = merged };
            }

            // The active program itself changed (start / select / stop) — re-fetch programs.
            if (evt.Items.Any(i => i.Key == "BSH.Common.Root.ActiveProgram"))
            {
                _ = RefreshProgramsAsync(evt.HaId, CancellationToken.None);
            }
        }
    }

    private static IReadOnlyList<ProgramOption> MergeProgramOptions(
        IReadOnlyList<ProgramOption> current,
        IReadOnlyList<EventItem> incoming)
    {
        var dict = current.ToDictionary(o => o.Key);
        foreach (var item in incoming)
        {
            // Only merge keys that look like program options ("*.Option.*").
            if (!item.Key.Contains(".Option.", StringComparison.Ordinal)) continue;
            dict.TryGetValue(item.Key, out var existing);
            dict[item.Key] = new ProgramOption(item.Key, item.Value)
            {
                Name         = existing?.Name,
                DisplayValue = item.DisplayValue ?? existing?.DisplayValue,
                Unit         = item.Unit         ?? existing?.Unit,
                Type         = existing?.Type,
                Constraints  = existing?.Constraints,
                LiveUpdate   = existing?.LiveUpdate,
            };
        }
        return dict.Values.ToList();
    }

    private static IReadOnlyList<StatusValue> MergeStatus(IReadOnlyList<StatusValue> current, IReadOnlyList<EventItem> incoming)
    {
        var dict = current.ToDictionary(s => s.Key);
        foreach (var item in incoming)
        {
            dict.TryGetValue(item.Key, out var existing);
            dict[item.Key] = new StatusValue(item.Key, item.Value)
            {
                Name         = existing?.Name,
                DisplayValue = item.DisplayValue ?? existing?.DisplayValue,
                Unit         = item.Unit         ?? existing?.Unit,
                Type         = existing?.Type,
                Constraints  = existing?.Constraints,
            };
        }
        return dict.Values.ToList();
    }

    private static IReadOnlyList<SettingValue> MergeSettings(IReadOnlyList<SettingValue> current, IReadOnlyList<EventItem> incoming)
    {
        var dict = current.ToDictionary(s => s.Key);
        foreach (var item in incoming)
        {
            // Only merge keys we've already seen as settings — option-keys live on the program endpoint.
            if (!dict.TryGetValue(item.Key, out var existing)) continue;
            dict[item.Key] = new SettingValue(existing.Key, item.Value)
            {
                Name         = existing.Name,
                DisplayValue = item.DisplayValue ?? existing.DisplayValue,
                Unit         = item.Unit         ?? existing.Unit,
                Type         = existing.Type,
                Constraints  = existing.Constraints,
            };
        }
        return dict.Values.ToList();
    }

    private void ApplyConnectionState(HomeConnectEventMessage evt)
    {
        if (evt.HaId is null) return;
        bool? connected = evt.Type switch
        {
            HomeConnectEventType.Connected => true,
            HomeConnectEventType.Disconnected => false,
            _ => null,
        };
        if (connected is null) return;

        var current = Appliances;
        var updated = new List<HomeAppliance>(current.Count);
        var changed = false;
        foreach (var a in current)
        {
            if (a.HaId == evt.HaId && a.Connected != connected.Value)
            {
                updated.Add(a with { Connected = connected.Value });
                changed = true;
            }
            else
            {
                updated.Add(a);
            }
        }
        if (changed)
        {
            Appliances = updated;
        }
    }

    private void RecordEvent(HomeConnectEventMessage evt)
    {
        lock (_eventsGate)
        {
            _events.AddFirst(evt);
            while (_events.Count > EventBufferSize)
            {
                _events.RemoveLast();
            }
        }
    }

    private void NotifyChange()
    {
        try
        {
            OnStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscriber threw while handling state change.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_eventPump is not null)
        {
            try { await _eventPump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch { /* shutdown best-effort */ }
        }
        if (_system is not null)
        {
            await _system.Terminate().ConfigureAwait(false);
            _system.Dispose();
        }
        _connectedOAuth?.Dispose();
        if (_tokenStore is IDisposable d) d.Dispose();
        _startGate.Dispose();
    }
}

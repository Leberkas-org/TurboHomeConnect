using TurboHomeConnect.Abstractions;

namespace TurboHomeConnect;

/// <summary>
/// Closed root of the command hierarchy. Only two kinds exist:
/// <see cref="RestCommandBase"/> (request/response) and <see cref="SubscribeCommand"/> (SSE).
/// The <c>private protected</c> constructor prevents third-party derivation.
/// </summary>
public abstract record HomeConnectCommand
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();

    private protected HomeConnectCommand() { }
    protected HomeConnectCommand(HomeConnectCommand original) => CorrelationId = original.CorrelationId;

    /// <summary>Builds the outgoing <see cref="HttpRequestMessage"/> for this command.</summary>
    protected internal abstract HttpRequestMessage BuildRequest();
}

/// <summary>
/// Non-generic handle for routing. The flow dispatches REST commands through this type
/// and calls <see cref="MapResponseCoreAsync"/> to produce a typed <see cref="ICorrelatedResponse"/>.
/// </summary>
public abstract record RestCommandBase : HomeConnectCommand
{
    private protected RestCommandBase() { }
    protected RestCommandBase(RestCommandBase original) : base(original) { }

    internal abstract Task<ICorrelatedResponse> MapResponseCoreAsync(
        HttpResponseMessage response, CancellationToken cancellationToken);
}

/// <summary>
/// Strongly-typed REST command. Derive from this to implement a request/response command
/// whose response is <typeparamref name="TResponse"/>.
/// </summary>
public abstract record RestCommand<TResponse> : RestCommandBase
    where TResponse : HomeConnectResponse
{
    protected RestCommand() { }
    protected RestCommand(RestCommand<TResponse> original) : base(original) { }

    internal sealed override async Task<ICorrelatedResponse> MapResponseCoreAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
        => await MapResponseAsync(response, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Called with a successful HTTP response (2xx). Read the body as needed and return
    /// the typed response. The dispatcher handles 4xx/5xx and transport exceptions.
    /// </summary>
    protected abstract Task<TResponse> MapResponseAsync(
        HttpResponseMessage response, CancellationToken cancellationToken);
}

/// <summary>
/// SSE subscription command. Opens a long-lived event stream.
/// </summary>
public abstract record SubscribeCommand : HomeConnectCommand
{
    protected SubscribeCommand() { }
    protected SubscribeCommand(SubscribeCommand original) : base(original) { }
}

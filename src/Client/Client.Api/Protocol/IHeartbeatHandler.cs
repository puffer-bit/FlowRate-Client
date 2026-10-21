namespace Client.Api.Protocol;

internal interface IHeartbeatHandler
{
    /// <summary>
    /// Raised once when the server stops answering pings; the pinging stops after it.
    /// </summary>
    event Action Timeout;

    /// <summary>
    /// Raised after every answered ping with its round trip.
    /// </summary>
    event Action<TimeSpan> RoundTripMeasured;

    /// <summary>
    /// Pings the server until cancelled or until it stops answering.
    /// </summary>
    /// <param name="ping">Sends one ping and completes when the server answers it.</param>
    Task StartAsync(Func<Task> ping, CancellationToken cancellationToken);
}

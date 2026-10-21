using System.Diagnostics;
using Client.Application.Ports;

namespace Client.Api.Protocol;

internal class HeartbeatHandler : IHeartbeatHandler
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(5);
    private const int MaxMissedPings = 3;

    public event Action? Timeout;
    public event Action<TimeSpan>? RoundTripMeasured;

    public async Task StartAsync(Func<Task> ping, CancellationToken cancellationToken)
    {
        var missedPings = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var sentAt = Stopwatch.GetTimestamp();

                try
                {
                    await ping().WaitAsync(PingTimeout, cancellationToken);

                    missedPings = 0;
                    RoundTripMeasured?.Invoke(Stopwatch.GetElapsedTime(sentAt));
                }
                catch (ConnectionLostException)
                {
                    // The socket reports the loss on its own
                    break;
                }
                catch (RequestRejectedException)
                {
                    // Refused by the rate limit, but an answer still proves the server is alive
                    missedPings = 0;
                }
                catch (TimeoutException)
                {
                    if (++missedPings >= MaxMissedPings)
                    {
                        Timeout?.Invoke();
                        break;
                    }
                }

                var untilNextPing = PingInterval - Stopwatch.GetElapsedTime(sentAt);
                if (untilNextPing > TimeSpan.Zero)
                    await Task.Delay(untilNextPing, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }
}

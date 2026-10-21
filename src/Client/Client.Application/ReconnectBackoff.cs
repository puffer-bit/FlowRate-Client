namespace Client.Application;

/// <summary>
/// Pauses before reconnect attempts, shared by the coordinator and the hybrid sessions.
/// </summary>
internal static class ReconnectBackoff
{
    // The last one repeats until the owner gives up
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30)
    ];

    /// <param name="attempt">Number of the attempt, starting from 1.</param>
    public static TimeSpan GetDelay(int attempt) => Delays[Math.Min(attempt - 1, Delays.Length - 1)];

    /// <summary>The pause between attempts that have no network to go through.</summary>
    public static TimeSpan LongestDelay => Delays[^1];

    /// <summary>
    /// <see cref="GetDelay"/> moved by up to a fifth either way, so that the clients of a server
    /// that restarted do not all come back in the same instant.
    /// </summary>
    public static TimeSpan GetSpreadDelay(int attempt) => GetDelay(attempt) * (0.8 + Random.Shared.NextDouble() * 0.4);
}

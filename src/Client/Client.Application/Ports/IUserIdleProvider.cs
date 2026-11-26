namespace Client.Application.Ports;

/// <summary>How long the whole system has gone without keyboard, mouse or touch input.</summary>
public interface IUserIdleProvider
{
    /// <summary>The time since the last input of the user in any application; null where it cannot be known.</summary>
    TimeSpan? GetIdleTime();
}

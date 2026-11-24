namespace Client.Application.Ports;

/// <summary>UI-thread leases that keep the display awake while video is being watched.</summary>
public interface IScreenAwakeService : IDisposable
{
    IDisposable Acquire();
}

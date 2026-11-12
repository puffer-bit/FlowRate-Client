namespace Client.Application.Ports;

public sealed record CameraDevice(string Id, string Name);

public interface ICameraDeviceProvider
{
    IReadOnlyList<CameraDevice> GetDevices();
}

namespace Client.Core.Media;

/// <summary>Capture choices and the last successfully started kind of video.</summary>
public sealed record CameraPreferences
{
    public string? DeviceId { get; init; }
    public int MaxHeight { get; init; } = 720;
    public int Framerate { get; init; } = 30;
    public int? BitrateKbps { get; init; }
    public bool LastWasCamera { get; init; }

    public ScreenShareSettings CaptureSettings => new ScreenShareSettings
    {
        MaxHeight = MaxHeight, Framerate = Framerate, BitrateKbps = BitrateKbps
    }.Normalize();
}

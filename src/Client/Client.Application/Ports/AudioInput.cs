namespace Client.Application.Ports;

/// <summary>
/// Where an outbound audio track takes its sound from. Kept free of media engine types, so that
/// code without a reference to the engine can choose the input.
/// </summary>
public abstract record AudioInput;

/// <summary>
/// A capture device.
/// </summary>
/// <param name="DeviceId">Id from the device list, or null for the system default device.</param>
public sealed record DeviceAudioInput(string? DeviceId) : AudioInput;

/// <summary>
/// A file played in a loop, prepared by the media engine beforehand.
/// </summary>
/// <param name="PreparedPcmPath">Raw 48 kHz mono S16LE audio.</param>
public sealed record FileAudioInput(string PreparedPcmPath) : AudioInput;

/// <summary>
/// A continuous sine tone.
/// </summary>
public sealed record ToneAudioInput(double Frequency = 440) : AudioInput;

/// <summary>
/// The sound a process and the processes it started play, or everything the system plays but
/// that. Captured from the audio engine of Windows, before it reaches any device.
/// </summary>
/// <param name="ProcessId">The process whose tree is captured, or left out.</param>
/// <param name="IsExcluded">
/// Captures everything except the tree: with our own process, the system sound without the voices
/// we play, which the others would otherwise hear back.
/// </param>
public sealed record ProcessLoopbackAudioInput(uint ProcessId, bool IsExcluded) : AudioInput;

/// <summary>
/// The sound an application plays through PipeWire: all of its streams, the ones it opens later
/// included, mixed together.
/// </summary>
/// <param name="AppKey">The <see cref="AudioApp.Key"/> of the application.</param>
/// <param name="Apps">Where the streams of the application are followed while it is captured.</param>
public sealed record PipeWireAppAudioInput(string AppKey, IAudioAppProvider Apps) : AudioInput;

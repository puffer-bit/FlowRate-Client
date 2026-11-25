using System;

namespace Client.Application.Ports;

/// <summary>
/// Leases that keep the system from throttling the process while it carries live voice: macOS
/// App Nap would otherwise slow the audio and network threads of a hidden, idle application
/// until the voice stops both ways. Any thread.
/// </summary>
public interface IProcessActivityService
{
    IDisposable BeginVoiceActivity();
}

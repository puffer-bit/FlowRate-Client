using System.Diagnostics;
using System.Runtime.InteropServices;
using Client.Core.Media;
using Client.Core.Settings;

namespace Client.LibWebRTC.Native;

/// <summary>
/// The voice gate of the microphone of libwebrtc. The capture processing of flowrate_webrtc measures
/// every 10 ms and asks here whether the gate is open; the ramps and the pre-roll are its own. There
/// is one microphone in the process, so there is one gate.
/// </summary>
internal static unsafe class NativeVoiceGate
{
    private static readonly Lock Lock = new();
    private static readonly long Start = Stopwatch.GetTimestamp();
    private static VoiceGateDecision? _decision;

    /// <param name="options">The gate, or null to send the microphone all the time.</param>
    public static void Set(VoiceGateOptions? options)
    {
        lock (Lock)
        {
            if (options == null)
            {
                _decision = null;
                NativeMethods.SetVoiceGate(null, 0);
                return;
            }

            if (_decision == null)
                _decision = new VoiceGateDecision(options);
            else
                _decision.SetOptions(options, Now, _decision.IsOpen);

            NativeMethods.SetVoiceGate(&OnLevel, 0);
        }
    }

    /// <summary>What the gate did since the previous call, or null while it is off.</summary>
    public static VoiceGateSummary? TakeSummary()
    {
        lock (Lock)
            return _decision?.TakeSummary();
    }

    private static TimeSpan Now => Stopwatch.GetElapsedTime(Start);

    // On the audio thread of libwebrtc; an exception may not leave it
    [UnmanagedCallersOnly]
    private static int OnLevel(nint context, double peakDb)
    {
        try
        {
            lock (Lock)
            {
                if (_decision == null)
                    return 1;

                _decision.OnLevel(peakDb, Now);
                return _decision.IsOpen ? 1 : 0;
            }
        }
        catch
        {
            return 1;
        }
    }
}

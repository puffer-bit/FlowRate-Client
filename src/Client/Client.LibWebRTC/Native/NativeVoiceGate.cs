using System.Diagnostics;
using System.Runtime.InteropServices;
using Client.Core.Media;
using Client.Core.Settings;

namespace Client.LibWebRTC.Native;

/// <summary>
/// The voice gate of the microphone of libwebrtc. The capture processing of flowrate_webrtc measures
/// every 10 ms and asks here whether the gate is open; the ramps and the pre-roll are its own. There
/// is one microphone in the process, so there is one gate: each peer that sends the microphone sets
/// its own options, and the gate follows the peer that set them last, until it lets go of them.
/// </summary>
internal static unsafe class NativeVoiceGate
{
    // Held by the audio thread while it asks the gate, from inside the lock of the capture processing.
    // So it is never held while the capture processing is called, which waits for that lock: the two
    // threads would wait for each other for good, and the microphone would be silent until a restart
    private static readonly Lock Lock = new();

    // Keeps the changes in order, and is never taken by the audio thread
    private static readonly Lock ChangeLock = new();

    private static readonly long Start = Stopwatch.GetTimestamp();
    private static VoiceGateDecision? _decision;

    // The options of every peer that holds the gate, the one set last at the end. Under ChangeLock
    private static readonly List<(object Owner, VoiceGateOptions Options)> Owners = [];

    /// <param name="owner">The peer the options are of.</param>
    /// <param name="options">The gate, or null when that peer no longer gates the microphone: the gate
    /// goes back to the options of another peer, or sends the microphone all the time.</param>
    public static void Set(object owner, VoiceGateOptions? options)
    {
        lock (ChangeLock)
        {
            Owners.RemoveAll(entry => ReferenceEquals(entry.Owner, owner));

            if (options != null)
                Owners.Add((owner, options));

            Apply(Owners.Count > 0 ? Owners[^1].Options : null);
        }
    }

    // Under ChangeLock
    private static void Apply(VoiceGateOptions? options)
    {
        if (options == null)
        {
            NativeMethods.SetVoiceGate(null, 0);

            lock (Lock)
                _decision = null;

            return;
        }

        lock (Lock)
        {
            if (_decision == null)
                _decision = new VoiceGateDecision(options);
            else
                _decision.SetOptions(options, Now, _decision.IsOpen);
        }

        NativeMethods.SetVoiceGate(&OnLevel, 0);
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

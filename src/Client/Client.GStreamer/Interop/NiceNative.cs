using System.Runtime.InteropServices;

namespace Client.GStreamer.Interop;

/// <summary>
/// What libnice and the socket under it give no property for: the socket the media goes over, and
/// the signal its sink waits for while that socket is full.
/// </summary>
public static class NiceNative
{
    // The component of RTP; webrtcbin muxes RTCP into it
    private const uint RtpComponent = 1;

    // webrtcbin bundles everything into one stream, but its id is not exposed: the first ones are tried
    private const uint MaxStreamId = 16;

    [DllImport(NativeLibraries.Nice)]
    private static extern IntPtr nice_agent_get_selected_socket(IntPtr agent, uint streamId, uint componentId);

    [DllImport(NativeLibraries.Gio)]
    private static extern int g_socket_set_option(IntPtr socket, int level, int option, int value, out IntPtr error);

    [DllImport(NativeLibraries.GLib)]
    private static extern void g_error_free(IntPtr error);

    [DllImport(NativeLibraries.GObject)]
    private static extern void g_signal_emit_by_name(IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, uint streamId, uint componentId);

    /// <summary>
    /// The stream of the agent that has a selected pair, and a reference to the socket of that pair,
    /// to be given back with <see cref="GstNative.Release"/>.
    /// </summary>
    /// <returns>False while no pair is selected.</returns>
    public static bool TryGetSelectedSocket(IntPtr agent, out uint streamId, out IntPtr socket)
    {
        for (streamId = 1; streamId <= MaxStreamId; streamId++)
        {
            socket = nice_agent_get_selected_socket(agent, streamId, RtpComponent);

            if (socket != IntPtr.Zero)
                return true;
        }

        streamId = 0;
        socket = IntPtr.Zero;
        return false;
    }

    /// <summary>
    /// Sets the send buffer of a socket.
    /// </summary>
    /// <returns>False when the system refused it.</returns>
    public static bool SetSendBuffer(IntPtr socket, int bytes)
    {
        // SOL_SOCKET and SO_SNDBUF
        var (level, option) = OperatingSystem.IsWindows() ? (0xFFFF, 0x1001) : (1, 7);

        if (g_socket_set_option(socket, level, option, bytes, out var error) != 0)
            return true;

        if (error != IntPtr.Zero)
            g_error_free(error);

        return false;
    }

    /// <summary>
    /// Tells the sinks of the agent that the socket of the stream takes data again. A nicesink that
    /// found the socket full waits for this, and it is raised only when the system reports the
    /// socket writable, which on Windows can get lost: see WebRTCBinTuning.WakeSender.
    /// </summary>
    public static void SignalWritable(IntPtr agent, uint streamId) =>
        g_signal_emit_by_name(agent, "reliable-transport-writable", streamId, RtpComponent);
}

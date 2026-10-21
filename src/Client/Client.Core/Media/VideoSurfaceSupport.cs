namespace Client.Core.Media;

/// <summary>
/// Ways a decoded frame can reach the screen without passing through memory.
/// </summary>
[Flags]
public enum VideoSurfaceKinds
{
    None = 0,

    /// <summary>DMA-BUF frames imported into OpenGL through EGL (VA-API decoders on Linux).</summary>
    DmaBuf = 1,

    /// <summary>CUDA frames copied into OpenGL textures on the GPU (NVIDIA decoders).</summary>
    CudaGl = 2,

    /// <summary>Direct3D 11 textures shared with the compositor (Windows).</summary>
    D3D11Shared = 4
}

/// <summary>
/// What the user interface can show without copying. The interface fills it in once it found out,
/// and the media layer reads it whenever it builds a video stream; frames of a kind that is not
/// here are delivered in memory.
/// </summary>
public static class VideoSurfaceSupport
{
    private static int _kinds;

    public static VideoSurfaceKinds Kinds => (VideoSurfaceKinds)Volatile.Read(ref _kinds);

    /// <summary>
    /// Raised when a kind turned out not to work after all, on the thread that found it out.
    /// Streams that deliver it are rebuilt to deliver frames in memory.
    /// </summary>
    public static event Action<VideoSurfaceKinds>? Failed;

    public static void Set(VideoSurfaceKinds kinds) => Volatile.Write(ref _kinds, (int)kinds);

    public static bool Supports(VideoSurfaceKinds kind) => (Kinds & kind) == kind && kind != VideoSurfaceKinds.None;

    /// <summary>
    /// Takes a kind out of the supported ones, once, and tells the media layer.
    /// </summary>
    public static void ReportFailure(VideoSurfaceKinds kind)
    {
        int current;

        do
        {
            current = Volatile.Read(ref _kinds);

            if ((current & (int)kind) == 0)
                return;
        } while (Interlocked.CompareExchange(ref _kinds, current & ~(int)kind, current) != current);

        Failed?.Invoke(kind);
    }
}

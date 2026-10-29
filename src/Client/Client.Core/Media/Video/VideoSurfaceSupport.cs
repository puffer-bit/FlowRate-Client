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
    D3D11Shared = 4,

    /// <summary>
    /// YUV planes in memory, uploaded as they are and turned into RGB by a shader (any OpenGL
    /// presenter); they still pass through memory, but are neither converted nor copied by the CPU.
    /// </summary>
    YuvMemory = 8
}

/// <summary>
/// What the user interface can show without copying. The interface fills it in once it found out,
/// and the media layer reads it whenever it builds a video stream; frames of a kind that is not
/// here are delivered in memory.
/// </summary>
public static class VideoSurfaceSupport
{
    private static int _kinds;
    private static int _failedKinds;

    public static VideoSurfaceKinds Kinds => (VideoSurfaceKinds)Volatile.Read(ref _kinds);

    /// <summary>The kinds taken out after they failed, for good until the application starts again.</summary>
    public static VideoSurfaceKinds FailedKinds => (VideoSurfaceKinds)Volatile.Read(ref _failedKinds);

    /// <summary>The user interface found out what it supports; until then <see cref="Kinds"/> is empty.</summary>
    public static bool IsProbed { get; private set; }

    /// <summary>
    /// Raised when a kind turned out not to work after all, on the thread that found it out.
    /// Streams that deliver it are rebuilt to deliver frames in memory.
    /// </summary>
    public static event Action<VideoSurfaceKinds>? Failed;

    public static void Set(VideoSurfaceKinds kinds)
    {
        Volatile.Write(ref _kinds, (int)kinds);
        IsProbed = true;
    }

    public static bool Supports(VideoSurfaceKinds kind) => (Kinds & kind) == kind && kind != VideoSurfaceKinds.None;

    /// <summary>Why the first kind that failed did, when the one who found out said so.</summary>
    public static string? FailureReason { get; private set; }

    /// <summary>
    /// Takes a kind out of the supported ones, once, and tells the media layer.
    /// </summary>
    /// <param name="reason">What failed, for the log and the user.</param>
    public static void ReportFailure(VideoSurfaceKinds kind, string? reason = null)
    {
        int current;

        do
        {
            current = Volatile.Read(ref _kinds);

            if ((current & (int)kind) == 0)
                return;
        } while (Interlocked.CompareExchange(ref _kinds, current & ~(int)kind, current) != current);

        FailureReason ??= reason;
        Interlocked.Or(ref _failedKinds, (int)kind);
        Failed?.Invoke(kind);
    }
}

namespace Client.Core.Media;

/// <summary>
/// How the frames of one watched stream are decoded and reach the screen, as its decoder reports it.
/// </summary>
/// <param name="Decoder">The element that decodes, such as <c>d3d11h264dec</c>.</param>
/// <param name="IsHardware">The GPU decodes, rather than the CPU.</param>
/// <param name="Surface">
/// The form of the frames: a GPU kind, <see cref="VideoSurfaceKinds.YuvMemory"/> for planes in memory,
/// or <see cref="VideoSurfaceKinds.None"/> for BGRA pixels in memory.
/// </param>
/// <param name="FailedSurface">
/// The GPU kind this stream delivered before it failed and the stream fell back to memory, or
/// <see cref="VideoSurfaceKinds.None"/>.
/// </param>
/// <param name="FailureReason">Why <paramref name="FailedSurface"/> failed, when that is known.</param>
public sealed record VideoDecoding(
    string Decoder,
    bool IsHardware,
    VideoSurfaceKinds Surface,
    VideoSurfaceKinds FailedSurface = VideoSurfaceKinds.None,
    string? FailureReason = null)
{
    /// <summary>The frames reach the screen without passing through memory.</summary>
    public bool IsZeroCopy => Surface is VideoSurfaceKinds.DmaBuf or VideoSurfaceKinds.CudaGl or VideoSurfaceKinds.D3D11Shared;
}

namespace Client.Core.Media;

/// <summary>
/// A decoded frame. Either its BGRA pixels are in memory at <see cref="DataPtr"/>, or it stayed on
/// the GPU and <see cref="Gpu"/> describes it; both are only valid during the call that delivers it.
/// </summary>
public readonly record struct RawVideoFrame(IntPtr DataPtr, int Width, int Height, int Stride, uint? SSRC = null)
{
    /// <summary>
    /// When the frame is to be shown, in nanoseconds of the stream, or -1 when it is not known.
    /// Only the differences between the frames of one stream mean something.
    /// </summary>
    public long Timestamp { get; init; } = -1;

    /// <summary>
    /// The frame in GPU memory, or null when the pixels are in memory. A receiver that keeps it
    /// past the call takes a reference with <see cref="GpuVideoFrame.Retain"/>.
    /// </summary>
    public GpuVideoFrame? Gpu { get; init; }
}

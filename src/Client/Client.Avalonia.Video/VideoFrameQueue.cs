using System;
using System.Buffers;
using Client.Core.Media;
using Client.Core.Settings;

namespace Client.Avalonia.Video;

/// <summary>
/// A frame waiting to be shown: the pixels copied out of the decoder, or a reference to a frame
/// that stayed on the GPU.
/// </summary>
internal sealed class PendingFrame
{
    private PendingFrame(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>BGRA rows without padding, from a pool; null for a GPU frame.</summary>
    public byte[]? Pixels { get; private set; }

    public GpuVideoFrame? Gpu { get; private set; }

    public int Stride => Width * 4;

    /// <returns>Null when a GPU frame was released already.</returns>
    public static unsafe PendingFrame? From(RawVideoFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
            return null;

        if (frame.Gpu != null)
            return frame.Gpu.Retain() is { } gpu ? new PendingFrame(frame.Width, frame.Height) { Gpu = gpu } : null;

        if (frame.DataPtr == IntPtr.Zero || frame.Stride < frame.Width * 4)
            return null;

        var pending = new PendingFrame(frame.Width, frame.Height);
        var rowBytes = frame.Width * 4;
        var pixels = ArrayPool<byte>.Shared.Rent(rowBytes * frame.Height);

        fixed (byte* destination = pixels)
        {
            if (frame.Stride == rowBytes)
            {
                Buffer.MemoryCopy((void*)frame.DataPtr, destination, pixels.Length, (long)rowBytes * frame.Height);
            }
            else
            {
                for (var row = 0; row < frame.Height; row++)
                    Buffer.MemoryCopy((byte*)frame.DataPtr + (long)row * frame.Stride, destination + (long)row * rowBytes,
                        rowBytes, rowBytes);
            }
        }

        pending.Pixels = pixels;
        return pending;
    }

    public void Release()
    {
        if (Pixels is { } pixels)
        {
            Pixels = null;
            ArrayPool<byte>.Shared.Return(pixels);
        }

        Gpu?.Dispose();
        Gpu = null;
    }
}

/// <summary>
/// The frames of one stream between the thread that decodes them and the refreshes that show
/// them, paced as the video settings say.
/// </summary>
internal sealed class VideoFrameQueue : IDisposable
{
    private readonly FramePacer<PendingFrame> _pacer = new(frame => frame.Release());

    /// <summary>Takes a frame on the thread of its stream.</summary>
    /// <returns>False when the frame could not be taken.</returns>
    public bool Enqueue(RawVideoFrame frame)
    {
        var pending = PendingFrame.From(frame);

        if (pending == null)
            return false;

        Configure();
        _pacer.Enqueue(pending, frame.Timestamp);
        return true;
    }

    /// <summary>The frame to show now, which the caller releases once it is replaced, or null.</summary>
    public PendingFrame? Pick()
    {
        Configure();
        return _pacer.Pick();
    }

    public FrameStats Stats => _pacer.Stats;

    public bool HasPending => _pacer.HasPending;

    /// <summary>Gives back the frames that wait.</summary>
    public void Clear() => _pacer.Reset();

    public void Dispose() => _pacer.Dispose();

    private void Configure()
    {
        var settings = VideoSettings.Current;

        _pacer.IsTimed = settings.IsTimedPresentationEnabled;
        _pacer.BufferFrames = settings.PresentationBufferFrames;
    }
}

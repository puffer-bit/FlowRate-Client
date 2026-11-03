using System.Runtime.InteropServices;
using Client.Core.Media;

namespace Client.GStreamer.Video;

/// <summary>A small still frame, independent of the decoder that produced it.</summary>
public sealed record ChatVideoPreview(byte[] Pixels, int Width, int Height, int Stride)
{
    private static readonly SemaphoreSlim Decoders = new(2, 2);

    public void DeliverTo(Action<RawVideoFrame> onFrame)
    {
        var pinned = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
        try { onFrame(new RawVideoFrame(pinned.AddrOfPinnedObject(), Width, Height, Stride)); }
        finally { pinned.Free(); }
    }

    public static async Task<ChatVideoPreview> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await Decoders.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(async () =>
            {
                var firstFrame = new TaskCompletionSource<ChatVideoPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var playback = new ChatVideoPlayback(path, frame =>
                {
                    if (firstFrame.Task.IsCompleted)
                        return;
                    var pixels = new byte[checked(frame.Stride * frame.Height)];
                    Marshal.Copy(frame.DataPtr, pixels, 0, pixels.Length);
                    firstFrame.TrySetResult(new ChatVideoPreview(pixels, frame.Width, frame.Height, frame.Stride));
                }, volume: 0, preview: true);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                while (!firstFrame.Task.IsCompleted)
                {
                    playback.Update();
                    await Task.WhenAny(firstFrame.Task, Task.Delay(50, timeout.Token));
                    timeout.Token.ThrowIfCancellationRequested();
                }
                return await firstFrame.Task;
            }, cancellationToken);
        }
        finally { Decoders.Release(); }
    }
}

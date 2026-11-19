using System.Runtime.InteropServices;
using Client.Avalonia.Video;
using Client.Core.Media;
using Xunit;

namespace Client.Application.Tests;

public class VideoFrameQueueTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    public void CopiesRowsWithoutPaddingAndOwnsThePixels(int stride)
    {
        var source = Marshal.AllocHGlobal(stride * 2);
        PendingFrame? pending = null;
        try
        {
            byte[] first = [1, 2, 3, 4, 5, 6, 7, 8];
            byte[] second = [9, 10, 11, 12, 13, 14, 15, 16];
            Marshal.Copy(first, 0, source, first.Length);
            Marshal.Copy(second, 0, source + stride, second.Length);
            pending = PendingFrame.From(new RawVideoFrame(source, 2, 2, stride));
            Assert.NotNull(pending);
            Marshal.WriteByte(source, 0);
            Assert.Equal(first.Concat(second), pending.Pixels!.Take(16));
            Assert.Equal(8, pending.Stride);
        }
        finally
        {
            pending?.Release();
            Marshal.FreeHGlobal(source);
        }
    }

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(1, -1, 4)]
    [InlineData(2, 1, 4)]
    [InlineData(int.MaxValue, 1, int.MaxValue)]
    [InlineData(1, int.MaxValue, 4)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    public void RejectsInvalidSizesBeforeAccessingNativeMemory(int width, int height, int stride)
    {
        Assert.Null(PendingFrame.From(new RawVideoFrame(new IntPtr(1), width, height, stride)));
    }
}

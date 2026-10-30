using System.Diagnostics;

namespace Client.GStreamer.Video;

/// <summary>
/// The time a test picture was drawn, written into its top row as black and white blocks, so that
/// whoever decodes the picture on the same machine can tell how long the frame took to arrive.
/// </summary>
/// <remarks>
/// 32 bits of milliseconds of the monotonic clock and 8 bits of a checksum, one block of 16 pixels
/// each, aligned to the macroblocks so that the encoder keeps them sharp. The monotonic clock is
/// shared by the processes of one machine, so two bots in separate processes can measure as well.
/// A picture scaled down on the way is read at the same relative places.
/// </remarks>
public static class TestPatternStamp
{
    private const int Block = 16;
    private const int ValueBits = 32;
    private const int CheckBits = 8;
    private const int Blocks = ValueBits + CheckBits;

    /// <summary>The width of the picture the blocks are laid out for.</summary>
    public const int Width = Blocks * Block;

    /// <summary>Milliseconds of the monotonic clock, wrapping around every 49 days.</summary>
    public static uint Now() => (uint)(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);

    /// <summary>
    /// Writes <paramref name="value"/> into the top <see cref="Block"/> rows of a BGRx picture of
    /// <see cref="Width"/> pixels.
    /// </summary>
    public static void Write(Span<byte> pixels, uint value)
    {
        var bits = (ulong)value | (ulong)Checksum(value) << ValueBits;

        for (var block = 0; block < Blocks; block++)
        {
            var shade = (byte)((bits >> block & 1) == 1 ? 255 : 0);

            for (var y = 0; y < Block; y++)
                pixels.Slice((y * Width + block * Block) * 4, Block * 4).Fill(shade);
        }
    }

    /// <summary>
    /// Reads the stamp of a decoded BGRA picture, of any size the test picture was scaled to.
    /// </summary>
    /// <returns>False when the blocks do not add up, as in a picture that is not the test one.</returns>
    public static unsafe bool TryRead(IntPtr data, int width, int height, int stride, out uint value)
    {
        value = 0;

        if (data == IntPtr.Zero || width < Blocks || height < 2 || stride < width * 4)
            return false;

        var pixels = (byte*)data;
        // The middle of the row of blocks; at 640x360 it is the eighth row
        var y = Block / 2 * height / 360;
        ulong bits = 0;

        for (var block = 0; block < Blocks; block++)
        {
            var x = (block * Block + Block / 2) * width / Width;
            var pixel = pixels + (long)y * stride + x * 4;

            // Green carries the most of the brightness and is the least smeared by the chroma subsampling
            if (pixel[1] >= 128)
                bits |= 1UL << block;
        }

        value = (uint)bits;
        return (byte)(bits >> ValueBits) == Checksum(value);
    }

    private static byte Checksum(uint value) =>
        (byte)((value ^ value >> 8 ^ value >> 16 ^ value >> 24) + 0x5A);
}

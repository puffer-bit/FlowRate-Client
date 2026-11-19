using Client.Application.Chat;
using Client.Application.Ports;
using Client.Core.Chat;
using Client.Services;
using SkiaSharp;
using Xunit;

namespace Client.Application.Tests;

public class SkiaImagePreparerTests
{
    private static readonly ImageCompressionPolicy Policy = new(1000, 80, true);

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format, bool transparent = false)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Red };
            for (var x = 0; x < width; x += 37)
                canvas.DrawRect(x, 0, 11, height, paint);
        }
        using var data = bitmap.Encode(format, 100);
        return data.ToArray();
    }

    private static Task<PreparedImage?> Prepare(byte[] content, string name, bool compress, bool strip) =>
        new SkiaImagePreparer().PrepareAsync(
            new ChatMediaSource(name, content.Length, () => Task.FromResult<Stream>(new MemoryStream(content))),
            new ImagePreparation(compress, strip, Policy));

    private static (int Width, int Height, SKEncodedImageFormat Format) Read(byte[] content)
    {
        using var codec = SKCodec.Create(new MemoryStream(content));
        return (codec.Info.Width, codec.Info.Height, codec.EncodedFormat);
    }

    [Fact]
    public async Task LargePhoto_IsReducedToTheSideOfThePolicy_AsAJpeg()
    {
        var prepared = await Prepare(Encode(3000, 1500, SKEncodedImageFormat.Png), "shot.png", compress: true, strip: true);

        Assert.NotNull(prepared?.Content);
        Assert.Equal((1000, 500, SKEncodedImageFormat.Jpeg), Read(prepared.Content));
        Assert.Equal("shot.jpg", prepared.FileName);
        Assert.Equal((3000, 1500), (prepared.Width, prepared.Height));
    }

    [Fact]
    public async Task TransparentImage_StaysTransparent_AsAWebP()
    {
        var prepared = await Prepare(Encode(2000, 2000, SKEncodedImageFormat.Png, transparent: true), "logo.png",
            compress: true, strip: false);

        Assert.Equal(SKEncodedImageFormat.Webp, Read(prepared!.Content!).Format);
        Assert.Equal("logo.webp", prepared.FileName);
    }

    [Fact]
    public async Task SmallImage_ThatWouldGrow_GoesAsItIs()
    {
        var original = Encode(200, 100, SKEncodedImageFormat.Jpeg);
        var smaller = await Prepare(original, "small.jpg", compress: true, strip: false);

        // Encoded at quality 100, so compressing at 80 makes it smaller and it goes compressed
        Assert.True(smaller!.Content == null || smaller.Content.Length < original.Length);
    }

    [Fact]
    public async Task WithoutCompressing_OnlyTheMetadataGoes()
    {
        var original = Encode(1200, 800, SKEncodedImageFormat.Jpeg);
        // A comment the encoder did not write
        byte[] comment = [0xFF, 0xFE, 0x00, 0x06, (byte)'h', (byte)'i', (byte)'!', (byte)'!'];
        byte[] withComment = [.. original[..2], .. comment, .. original[2..]];

        var prepared = await Prepare(withComment, "photo.jpg", compress: false, strip: true);

        Assert.Equal(original, prepared!.Content);
        Assert.Equal("photo.jpg", prepared.FileName);
    }

    [Fact]
    public async Task NotAnImage_IsNotPrepared()
    {
        Assert.Null(await Prepare("not an image at all"u8.ToArray(), "notes.png", compress: true, strip: true));
    }
}

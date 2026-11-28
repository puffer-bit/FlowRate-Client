using System.Buffers.Binary;
using System.Text;
using Client.Application.Chat;
using Xunit;

namespace Client.Application.Tests;

public class ImageMetadataTests
{
    // A segment of a JPEG: marker, length, payload
    private static byte[] Segment(byte marker, byte[] payload)
    {
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);
        return segment;
    }

    // An EXIF block in Intel order with an orientation and a GPS pointer, as a phone writes it
    private static byte[] Exif(ushort orientation)
    {
        var tiff = new byte[8 + 2 + 2 * 12 + 4];
        "II"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 2);
        // GPS IFD pointer
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10), 0x8825);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(18), 999);
        // Orientation
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(22), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(24), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(30), orientation);
        return [.. "Exif\0\0"u8, .. tiff];
    }

    // The pointer to the GPS block of Exif(): tag 0x8825, LONG, 1, 999
    private static bool ContainsGpsPointer(byte[] content) =>
        content.AsSpan().IndexOf((ReadOnlySpan<byte>)[0x25, 0x88, 4, 0, 1, 0, 0, 0, 0xE7, 0x03, 0, 0]) >= 0;

    private static readonly byte[] Jfif = Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
    private static readonly byte[] Icc = Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, 42, 42]);
    private static readonly byte[] Frame = Segment(0xC0, [8, 0, 16, 0, 16, 3, 1, 0x11, 0, 2, 0x11, 1, 3, 0x11, 1]);

    // A scan with a stuffed 0xFF and a restart marker in its data, then the end of the image
    private static readonly byte[] Scan = [.. Segment(0xDA, [1, 1, 0, 0, 63, 0]), 0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56, 0xFF, 0xD9];

    private static byte[] Jpeg(params byte[][] parts) => [0xFF, 0xD8, .. parts.SelectMany(part => part), .. Frame, .. Scan];

    [Fact]
    public void Jpeg_LosesExifXmpAndComments_KeepsColorProfileAndPicture()
    {
        var jpeg = Jpeg(Jfif, Segment(0xE1, Exif(1)), Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, 1]),
            Icc, Segment(0xFE, "made with a phone"u8.ToArray()));

        var stripped = ImageMetadata.Strip(jpeg)!;

        Assert.Equal([0xFF, 0xD8, .. Jfif, .. Icc, .. Frame, .. Scan], stripped);
        Assert.Null(ImageMetadata.ReadJpegOrientation(stripped));
    }

    [Fact]
    public void Jpeg_KeepsItsOrientation_InAMinimalExif()
    {
        var jpeg = Jpeg(Jfif, Segment(0xE1, Exif(6)));

        var stripped = ImageMetadata.Strip(jpeg)!;

        Assert.Equal((ushort)6, ImageMetadata.ReadJpegOrientation(stripped));
        Assert.False(ContainsGpsPointer(stripped));
        Assert.True(stripped.Length < jpeg.Length);
        Assert.Equal(Jfif, stripped.AsSpan(2, Jfif.Length).ToArray());
    }

    [Fact]
    public void Jpeg_LosesWhatIsAppendedAfterIt_AndTheIndexOfIt()
    {
        var jpeg = Jpeg(Jfif, Segment(0xE2, [.. "MPF\0"u8, 1, 2, 3]));
        byte[] appended = [.. jpeg, .. Jpeg(Segment(0xE1, Exif(1)))];

        Assert.Equal([0xFF, 0xD8, .. Jfif, .. Frame, .. Scan], ImageMetadata.Strip(appended));
    }

    [Fact]
    public void Jpeg_WithoutMetadata_IsTheSameArray()
    {
        var jpeg = Jpeg(Jfif, Icc);

        Assert.Same(jpeg, ImageMetadata.Strip(jpeg));
    }

    [Fact]
    public void Jpeg_ThatIsCut_IsNotStripped()
    {
        var jpeg = Jpeg(Jfif, Segment(0xE1, Exif(1)));

        Assert.Null(ImageMetadata.Strip(jpeg[..^3]));
    }

    [Fact]
    public void CopiedExif_IsUpright_AndTheColorProfileGoesAlong()
    {
        var jpeg = Jpeg(Jfif, Segment(0xE1, Exif(8)), Icc);

        var copied = ImageMetadata.CopyableJpegSegments(jpeg, withExif: true);
        var target = ImageMetadata.InsertJpegSegments(Jpeg(Jfif), copied);

        Assert.Equal(2, copied.Count);
        Assert.Equal((ushort)1, ImageMetadata.ReadJpegOrientation(target));
        Assert.True(ContainsGpsPointer(target));
        Assert.Single(ImageMetadata.CopyableJpegSegments(jpeg, withExif: false));
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    [Fact]
    public void Png_LosesExifAndTexts_KeepsTheRest()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var header = PngChunk("IHDR", new byte[13]);
        var profile = PngChunk("iCCP", [1, 2, 3]);
        var data = PngChunk("IDAT", [9, 9, 9]);
        var end = PngChunk("IEND", []);
        byte[] png = [.. signature, .. header, .. PngChunk("eXIf", Exif(1)), .. profile,
            .. PngChunk("tEXt", "Author\0me"u8.ToArray()), .. PngChunk("tIME", new byte[7]), .. data, .. end];

        var stripped = ImageMetadata.Strip(png);
        Assert.NotNull(stripped);
        Assert.Equal([.. signature, .. header, .. profile, .. data, .. end], stripped);
    }

    private static byte[] RiffChunk(string fourCc, byte[] data)
    {
        var chunk = new byte[8 + data.Length + (data.Length & 1)];
        Encoding.ASCII.GetBytes(fourCc).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)data.Length);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] WebP(params byte[][] chunks)
    {
        byte[] body = [.. "WEBP"u8, .. chunks.SelectMany(chunk => chunk)];
        var riff = new byte[8 + body.Length];
        "RIFF"u8.CopyTo(riff);
        BinaryPrimitives.WriteUInt32LittleEndian(riff.AsSpan(4), (uint)body.Length);
        body.CopyTo(riff, 8);
        return riff;
    }

    [Fact]
    public void WebP_LosesExifAndXmp_AndItsHeaderSaysSo()
    {
        var extended = RiffChunk("VP8X", [0x08 | 0x04 | 0x20, 0, 0, 0, 1, 0, 0, 1, 0, 0]);
        var picture = RiffChunk("VP8 ", [1, 2, 3]);
        var webp = WebP(extended, RiffChunk("ICCP", [5, 5]), picture, RiffChunk("EXIF", Exif(1)), RiffChunk("XMP ", [7]));

        var stripped = ImageMetadata.Strip(webp)!;

        var expectedExtended = (byte[])extended.Clone();
        expectedExtended[8] = 0x20;
        Assert.Equal(WebP(expectedExtended, RiffChunk("ICCP", [5, 5]), picture), stripped);
    }

    [Fact]
    public void OtherFormats_AreNotStripped()
    {
        Assert.Null(ImageMetadata.Strip("GIF89a......"u8.ToArray()));
    }
}

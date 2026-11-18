using System.Buffers.Binary;

namespace Client.Application.Chat;

/// <summary>
/// Takes the metadata out of JPEG, PNG and WebP files without decoding them, so the picture stays exactly as
/// it was: EXIF (where and when a photo was taken, the camera), XMP, IPTC, comments and text. Color profiles
/// stay. A JPEG keeps its orientation in a minimal EXIF, so it is not shown turned.
/// </summary>
public static class ImageMetadata
{
    private const byte Soi = 0xD8, Eoi = 0xD9, Sos = 0xDA, App0 = 0xE0, App1 = 0xE1, App2 = 0xE2, App14 = 0xEE;
    private const ushort OrientationTag = 0x0112;

    private static ReadOnlySpan<byte> ExifId => "Exif\0\0"u8;
    private static ReadOnlySpan<byte> MpfId => "MPF\0"u8;
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <returns>
    /// The content without its metadata; the same array when it has none; null when the format is not one of
    /// these or the file is damaged.
    /// </returns>
    public static byte[]? Strip(byte[] content)
    {
        if (content.AsSpan().StartsWith<byte>([0xFF, Soi]))
            return StripJpeg(content);
        if (content.AsSpan().StartsWith(PngSignature))
            return StripPng(content);
        if (content.Length >= 12 && content.AsSpan().StartsWith("RIFF"u8) && content.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            return StripWebP(content);
        return null;
    }

    // JPEG

    /// <summary>A segment of a JPEG before its image data: its marker and where it lies, the marker included.</summary>
    private readonly record struct Segment(byte Marker, int Start, int Length)
    {
        public int End => Start + Length;
        public ReadOnlySpan<byte> Payload(byte[] content) => content.AsSpan(Start + 4, Length - 4);
    }

    /// <summary>
    /// The segments before the first scan, and where the image ends: after its end marker, so whatever is
    /// appended (another picture, a video of a motion photo) is left out.
    /// </summary>
    private static (List<Segment> Header, int ScanStart, int End)? ParseJpeg(byte[] content)
    {
        var segments = new List<Segment>();
        var position = 2;
        int? scanStart = null;

        while (true)
        {
            if (position + 1 >= content.Length || content[position] != 0xFF)
                return null;

            var markerAt = position;
            while (position < content.Length && content[position] == 0xFF)
                position++;
            if (position >= content.Length)
                return null;

            var marker = content[position++];
            if (marker == Eoi)
                return scanStart is { } start ? (segments, start, position) : null;

            // Markers without a length
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
                continue;

            if (position + 2 > content.Length)
                return null;
            var length = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(position));
            if (length < 2 || position + length > content.Length)
                return null;
            position += length;

            if (scanStart == null)
            {
                if (marker == Sos)
                    scanStart = markerAt;
                else
                    segments.Add(new Segment(marker, markerAt, position - markerAt));
            }

            if (marker != Sos)
                continue;

            // The entropy-coded data ends at the first marker that is not a stuffed byte or a restart
            while (true)
            {
                if (position + 1 >= content.Length)
                    return null;
                if (content[position] == 0xFF && content[position + 1] != 0x00 && content[position + 1] is not (>= 0xD0 and <= 0xD7))
                    break;
                position++;
            }
        }
    }

    private static byte[]? StripJpeg(byte[] content)
    {
        if (ParseJpeg(content) is not var (header, scanStart, end))
            return null;

        var orientation = ReadOrientation(content, header);
        var kept = header.Where(segment => Keeps(content, segment)).ToList();
        if (kept.Count == header.Count && end == content.Length)
            return content;

        using var result = new MemoryStream(content.Length);
        result.Write([0xFF, Soi]);

        // After JFIF, which has to come first, or right at the start without it
        var pendingOrientation = orientation is { } turned and not 1 ? OrientationSegment(turned) : null;
        if (pendingOrientation != null && kept.All(segment => segment.Marker != App0))
        {
            result.Write(pendingOrientation);
            pendingOrientation = null;
        }

        foreach (var segment in kept)
        {
            result.Write(content, segment.Start, segment.Length);
            if (pendingOrientation != null && segment.Marker == App0)
            {
                result.Write(pendingOrientation);
                pendingOrientation = null;
            }
        }

        result.Write(content, scanStart, end - scanStart);
        return result.ToArray();
    }

    // JFIF, color profiles and the color transform of Adobe stay; the index of appended pictures goes with them
    private static bool Keeps(byte[] content, Segment segment) => segment.Marker switch
    {
        App0 or App14 => true,
        App2 => !segment.Payload(content).StartsWith(MpfId),
        >= App1 and <= 0xEF or 0xFE => false,
        _ => true
    };

    /// <summary>The orientation of a JPEG from its EXIF, 1 to 8; null when it has none.</summary>
    public static ushort? ReadJpegOrientation(byte[] content) =>
        content.AsSpan().StartsWith<byte>([0xFF, Soi]) && ParseJpeg(content) is var (header, _, _)
            ? ReadOrientation(content, header)
            : null;

    private static ushort? ReadOrientation(byte[] content, List<Segment> header)
    {
        foreach (var segment in header)
            if (segment.Marker == App1 && segment.Payload(content).StartsWith(ExifId)
                && FindOrientation(segment.Payload(content)[ExifId.Length..]) is var (value, _))
                return value;
        return null;
    }

    /// <summary>The value of the orientation tag in the first directory of a TIFF block, and where it is.</summary>
    private static (ushort Value, int Offset)? FindOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            return null;

        var bigEndian = tiff.StartsWith("MM"u8);
        if (!bigEndian && !tiff.StartsWith("II"u8))
            return null;

        ushort U16(ReadOnlySpan<byte> span) => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(span) : BinaryPrimitives.ReadUInt16LittleEndian(span);
        uint U32(ReadOnlySpan<byte> span) => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);

        var directory = U32(tiff[4..]);
        if (directory + 2 > (uint)tiff.Length)
            return null;

        var count = U16(tiff[(int)directory..]);
        for (var i = 0; i < count; i++)
        {
            var entry = (int)directory + 2 + i * 12;
            if (entry + 12 > tiff.Length)
                return null;
            // A SHORT, whose value lies in the first two bytes of the entry's value field
            if (U16(tiff[entry..]) == OrientationTag && U16(tiff[(entry + 2)..]) == 3)
            {
                var value = U16(tiff[(entry + 8)..]);
                return value is >= 1 and <= 8 ? (value, entry + 8) : null;
            }
        }

        return null;
    }

    /// <summary>An EXIF segment with nothing but the orientation.</summary>
    private static byte[] OrientationSegment(ushort orientation)
    {
        // Marker, length, "Exif\0\0", TIFF header, a directory of one entry, no next directory
        var segment = new byte[2 + 2 + 6 + 8 + 2 + 12 + 4];
        segment[0] = 0xFF;
        segment[1] = App1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(segment.Length - 2));
        ExifId.CopyTo(segment.AsSpan(4));
        var tiff = segment.AsSpan(10);
        "MM"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[2..], 42);
        BinaryPrimitives.WriteUInt32BigEndian(tiff[4..], 8);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[8..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[10..], OrientationTag);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[12..], 3);
        BinaryPrimitives.WriteUInt32BigEndian(tiff[14..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[18..], orientation);
        return segment;
    }

    /// <summary>
    /// The EXIF and color profile segments of a JPEG, to carry over to an encoded copy of it. The orientation
    /// in the EXIF is set to upright, as the copy is turned already.
    /// </summary>
    /// <param name="withExif">False to carry over only the color profile.</param>
    public static IReadOnlyList<byte[]> CopyableJpegSegments(byte[] content, bool withExif)
    {
        if (!content.AsSpan().StartsWith<byte>([0xFF, Soi]) || ParseJpeg(content) is not var (header, _, _))
            return [];

        var copied = new List<byte[]>();
        foreach (var segment in header)
        {
            var payload = segment.Payload(content);
            if (segment.Marker == App2 && payload.StartsWith("ICC_PROFILE\0"u8))
                copied.Add(content.AsSpan(segment.Start, segment.Length).ToArray());
            else if (withExif && segment.Marker == App1 && payload.StartsWith(ExifId))
            {
                var copy = content.AsSpan(segment.Start, segment.Length).ToArray();
                if (FindOrientation(payload[ExifId.Length..]) is var (_, offset))
                {
                    var tiff = copy.AsSpan(4 + ExifId.Length);
                    if (tiff.StartsWith("MM"u8))
                        BinaryPrimitives.WriteUInt16BigEndian(tiff[offset..], 1);
                    else
                        BinaryPrimitives.WriteUInt16LittleEndian(tiff[offset..], 1);
                }
                copied.Add(copy);
            }
        }

        return copied;
    }

    /// <summary>Puts segments into a JPEG after its start and its JFIF segment.</summary>
    public static byte[] InsertJpegSegments(byte[] jpeg, IReadOnlyList<byte[]> segments)
    {
        if (segments.Count == 0 || !jpeg.AsSpan().StartsWith<byte>([0xFF, Soi]))
            return jpeg;

        var at = 2;
        if (jpeg.Length > 6 && jpeg[2] == 0xFF && jpeg[3] == App0)
            at = 4 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(4));

        using var result = new MemoryStream(jpeg.Length + segments.Sum(segment => segment.Length));
        result.Write(jpeg, 0, at);
        foreach (var segment in segments)
            result.Write(segment);
        result.Write(jpeg, at, jpeg.Length - at);
        return result.ToArray();
    }

    // PNG

    private static readonly HashSet<string> PngMetadataChunks = ["eXIf", "tEXt", "zTXt", "iTXt", "tIME"];

    private static byte[]? StripPng(byte[] content)
    {
        using var result = new MemoryStream(content.Length);
        result.Write(PngSignature);
        var position = PngSignature.Length;
        var removed = false;

        while (true)
        {
            if (position + 12 > content.Length)
                return null;

            var length = BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(position));
            if (length > int.MaxValue - 12 || position + 12 + length > content.Length)
                return null;

            var type = System.Text.Encoding.ASCII.GetString(content, position + 4, 4);
            var size = 12 + (int)length;
            if (PngMetadataChunks.Contains(type))
                removed = true;
            else
                result.Write(content, position, size);
            position += size;

            if (type == "IEND")
                break;
        }

        return removed || position != content.Length ? result.ToArray() : content;
    }

    // WebP

    private const byte VP8XExifFlag = 0x08, VP8XXmpFlag = 0x04;

    private static byte[]? StripWebP(byte[] content)
    {
        var chunks = new List<(string FourCc, int Start, int Length)>();
        var position = 12;
        var end = (int)Math.Min(content.Length, 8L + BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(4)));

        while (position + 8 <= end)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(position + 4));
            var padded = 8L + size + (size & 1);
            if (position + padded > end)
            {
                // The last chunk may lack its padding byte
                if (position + 8L + size != end)
                    return null;
                padded = 8L + size;
            }

            chunks.Add((System.Text.Encoding.ASCII.GetString(content, position, 4), position, (int)padded));
            position += (int)padded;
        }

        if (chunks.Count == 0)
            return null;
        if (chunks.All(chunk => chunk.FourCc is not ("EXIF" or "XMP ")))
            return content;

        using var result = new MemoryStream(content.Length);
        result.Write(content, 0, 12);
        foreach (var (fourCc, start, length) in chunks)
        {
            if (fourCc is "EXIF" or "XMP ")
                continue;

            var chunk = content.AsSpan(start, length).ToArray();
            if (fourCc == "VP8X" && chunk.Length > 8)
                chunk[8] &= unchecked((byte)~(VP8XExifFlag | VP8XXmpFlag));
            result.Write(chunk);
        }

        var stripped = result.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(stripped.AsSpan(4), (uint)(stripped.Length - 8));
        return stripped;
    }
}

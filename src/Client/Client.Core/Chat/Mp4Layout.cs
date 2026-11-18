using System.Buffers.Binary;

namespace Client.Core.Chat;

/// <summary>
/// The order of the top-level boxes of an MP4 or QuickTime file, read from their headers alone.
/// </summary>
/// <param name="IsMp4">The file is made of such boxes and has the index of its media, "moov".</param>
/// <param name="IsFastStart">"moov" comes before the media, "mdat", so the file plays while it downloads.</param>
public sealed record Mp4Layout(bool IsMp4, bool IsFastStart)
{
    private static readonly Mp4Layout NotMp4 = new(false, false);

    // Top-level boxes of the files cameras and phones write
    private static readonly HashSet<string> TopLevel = ["ftyp", "moov", "mdat", "free", "skip", "wide", "uuid", "meta", "pdin", "moof", "mfra", "sidx", "styp"];

    public static Mp4Layout Read(Stream stream)
    {
        Span<byte> header = stackalloc byte[16];
        long position = 0;
        var length = stream.Length;
        var seenMedia = false;

        while (position + 8 <= length)
        {
            stream.Position = position;
            stream.ReadExactly(header[..8]);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = System.Text.Encoding.ASCII.GetString(header.Slice(4, 4));
            if (!TopLevel.Contains(type))
                return NotMp4;

            switch (size)
            {
                case 1:
                    if (position + 16 > length)
                        return NotMp4;
                    stream.ReadExactly(header.Slice(8, 8));
                    size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
                    break;
                case 0:
                    size = length - position;
                    break;
            }
            if (size < 8)
                return NotMp4;

            switch (type)
            {
                case "moov":
                    return new Mp4Layout(true, !seenMedia);
                case "mdat" or "moof":
                    seenMedia = true;
                    break;
            }
            position += size;
        }
        return NotMp4;
    }

    public static Mp4Layout Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }
}

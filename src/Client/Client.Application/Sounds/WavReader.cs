using System.Buffers.Binary;

namespace Client.Application.Sounds;

/// <summary>
/// Reads the only format the sounds are kept in: a RIFF file of uncompressed 16-bit samples, mono or stereo.
/// </summary>
public static class WavReader
{
    // Far above any interface sound; a pack is a file of the user and is not trusted
    public const int MaxFileSize = 8 * 1024 * 1024;

    private const int PcmFormat = 1;
    private const int ExtensibleFormat = 0xFFFE;

    public static SoundClip Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxFileSize)
                throw new InvalidDataException("The sound file is too large");

            buffer.Write(chunk, 0, read);
        }

        return Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    public static SoundClip Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Not a WAV file");

        int channels = 0;
        int sampleRate = 0;
        var position = 12;

        while (position + 8 <= data.Length)
        {
            var id = data.Slice(position, 4);
            var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position + 4, 4));
            position += 8;

            // Writers that stream the file leave the size of the last chunk wrong
            var size = (int)Math.Min(declaredSize, (uint)(data.Length - position));
            var body = data.Slice(position, size);

            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16)
                    throw new InvalidDataException("The format chunk is too short");

                var format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                // The extensible header keeps the real format in the first two bytes of its sub-format
                if (format == ExtensibleFormat && size >= 26)
                    format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);

                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);

                if (format != PcmFormat || bitsPerSample != 16)
                    throw new InvalidDataException("Only uncompressed 16-bit sound is supported");
                if (channels is not (1 or 2))
                    throw new InvalidDataException("Only mono and stereo sound is supported");
                if (sampleRate is < 8000 or > 192000)
                    throw new InvalidDataException("Unsupported sample rate");
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (channels == 0)
                    throw new InvalidDataException("The samples come before their format");

                // Whole frames only
                var frameSize = channels * 2;
                return new SoundClip(body[..(size - size % frameSize)].ToArray(), channels, sampleRate);
            }

            // Chunks are padded to an even size
            position += size + (size & 1);
        }

        throw new InvalidDataException("The WAV file has no samples");
    }
}

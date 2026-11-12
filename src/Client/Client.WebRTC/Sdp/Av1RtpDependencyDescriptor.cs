using System.Buffers.Binary;

namespace Client.WebRTC.Sdp;

/// <summary>Adds single-layer AV1 dependency descriptors to packets emitted by a payloader.</summary>
public sealed class Av1RtpDependencyDescriptor
{
    // The extension header and the longest descriptor, including alignment padding.
    public const int MaxOverhead = 16;
    private uint? _timestamp;
    private ushort _frameNumber;
    private bool _keyframe;

    public byte[] Rewrite(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 12 || packet[0] >> 6 != 2)
            throw new ArgumentException("Invalid RTP header", nameof(packet));

        var headerSize = 12 + 4 * (packet[0] & 15);
        var extensionSize = 0;
        var payloadOffset = headerSize;
        if ((packet[0] & 0x10) != 0)
        {
            if (packet.Length < headerSize + 4 || BinaryPrimitives.ReadUInt16BigEndian(packet[headerSize..]) != 0xBEDE)
                throw new ArgumentException("Expected one-byte RTP extensions", nameof(packet));
            extensionSize = 4 * BinaryPrimitives.ReadUInt16BigEndian(packet[(headerSize + 2)..]);
            payloadOffset += 4 + extensionSize;
        }
        if (packet.Length <= payloadOffset)
            throw new ArgumentException("Missing AV1 payload", nameof(packet));

        var timestamp = BinaryPrimitives.ReadUInt32BigEndian(packet[4..]);
        var first = _timestamp != timestamp;
        if (first)
        {
            _timestamp = timestamp;
            _frameNumber++;
            // The AV1 aggregation header's N bit starts a coded video sequence.
            _keyframe = (packet[payloadOffset] & 8) != 0;
        }
        var descriptor = _keyframe && first ? KeyframeDescriptor() : new byte[3];
        descriptor[0] = (byte)((first ? 0x80 : 0) | ((packet[1] & 0x80) != 0 ? 0x40 : 0) | (_keyframe ? 0 : 1));
        BinaryPrimitives.WriteUInt16BigEndian(descriptor.AsSpan(1), _frameNumber);

        var newExtensionSize = (extensionSize + 1 + descriptor.Length + 3) & ~3;
        var result = new byte[headerSize + 4 + newExtensionSize + packet.Length - payloadOffset];
        packet[..headerSize].CopyTo(result);
        result[0] |= 0x10;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(headerSize), 0xBEDE);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(headerSize + 2), (ushort)(newExtensionSize / 4));
        if (extensionSize > 0)
            packet.Slice(headerSize + 4, extensionSize).CopyTo(result.AsSpan(headerSize + 4));
        var position = headerSize + 4 + extensionSize;
        result[position] = (byte)((SdpHeaderExtension.Av1DependencyDescriptorId << 4) | (descriptor.Length - 1));
        descriptor.CopyTo(result, position + 1);
        packet[payloadOffset..].CopyTo(result.AsSpan(headerSize + 4 + newExtensionSize));
        return result;
    }

    private static byte[] KeyframeDescriptor()
    {
        // Two L1T1 templates: an independent keyframe, and a delta depending on its predecessor.
        // Field order follows the AV1 RTP dependency descriptor specification, A.8.
        var data = new byte[9];
        var position = 24;
        Write(0b10000, 5); // Attached structure; no per-frame overrides.
        Write(0, 6); // Template id offset.
        Write(0, 5); // One decode target.
        Write(0, 2); Write(3, 2); // Same layer, then end of templates.
        Write(2, 2); Write(2, 2); // Switch indications.
        Write(0, 1); // Keyframe has no dependencies.
        Write(16, 5); Write(0, 1); // Delta frame difference 1, then end.
        Write(1, 1); // One chain; its sole target implicitly maps to chain zero.
        Write(0, 4); Write(1, 4); // Key and delta chain differences.
        Write(0, 1); // No resolutions.
        return data;

        void Write(int value, int bits)
        {
            for (var bit = bits - 1; bit >= 0; bit--, position++)
                data[position / 8] |= (byte)(((value >> bit) & 1) << (7 - position % 8));
        }
    }
}

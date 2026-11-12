using System.Buffers.Binary;
using Client.WebRTC.Sdp;
using Xunit;

namespace Client.Application.Tests;

public class Av1RtpDependencyDescriptorTests
{
    [Fact]
    public void KeyframeCarriesTheL1T1StructureRequiredByTheSfu()
    {
        var packet = Packet(1, 0x08, true);
        var rewritten = new Av1RtpDependencyDescriptor().Rewrite(packet);
        // This fixture is also passed through a real mediasoup worker by av1-forwarding.test.ts.
        Assert.Equal(Convert.FromHexString("90E8002A00000001000004D2BEDE000368C0000180003A4101000000081200"), rewritten);
        Assert.True(rewritten.Length - packet.Length <= Av1RtpDependencyDescriptor.MaxOverhead);
    }

    [Fact]
    public void FragmentsShareAFrameNumberAndOnlyTheFirstCarriesTheStructure()
    {
        var writer = new Av1RtpDependencyDescriptor();
        var first = writer.Rewrite(Packet(100, 0x08, false));
        var last = writer.Rewrite(Packet(100, 0x80, true));
        var delta = writer.Rewrite(Packet(200, 0, true));

        Assert.Equal(0x80, first[17]);
        Assert.Equal(new byte[] { 0x62, 0x40, 0, 1 }, last[16..20]);
        Assert.Equal(new byte[] { 0x62, 0xC1, 0, 2 }, delta[16..20]);
        Assert.Equal(new byte[] { 0x80, 0x12, 0 }, last[^3..]);
    }

    [Fact]
    public void ExistingCsrcAndHeaderExtensionsArePreserved()
    {
        var packet = Convert.FromHexString("91E8002A00000001000004D211223344BEDE000151123400081200");
        var rewritten = new Av1RtpDependencyDescriptor().Rewrite(packet);
        Assert.Equal(packet[12..16], rewritten[12..16]);
        Assert.Equal(packet[20..24], rewritten[20..24]);
        Assert.Equal(packet[^3..], rewritten[^3..]);
        Assert.Equal(4, BinaryPrimitives.ReadUInt16BigEndian(rewritten.AsSpan(18)));
    }

    [Fact]
    public void TruncatedPacketIsRejected() =>
        Assert.Throws<ArgumentException>(() => new Av1RtpDependencyDescriptor().Rewrite(new byte[8]));

    private static byte[] Packet(uint timestamp, byte aggregationHeader, bool last)
    {
        var packet = Convert.FromHexString("8068002A00000001000004D2001200");
        packet[1] |= (byte)(last ? 0x80 : 0);
        packet[12] = aggregationHeader;
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), timestamp);
        return packet;
    }
}

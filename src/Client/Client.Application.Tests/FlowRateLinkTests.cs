using Client.Application.Links;
using Xunit;

namespace Client.Application.Tests;

public sealed class FlowRateLinkTests
{
    private const string Fingerprint = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";

    [Fact]
    public void ConnectLinkReadsBackWhatWasWritten()
    {
        var link = FlowRateLinks.Connect("chat.example.com", 5000, Fingerprint);

        Assert.Equal($"flowrate://connect?host=chat.example.com&port=5000&cert={Fingerprint}", link);
        Assert.Equal(new ConnectLink("chat.example.com", 5000, Fingerprint), FlowRateLinks.Parse(link));
    }

    [Fact]
    public void ConnectLinkLeavesOutWhatIsNotKnown()
    {
        var link = FlowRateLinks.Connect("chat.example.com", null, null);

        Assert.Equal("flowrate://connect?host=chat.example.com", link);
        Assert.Equal(new ConnectLink("chat.example.com", null, null), FlowRateLinks.Parse(link));
    }

    [Fact]
    public void Ipv6HostIsEscapedAndReadBack()
    {
        var link = FlowRateLinks.Connect("[2001:db8::1]", 26666, null);

        Assert.Equal(new ConnectLink("[2001:db8::1]", 26666, null), FlowRateLinks.Parse(link));
    }

    [Fact]
    public void LoginLinkCarriesTheCode()
    {
        var link = FlowRateLinks.Login("chat.example.com", null, null, "ABCD-EFGH-JKMN");

        Assert.Equal(new LoginLink("chat.example.com", null, null, "ABCD-EFGH-JKMN"), FlowRateLinks.Parse(link));
    }

    [Fact]
    public void DeviceLinkCarriesTheServerOfTheNewDevice()
    {
        var link = FlowRateLinks.DeviceLink("ABCD-EFGH", "chat.example.com", 5000);

        Assert.Equal(new DeviceLinkApproval("ABCD-EFGH", "chat.example.com", 5000), FlowRateLinks.Parse(link));
        Assert.Equal("ABCD-EFGH", FlowRateLinks.DeviceLinkCode(link));
    }

    [Theory]
    [InlineData("flowrate-link:ABCD-EFGH")]
    [InlineData("  FLOWRATE-LINK:ABCD-EFGH ")]
    public void CodeOfEarlierVersionsIsADeviceLink(string text)
    {
        Assert.Equal(new DeviceLinkApproval("ABCD-EFGH", null, null), FlowRateLinks.Parse(text));
        Assert.Equal("ABCD-EFGH", FlowRateLinks.DeviceLinkCode(text));
    }

    [Fact]
    public void PlainCodeStaysAsTyped()
    {
        Assert.Null(FlowRateLinks.Parse("ABCD-EFGH"));
        Assert.Equal("ABCD-EFGH", FlowRateLinks.DeviceLinkCode(" ABCD-EFGH "));
    }

    [Theory]
    [InlineData("flowrate:connect?host=chat.example.com")]
    [InlineData("FlowRate://Connect/?host=chat.example.com")]
    [InlineData("flowrate:///connect?host=chat.example.com#ignored")]
    public void SpellingsTheSystemsUseAreRead(string text)
    {
        Assert.Equal(new ConnectLink("chat.example.com", null, null), FlowRateLinks.Parse(text));
    }

    [Theory]
    [InlineData("sha256:" + Fingerprint)]
    [InlineData("ab:12:cd:34:ef:56:ab:12:cd:34:ef:56:ab:12:cd:34:ef:56:ab:12:cd:34:ef:56:ab:12:cd:34:ef:56:ab:12")]
    public void FingerprintIsReadInItsCommonSpellings(string fingerprint)
    {
        var link = FlowRateLinks.Parse("flowrate://connect?host=h&cert=" + Uri.EscapeDataString(fingerprint));

        Assert.Equal(Fingerprint, Assert.IsType<ConnectLink>(link).Fingerprint);
    }

    [Fact]
    public void BrokenFingerprintIsDroppedSoTheCertificateIsAskedAbout()
    {
        var link = FlowRateLinks.Parse("flowrate://connect?host=h&cert=nothex");

        Assert.Null(Assert.IsType<ConnectLink>(link).Fingerprint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("flowrate://connect")]
    [InlineData("flowrate://connect?host=")]
    [InlineData("flowrate://connect?host=a b")]
    [InlineData("flowrate://connect?host=user@host")]
    [InlineData("flowrate://connect?host=h&port=0")]
    [InlineData("flowrate://connect?host=h&port=70000")]
    [InlineData("flowrate://connect?host=h&port=x")]
    [InlineData("flowrate://connect?host=a&host=b")]
    [InlineData("flowrate://login?host=h")]
    [InlineData("flowrate://login?host=h&code=<script>")]
    [InlineData("flowrate://link")]
    [InlineData("flowrate://unknown?host=h")]
    [InlineData("flowrate-link:")]
    public void IncompleteOrUnknownLinksAreNotRead(string text)
    {
        Assert.Null(FlowRateLinks.Parse(text));
    }

    [Fact]
    public void FingerprintsAreComparedWhateverTheirSpelling()
    {
        Assert.True(FlowRateLinks.IsSameFingerprint(Fingerprint.ToLowerInvariant(), "sha256:" + Fingerprint));
        Assert.False(FlowRateLinks.IsSameFingerprint(Fingerprint, null));
        Assert.False(FlowRateLinks.IsSameFingerprint(Fingerprint, new string('0', 64)));
    }

    [Fact]
    public void OnlyLinksOfTheApplicationLookLikeLinks()
    {
        Assert.True(FlowRateLinks.LooksLikeLink("flowrate://connect"));
        Assert.True(FlowRateLinks.LooksLikeLink("flowrate-link:X"));
        Assert.False(FlowRateLinks.LooksLikeLink("ABCD-EFGH"));
        Assert.False(FlowRateLinks.LooksLikeLink(null));
    }

    [Fact]
    public void SettingsLinkNamesItsTab()
    {
        Assert.Equal(new SettingsLink("about"), FlowRateLinks.Parse(FlowRateLinks.Settings("about")));
        Assert.Equal(new SettingsLink(null), FlowRateLinks.Parse("flowrate://settings"));
        Assert.Equal(new SettingsLink(null), FlowRateLinks.Parse("flowrate:settings?tab="));
    }
}

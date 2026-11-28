using Client.Application.Links;
using Client.Core.Chat;
using Client.ViewModels.Sessions.Chat;
using Xunit;

namespace Client.Application.Tests;

public class ChatAppLinksTests
{
    private const string Connect = "flowrate://connect?host=example.org&port=4433";

    [Fact]
    public void FindsLinkWithoutTrailingPunctuation()
    {
        string text = $"join ({Connect}).";

        var link = Assert.Single(ChatAppLinks.Find(text));

        Assert.Equal(text.IndexOf("flowrate", StringComparison.Ordinal), link.Start);
        Assert.Equal(Connect, text.Substring(link.Start, link.Length));
        Assert.Equal(Connect, link.Uri.OriginalString);
    }

    [Fact]
    public void LeavesDeviceApprovalsOutOfClickableLinks()
    {
        string text = "flowrate://link?code=ABCD-1234";

        Assert.Empty(ChatAppLinks.Find(text));
        Assert.Single(ChatAppLinks.FindAll(text));
    }

    [Theory]
    [InlineData("xflowrate://connect?host=a")]
    [InlineData("flowrate://")]
    [InlineData("no links here https://example.org")]
    public void IgnoresWhatIsNotALink(string text) => Assert.Empty(ChatAppLinks.Find(text));

    [Fact]
    public void MarkdownKeepsBothKindsOfLinksInOrder()
    {
        var result = ChatMarkdown.Parse(new($"see https://example.org and {Connect}", []));

        Assert.Equal(2, result.Links.Count);
        Assert.Equal("https", result.Links[0].Uri.Scheme);
        Assert.Equal("flowrate", result.Links[1].Uri.Scheme);
    }

    [Fact]
    public void MarkdownLeavesLinkInCodeAsText()
    {
        var result = ChatMarkdown.Parse(new($"`{Connect}`", []));

        Assert.Empty(result.Links);
    }

    [Fact]
    public void CardTakesFirstLinkTheApplicationUnderstands()
    {
        var card = ChatAppLinkVM.FromText($"flowrate://nothing?x=1 then {Connect}");

        Assert.NotNull(card);
        Assert.IsType<ConnectLink>(card.Link);
        Assert.Equal("example.org:4433", card.Server);
        Assert.True(card.CanOpen);
    }

    [Fact]
    public void DeviceApprovalCardWarnsWithoutButton()
    {
        var card = ChatAppLinkVM.FromText("flowrate://link?code=ABCD-1234&host=example.org");

        Assert.NotNull(card);
        Assert.True(card.IsWarning);
        Assert.False(card.CanOpen);
        Assert.Equal("ABCD-1234", card.Code);
        Assert.Equal("example.org", card.Server);
    }

    [Fact]
    public void ComposerCardHasNoButton()
    {
        var card = ChatAppLinkVM.FromText(Connect, canOpen: false);

        Assert.NotNull(card);
        Assert.False(card.CanOpen);
    }
}

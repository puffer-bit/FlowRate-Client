using Client.Core.Chat;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Mentions;
using Xunit;

namespace Client.Application.Tests;

public class ChatMentionsTests
{
    [Fact]
    public void Find_ReadsUserEveryoneAndHereTokens()
    {
        var tokens = ChatMentions.Find("hi <@12> and <@everyone>, <@here>!");

        Assert.Equal(3, tokens.Count);
        Assert.Equal((ChatMentionKind.User, 12, 3, 5), (tokens[0].Kind, tokens[0].UserId, tokens[0].Start, tokens[0].Length));
        Assert.Equal(ChatMentionKind.Everyone, tokens[1].Kind);
        Assert.Equal(ChatMentionKind.Here, tokens[2].Kind);
    }

    [Theory]
    [InlineData("<@>")]
    [InlineData("<@0>")]
    [InlineData("<@012>")]
    [InlineData("<@12")]
    [InlineData("<@-3>")]
    [InlineData("<@99999999999>")]
    [InlineData("<@all>")]
    public void Find_IgnoresMalformedTokens(string text) => Assert.Empty(ChatMentions.Find(text));

    [Fact]
    public void UserIdsOf_ListsEachUserOnce()
    {
        var ids = ChatMentions.UserIdsOf(ChatMentions.Find("<@5> <@7> <@5> <@everyone>"));

        Assert.Equal([5, 7], ids);
    }

    [Fact]
    public void Body_WritesNamesAndMarksTheCurrentUser()
    {
        var body = ChatMessageBody.Create("hey <@5>, <@7> and <@here>", id => id == 5 ? "Ann" : null, 5, "Unknown");

        Assert.Equal("hey @Ann, @Unknown and @here", body.Text);
        Assert.Equal(3, body.Mentions.Count);
        Assert.Equal("@Ann", body.Text.Substring(body.Mentions[0].Start, body.Mentions[0].Length));
        Assert.True(body.Mentions[0].IsSelf);
        Assert.Equal("@Unknown", body.Text.Substring(body.Mentions[1].Start, body.Mentions[1].Length));
        Assert.False(body.Mentions[1].IsSelf);
        Assert.True(body.Mentions[2].IsSelf);
    }

    [Fact]
    public void Body_WithoutTokensKeepsTheText()
    {
        var body = ChatMessageBody.Create("a@b.c <b>", _ => "x", 1, "?");

        Assert.Equal("a@b.c <b>", body.Text);
        Assert.Empty(body.Mentions);
    }
}

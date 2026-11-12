using Client.Application.Coordinator;
using Client.Application.Tests.Fakes;
using Client.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Shared.Validation;
using Xunit;

namespace Client.Application.Tests;

public sealed class CoordinatorSessionCreationTests
{
    [Fact]
    public async Task UnlimitedChatIsSentToGateway()
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        var chat = new NewSessionModel("General", 1, Limits.SessionCapacityUnlimited,
            new NewChatSessionOptions(Description: "Team chat"));

        var result = await session.CreateSession(chat);

        Assert.True(result.IsSuccess);
        Assert.Equal(chat, Assert.Single(fake.CreatedSessions));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public async Task HybridCapacityIsCheckedBeforeSending(int capacity, bool accepted)
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        var hybrid = new NewSessionModel("Voice", 1, capacity, new NewHybridSessionOptions(WebRTCEngine.GStreamer));

        var result = await session.CreateSession(hybrid);

        Assert.Equal(accepted, result.IsSuccess);
        if (accepted)
            Assert.Equal(hybrid, Assert.Single(fake.CreatedSessions));
        else
        {
            Assert.Equal(ErrorCode.InvalidValue, result.Error);
            Assert.Empty(fake.CreatedSessions);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Bad\nname")]
    public async Task InvalidChatNameIsNotSent(string name)
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);

        var result = await session.CreateSession(new NewSessionModel(name, 1, Limits.SessionCapacityUnlimited,
            new NewChatSessionOptions()));

        Assert.Equal(ErrorCode.InvalidValue, result.Error);
        Assert.Empty(fake.CreatedSessions);
    }
}

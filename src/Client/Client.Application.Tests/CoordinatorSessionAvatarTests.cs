using Client.Application.Coordinator;
using Client.Application.Tests.Fakes;
using Client.Core.Chat;
using Client.Core.Users;
using Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Validation;
using Xunit;

namespace Client.Application.Tests;

public class CoordinatorSessionAvatarTests
{
    private readonly FakeGateway _gateway;
    private readonly CoordinatorSession _session;

    public CoordinatorSessionAvatarTests()
    {
        (var gateway, _gateway) = FakeGateway.Create();
        _session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task SetAvatar_UploadsThePictureAndPutsItOnTheProfile()
    {
        await _session.Connect("server", 1, null, null);
        byte[] image = [1, 2, 3];

        var (result, user) = await _session.SetAvatar(image, "image/webp", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var (uploadId, (upload, content)) = Assert.Single(_gateway.Uploads);
        Assert.Equal(MediaUploadKind.Avatar, upload.Kind);
        Assert.Null(upload.ChatId);
        Assert.Equal("image/webp", upload.MimeType);
        Assert.Equal(image, content);
        Assert.Equal((uploadId, null), Assert.Single(_gateway.AvatarRequests));
        Assert.Equal(uploadId, AvatarAddress.IdOf(user!.AvatarUrl));
    }

    [Fact]
    public async Task SetAvatar_WhenTheUploadFails_AsksForNothing()
    {
        await _session.Connect("server", 1, null, null);
        _gateway.UploadResult = MediaTransferResult.ConnectionFailed;

        var (result, user) = await _session.SetAvatar([1], "image/webp", TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCode.UploadNotFound, result.Error);
        Assert.Null(user);
        Assert.Empty(_gateway.AvatarRequests);
    }

    [Fact]
    public async Task SetAvatar_LargerThanTheLimit_IsNotSent()
    {
        await _session.Connect("server", 1, null, null);

        var (result, _) = await _session.SetAvatar(new byte[Limits.AvatarMaxSize + 1], "image/webp",
            TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCode.UploadNotFound, result.Error);
        Assert.Empty(_gateway.Uploads);
    }

    [Fact]
    public async Task SetAvatar_TheServerRefuses_ReportsItsAnswer()
    {
        await _session.Connect("server", 1, null, null);
        _gateway.AvatarError = ErrorCode.UploadNotFound;

        var (result, user) = await _session.SetAvatar([1], "image/webp", TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCode.UploadNotFound, result.Error);
        Assert.Null(user);
    }

    [Fact]
    public async Task ResetAvatar_NamesTheUserAndNoUpload()
    {
        await _session.Connect("server", 1, null, null);

        var result = await _session.ResetAvatar(7);

        Assert.True(result.IsSuccess);
        Assert.Equal((null, 7), Assert.Single(_gateway.AvatarRequests));
        Assert.Empty(_gateway.Uploads);
    }

    [Theory]
    [InlineData("/media/avatars/0a1b", "0a1b")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("/media/avatars/", null)]
    [InlineData("/media/avatars/../images/1", null)]
    [InlineData("/media/avatars/0a1b?x=1", null)]
    [InlineData("https://elsewhere.example/avatar.png", null)]
    [InlineData("/media/images/1", null)]
    public void AvatarAddress_GivesTheIdOfAnAvatarOfTheCoordinatorOnly(string? avatarUrl, string? expected)
    {
        Assert.Equal(expected, AvatarAddress.IdOf(avatarUrl));
    }
}

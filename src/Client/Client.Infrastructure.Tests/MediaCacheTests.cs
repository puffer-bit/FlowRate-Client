using Client.Infrastructure.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Infrastructure.Tests;

public sealed class MediaCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"media-cache-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    private MediaCache Cache(long sizeLimit = MediaCache.DefaultSizeLimit)
    {
        var cache = new MediaCache(_directory, NullLogger<MediaCache>.Instance, sizeLimit);

        // A new cache trims in the background after its first write; done here, it cannot race the test
        cache.Trim();
        return cache;
    }

    private static Task<string?> Store(MediaCache cache, int imageId, int size, bool preview = true,
        string coordinator = "coordinator") =>
        cache.StoreAsync(coordinator, imageId, preview, async stream =>
        {
            await stream.WriteAsync(new byte[size]);
            return true;
        });

    [Fact]
    public async Task StoredImage_IsFoundByItsCoordinatorIdAndVariant()
    {
        var cache = Cache();

        var path = await Store(cache, 5, 100);

        Assert.NotNull(path);
        Assert.Equal(100, new FileInfo(path).Length);
        Assert.Equal(path, cache.Find("coordinator", 5, true));
        Assert.Null(cache.Find("coordinator", 5, false));
        Assert.Null(cache.Find("coordinator", 6, true));
        Assert.Null(cache.Find("other", 5, true));
    }

    [Fact]
    public async Task StoredAvatar_IsFoundByItsCoordinatorAndId()
    {
        var cache = Cache();

        var path = await cache.StoreAvatarAsync("coordinator", "0a1b", async stream =>
        {
            await stream.WriteAsync(new byte[100]);
            return true;
        });

        Assert.NotNull(path);
        Assert.Equal(path, cache.FindAvatar("coordinator", "0a1b"));
        Assert.Null(cache.FindAvatar("coordinator", "0a1c"));
        Assert.Null(cache.FindAvatar("other", "0a1b"));
        Assert.Equal(100, await cache.GetSizeAsync());
    }

    [Fact]
    public async Task Attachment_KeepsItsNameAndIsSeparateFromOtherMediaAndCoordinators()
    {
        var cache = Cache();
        byte[] content = [0, 1, 127, 255];
        var attachment = await cache.StoreFileAsync("coordinator", 5, "report.pdf", async stream =>
        {
            await stream.WriteAsync(content);
            return true;
        });
        var image = await Store(cache, 5, 100, preview: false);
        var other = await cache.StoreFileAsync("other", 5, "report.pdf", _ => Task.FromResult(true));

        Assert.NotNull(attachment);
        Assert.Equal("report.pdf", Path.GetFileName(attachment));
        Assert.Equal(content, await File.ReadAllBytesAsync(attachment, TestContext.Current.CancellationToken));
        Assert.Equal(attachment, cache.FindFile("coordinator", 5, "report.pdf"));
        Assert.NotEqual(image, attachment);
        Assert.NotEqual(other, attachment);
        Assert.Null(cache.FindFile("coordinator", 6, "report.pdf"));
    }

    [Theory]
    [InlineData("../report.pdf", "report.pdf")]
    [InlineData("folder\\report.pdf", "report.pdf")]
    [InlineData("..", "attachment")]
    [InlineData("", "attachment")]
    public async Task AttachmentName_StaysInsideItsCacheDirectory(string name, string expected)
    {
        var cache = Cache();
        var path = await cache.StoreFileAsync("coordinator", 5, name, _ => Task.FromResult(true));

        Assert.NotNull(path);
        Assert.Equal(Path.Combine(_directory, "coordinator", "5.file", expected), path);
    }

    [Fact]
    public async Task FailedAttachment_IsNotCachedAndLeavesNoPartialDownload()
    {
        var cache = Cache();
        var path = await cache.StoreFileAsync("coordinator", 5, "report.pdf", async stream =>
        {
            await stream.WriteAsync(new byte[10]);
            return false;
        });

        Assert.Null(path);
        Assert.Null(cache.FindFile("coordinator", 5, "report.pdf"));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AttachmentEndingInPart_IsCountedAndClearedAsACompletedFile()
    {
        var cache = Cache();
        var path = await cache.StoreFileAsync("coordinator", 5, "download.part", async stream =>
        {
            await stream.WriteAsync(new byte[10]);
            return true;
        });

        Assert.NotNull(path);
        Assert.Equal(10, await cache.GetSizeAsync());
        await cache.ClearAsync();
        Assert.False(File.Exists(path));
        Assert.Null(cache.FindFile("coordinator", 5, "download.part"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../0a1b")]
    [InlineData("0a/1b")]
    [InlineData("0a1b.avatar")]
    public async Task AvatarId_ThatIsNotAPlainName_IsRefused(string avatarId)
    {
        var cache = Cache();

        var path = await cache.StoreAvatarAsync("coordinator", avatarId, _ => Task.FromResult(true));

        Assert.Null(path);
        Assert.Null(cache.FindAvatar("coordinator", avatarId));
    }

    [Fact]
    public async Task ImageThatWasNotWrittenWhole_IsNotKept()
    {
        var cache = Cache();

        var refused = await cache.StoreAsync("coordinator", 5, true, async stream =>
        {
            await stream.WriteAsync(new byte[10]);
            return false;
        });
        var failed = await cache.StoreAsync("coordinator", 6, true, _ => throw new IOException("disk"));

        Assert.Null(refused);
        Assert.Null(failed);
        Assert.Null(cache.Find("coordinator", 5, true));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Trim_RemovesTheImagesUsedLongestAgo()
    {
        var cache = Cache(sizeLimit: 250);
        var first = (await Store(cache, 1, 100))!;
        var second = (await Store(cache, 2, 100))!;
        var third = (await Store(cache, 3, 100))!;
        File.SetLastAccessTimeUtc(first, DateTime.UtcNow.AddHours(-1));
        File.SetLastAccessTimeUtc(second, DateTime.UtcNow.AddHours(-3));
        File.SetLastAccessTimeUtc(third, DateTime.UtcNow.AddHours(-2));

        cache.Trim();

        Assert.NotNull(cache.Find("coordinator", 1, true));
        Assert.Null(cache.Find("coordinator", 2, true));
        Assert.NotNull(cache.Find("coordinator", 3, true));
    }

    [Fact]
    public async Task Size_IsWhatTheKeptImagesTake_AndClearingRemovesThem()
    {
        var cache = Cache();
        Assert.Equal(0, await cache.GetSizeAsync());

        await Store(cache, 1, 100);
        await Store(cache, 1, 250, preview: false);
        await Store(cache, 2, 50, coordinator: "other");
        Assert.Equal(400, await cache.GetSizeAsync());

        await cache.ClearAsync();

        Assert.Equal(0, await cache.GetSizeAsync());
        Assert.Null(cache.Find("coordinator", 1, true));
    }

    [Fact]
    public async Task LoweringTheLimit_RemovesWhatDoesNotFitAnymore()
    {
        var cache = Cache();
        var old = (await Store(cache, 1, 100))!;
        await Store(cache, 2, 100);
        File.SetLastAccessTimeUtc(old, DateTime.UtcNow.AddHours(-1));

        cache.SizeLimit = 150;

        for (var i = 0; i < 100 && await cache.GetSizeAsync() > 150; i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.Equal(100, await cache.GetSizeAsync());
        Assert.Null(cache.Find("coordinator", 1, true));
        Assert.NotNull(cache.Find("coordinator", 2, true));
    }

    [Fact]
    public async Task Trim_RemovesWhatAnInterruptedWriteLeft()
    {
        var cache = Cache();
        var kept = (await Store(cache, 1, 100))!;
        var abandoned = kept + ".0123.part";
        File.WriteAllBytes(abandoned, new byte[10]);
        File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-2));

        cache.Trim();

        Assert.False(File.Exists(abandoned));
        Assert.True(File.Exists(kept));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/b")]
    [InlineData("")]
    public async Task CoordinatorId_ThatIsNotAPlainName_IsRefused(string coordinator)
    {
        var cache = Cache();

        Assert.Null(await Store(cache, 1, 10, coordinator: coordinator));
        Assert.Null(cache.Find(coordinator, 1, true));
    }
}

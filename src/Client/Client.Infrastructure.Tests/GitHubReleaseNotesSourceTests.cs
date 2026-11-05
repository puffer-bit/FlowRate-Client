using System.Net;
using System.Text.Json;
using Client.Core.Settings;
using Client.Infrastructure.Updates;
using Xunit;

namespace Client.Infrastructure.Tests;

public class GitHubReleaseNotesSourceTests
{
    private static object Release(int day, bool dev = false, bool draft = false) => new
    {
        tag_name = $"v1.0.{day}", name = $"Version {day}", body = "## Fixes\n- Voice", prerelease = dev, draft,
        published_at = $"2026-10-{day:00}T12:00:00Z", html_url = $"https://github.com/puffer-bit/FlowRate-Client/releases/tag/v1.0.{day}"
    };

    private static HttpResponseMessage Page(params object[] releases) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(releases))
    };

    [Theory]
    [InlineData(UpdateChannel.Release)]
    [InlineData(UpdateChannel.Dev)]
    public async Task FiltersChannelAndDraftsOrdersByPublishedDateAndLimitsToSix(UpdateChannel channel)
    {
        using var source = new GitHubReleaseNotesSource(new HttpClient(new Handler(request =>
        {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            Assert.NotEmpty(request.Headers.UserAgent);
            return Page(Enumerable.Range(1, 9).Select(day => Release(day, channel == UpdateChannel.Dev))
                .Append(Release(30, channel != UpdateChannel.Dev)).Append(Release(31, channel == UpdateChannel.Dev, true)).ToArray());
        })));
        var notes = await source.GetAsync(channel, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(6, notes.Count);
        Assert.Equal("v1.0.9", notes[0].Tag);
        Assert.Equal("v1.0.4", notes[^1].Tag);
    }

    [Fact]
    public async Task FollowsPaginationWhenMatchingReleasesAreOnOlderPages()
    {
        var calls = 0;
        using var source = new GitHubReleaseNotesSource(new HttpClient(new Handler(request =>
        {
            if (++calls == 1)
            {
                var first = Page(Release(9, true));
                first.Headers.Add("Link", "<https://api.github.com/repos/puffer-bit/FlowRate-Client/releases?per_page=100&page=2>; rel=\"next\"");
                return first;
            }
            Assert.Contains("page=2", request.RequestUri!.Query);
            return Page(Release(1));
        })));
        Assert.Equal("v1.0.1", Assert.Single(await source.GetAsync(UpdateChannel.Release, cancellationToken: TestContext.Current.CancellationToken)).Tag);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CachesSuccessfulResultsButRefreshBypassesCache()
    {
        var calls = 0;
        using var source = new GitHubReleaseNotesSource(new HttpClient(new Handler(_ => { calls++; return Page(); })));
        Assert.Empty(await source.GetAsync(UpdateChannel.Release, cancellationToken: TestContext.Current.CancellationToken));
        await source.GetAsync(UpdateChannel.Release, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
        await source.GetAsync(UpdateChannel.Release, forceRefresh: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
        await source.GetAsync(UpdateChannel.Dev, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task HttpErrorsAreNotCached()
    {
        var calls = 0;
        using var source = new GitHubReleaseNotesSource(new HttpClient(new Handler(_ =>
            ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Page(Release(1)))));
        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetAsync(UpdateChannel.Release, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(await source.GetAsync(UpdateChannel.Release, cancellationToken: TestContext.Current.CancellationToken));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handle(request));
    }
}

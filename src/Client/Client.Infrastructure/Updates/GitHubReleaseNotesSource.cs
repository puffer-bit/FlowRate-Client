using System.Net.Http.Headers;
using System.Text.Json;
using Client.Application.Ports;
using Client.Application.Updates;
using Client.Core.Settings;
using Client.Tools.AppInfo;

namespace Client.Infrastructure.Updates;

public sealed class GitHubReleaseNotesSource : IReleaseNotesSource, IDisposable
{
    private readonly HttpClient _client;
    private readonly Dictionary<UpdateChannel, (DateTimeOffset Time, IReadOnlyList<PublishedRelease> Releases)> _cache = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GitHubReleaseNotesSource() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }) { }

    public GitHubReleaseNotesSource(HttpClient client) => _client = client;

    public async Task<IReadOnlyList<PublishedRelease>> GetAsync(UpdateChannel channel, bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _cache.TryGetValue(channel, out var cached) && DateTimeOffset.UtcNow - cached.Time < TimeSpan.FromMinutes(5))
                return cached.Releases;

            var releases = new List<PublishedRelease>();
            var repository = new Uri(AppInformation.RepositoryUrl).AbsolutePath;
            string? next = $"https://api.github.com/repos{repository}/releases?per_page=100";
            while (next != null && releases.Count < 6)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, next);
                request.Headers.UserAgent.ParseAdd("FlowRate/1.0");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.GetProperty("draft").GetBoolean() ||
                        item.GetProperty("prerelease").GetBoolean() != (channel == UpdateChannel.Dev))
                        continue;
                    var tag = item.GetProperty("tag_name").GetString() ?? "";
                    var name = item.GetProperty("name").GetString();
                    var url = item.GetProperty("html_url").GetString();
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com")
                        continue;
                    releases.Add(new PublishedRelease(tag, string.IsNullOrWhiteSpace(name) ? tag : name,
                        item.GetProperty("body").GetString() ?? "", item.GetProperty("published_at").GetDateTimeOffset(), uri));
                }
                next = NextPage(response);
            }

            var result = releases.OrderByDescending(release => release.PublishedAt).Take(6).ToArray();
            _cache[channel] = (DateTimeOffset.UtcNow, result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? NextPage(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var links))
            return null;
        foreach (var part in links.SelectMany(value => value.Split(',')))
        {
            var sections = part.Split(';', StringSplitOptions.TrimEntries);
            if (!sections.Skip(1).Contains("rel=\"next\""))
                continue;
            var address = sections[0].Trim('<', '>');
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "api.github.com")
                return address;
        }
        return null;
    }

    public void Dispose()
    {
        _client.Dispose();
        _gate.Dispose();
    }
}

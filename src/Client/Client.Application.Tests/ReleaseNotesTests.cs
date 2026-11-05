using System.Net.Http;
using Client.Application.Ports;
using Client.Application.Updates;
using Client.Core.Settings;
using Client.ViewModels.Settings.Pages;
using Xunit;

namespace Client.Application.Tests;

public class ReleaseNotesTests
{
    private static PublishedRelease Release(int day, string body = "") => new($"v1.0.{day}", $"Release {day}", body,
        new DateTimeOffset(2026, 10, day, 12, 0, 0, TimeSpan.Zero), new Uri($"https://github.com/test/app/releases/tag/v1.0.{day}"));

    [Fact]
    public void MarkdownBecomesReadableSectionsAndPreservesLinkDestinations()
    {
        var note = new ReleaseNote(Release(1, "\uFEFFIntro\r\n\r\n## Fixes\r\n- **Fixed** `voice`\r\n\r\n## Links\n[Details](https://example.com)"));
        Assert.Collection(note.Sections,
            section => { Assert.False(section.HasHeading); Assert.Equal("Intro", section.Body); },
            section => { Assert.Equal("Fixes", section.Heading); Assert.Equal("• Fixed voice", section.Body); },
            section => Assert.Equal("Details (https://example.com)", section.Body));
    }

    [Fact]
    public void MarkdownListsKeepFormattingAndRecognizeSectionIcons()
    {
        var note = new ReleaseNote(Release(1, "## Исправления\n- **Voice** `audio`\n  continues here\n\n## Build\n1. Runtime\n2. Package\n\n## Other\nSome text\nwrapped over lines"));
        Assert.Equal("GearIcon", note.Sections[0].IconKey);
        var bullet = Assert.Single(note.Sections[0].Blocks);
        Assert.Equal("•", bullet.Marker);
        Assert.Equal("**Voice** `audio` continues here", bullet.Markdown);
        Assert.Equal("PlugIcon", note.Sections[1].IconKey);
        Assert.Equal(new[] { "1.", "2." }, note.Sections[1].Blocks.Select(block => block.Marker));
        Assert.Equal("LogsIcon", note.Sections[2].IconKey);
        Assert.False(Assert.Single(note.Sections[2].Blocks).IsListItem);
        Assert.Equal("Some text wrapped over lines", note.Sections[2].Blocks[0].Markdown);
    }

    [Fact]
    public async Task FollowsUpdatePreferencesWithoutLoadingUntilAboutOpens()
    {
        var preferences = new BehaviorControlVM(null!, null!) { UpdateChannelIndex = (int)UpdateChannel.Dev };
        var channels = new List<UpdateChannel>();
        var vm = new ReleaseHistoryVM(new Source((channel, _, _) =>
        {
            channels.Add(channel);
            return Task.FromResult<IReadOnlyList<PublishedRelease>>([]);
        }), preferences);
        Assert.True(vm.IsDev);
        Assert.Empty(channels);
        await vm.EnsureLoadedAsync();
        Assert.Equal(new[] { UpdateChannel.Dev }, channels);
        preferences.UpdateChannelIndex = (int)UpdateChannel.Release;
        Assert.False(vm.IsDev);
        Assert.Equal(new[] { UpdateChannel.Dev, UpdateChannel.Release }, channels);
        vm.IsDev = true;
        Assert.Equal((int)UpdateChannel.Release, preferences.UpdateChannelIndex);
        vm.Dispose();
        preferences.UpdateChannelIndex = (int)UpdateChannel.Dev;
        preferences.UpdateChannelIndex = (int)UpdateChannel.Release;
        Assert.True(vm.IsDev);
    }

    [Fact]
    public async Task ShowsOnlySixNewestAndExpandsFirstCard()
    {
        using var vm = new ReleaseHistoryVM(new Source((_, _, _) => Task.FromResult<IReadOnlyList<PublishedRelease>>(
            Enumerable.Range(1, 9).Select(day => Release(day)).ToArray())));
        await vm.EnsureLoadedAsync();
        Assert.Equal(6, vm.Releases.Count);
        Assert.Equal("v1.0.9", vm.Releases[0].Version);
        Assert.True(vm.Releases[0].IsExpanded);
        Assert.All(vm.Releases.Skip(1), note => Assert.False(note.IsExpanded));
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task ChannelSwitchCancelsPreviousRequestAndIgnoresLateResponse()
    {
        var stable = new TaskCompletionSource<IReadOnlyList<PublishedRelease>>();
        var dev = new TaskCompletionSource<IReadOnlyList<PublishedRelease>>();
        CancellationToken stableToken = default;
        using var vm = new ReleaseHistoryVM(new Source((channel, _, token) =>
        {
            if (channel == UpdateChannel.Release) { stableToken = token; return stable.Task; }
            return dev.Task;
        }));
        var pending = vm.EnsureLoadedAsync();
        vm.ChannelIndex = 1;
        Assert.True(stableToken.IsCancellationRequested);
        var devLoaded = new TaskCompletionSource();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsLoading) && !vm.IsLoading)
                devLoaded.TrySetResult();
        };
        dev.SetResult([Release(9)]);
        await devLoaded.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("v1.0.9", Assert.Single(vm.Releases).Version);
        stable.SetResult([Release(1)]);
        await pending;
        Assert.Equal("v1.0.9", Assert.Single(vm.Releases).Version);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task ErrorCanBeRetriedAndEmptyChannelHasItsOwnState()
    {
        var attempts = 0;
        using var vm = new ReleaseHistoryVM(new Source((_, force, _) =>
        {
            if (++attempts == 1) throw new HttpRequestException("Offline");
            Assert.True(force);
            return Task.FromResult<IReadOnlyList<PublishedRelease>>([]);
        }));
        await vm.EnsureLoadedAsync();
        Assert.True(vm.HasError);
        Assert.False(vm.IsEmpty);
        await vm.ReloadAsync();
        Assert.False(vm.HasError);
        Assert.True(vm.IsEmpty);
        await vm.EnsureLoadedAsync();
        Assert.Equal(2, attempts);
    }

    private sealed class Source(Func<UpdateChannel, bool, CancellationToken, Task<IReadOnlyList<PublishedRelease>>> load) : IReleaseNotesSource
    {
        public Task<IReadOnlyList<PublishedRelease>> GetAsync(UpdateChannel channel, bool forceRefresh = false,
            CancellationToken cancellationToken = default) => load(channel, forceRefresh, cancellationToken);
    }
}

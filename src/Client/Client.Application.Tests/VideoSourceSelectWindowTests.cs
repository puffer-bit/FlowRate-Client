using Client.Application.Ports.Repositories;
using Client.Core.Media;
using Client.ViewModels.Sessions.Hybrid;
using Xunit;

namespace Client.Application.Tests;

public class VideoSourceSelectWindowTests
{
    [Fact]
    public void QualitySelectionsUpdateTheShareSettingsAndAutomaticBitrate()
    {
        var vm = Create(new ScreenShareSettings { MaxHeight = 480, Framerate = 15 });
        var originalLabel = vm.AutoBitrateLabel;

        vm.SelectedResolution = vm.Resolutions.Single(option => option.Value == 1080);
        vm.SelectedFramerate = vm.Framerates.Single(option => option.Value == 60);

        Assert.Equal(1080, vm.Settings.MaxHeight);
        Assert.Equal(60, vm.Settings.Framerate);
        Assert.Single(vm.Resolutions, option => option.IsSelected);
        Assert.Single(vm.Framerates, option => option.IsSelected);
        Assert.NotEqual(originalLabel, vm.AutoBitrateLabel);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("999999", 999999)]
    [InlineData("", null)]
    [InlineData("invalid", null)]
    public void QualityChangesPreserveCustomBitrateAndInvalidInputUsesAutomatic(string input, int? expected)
    {
        var vm = Create(new ScreenShareSettings { BitrateKbps = 1000 });
        vm.CustomBitrate = input;

        vm.SelectedResolution = vm.Resolutions.Single(option => option.Value == 720);
        vm.SelectedFramerate = vm.Framerates.Single(option => option.Value == 144);

        Assert.True(vm.IsCustomBitrate);
        Assert.Equal(input, vm.CustomBitrate);
        Assert.Equal(expected, vm.Settings.BitrateKbps);
        Assert.Equal(720, vm.Settings.MaxHeight);
        Assert.Equal(144, vm.Settings.Framerate);
    }

    private static VideoSourceSelectWindowVM Create(ScreenShareSettings settings) =>
        new(null, null, null, false, settings, new DismissedPrompts());

    private sealed class DismissedPrompts : IDismissedPromptsRepository
    {
        public Task<bool> IsDismissedAsync(string promptKey) => Task.FromResult(false);
        public Task SetAsync(string promptKey, bool isDismissed) => Task.CompletedTask;
    }
}

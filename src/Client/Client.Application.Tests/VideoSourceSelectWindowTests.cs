using Client.Application.Ports.Repositories;
using Client.Application.Ports;
using Client.Core.Settings;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Client.Core.Media;
using Client.ViewModels.Sessions.Hybrid;
using Xunit;

namespace Client.Application.Tests;

[Collection("Media mode")]
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

    [Theory]
    [InlineData(null, "vaav1enc", "25000", true)]
    [InlineData("vaav1enc", "vaav1enc", "0", true)]
    [InlineData("missing", "vaav1enc", "25000", true)]
    [InlineData("qsvav1enc", "vaav1enc", "25000", false)]
    [InlineData("svtav1enc", "svtav1enc", "25000", true)]
    [InlineData("vaav1enc", "vaav1enc", "invalid", false)]
    [InlineData("vaav1enc", "vaav1enc", "999999999", false)]
    public void BitrateOverrideFollowsTheEffectiveEncoderAndRuntimeValidation(
        string? preferred, string configured, string value, bool expected)
    {
        IMediaCapabilities capabilities = new Capabilities();
        var settings = new VideoSettings
        {
            Av1Encoder = preferred, CodecOverrides = [new(configured, "bitrate", value)]
        };
        Assert.Equal(expected, capabilities.GetScreenShareBitrateOverride(VideoCodec.AV1, settings) != null);
        Assert.Null(capabilities.GetScreenShareBitrateOverride(VideoCodec.H264, settings));
        Assert.Null(capabilities.GetScreenShareBitrateOverride(VideoCodec.VP8, settings));
        Assert.Null(capabilities.GetScreenShareBitrateOverride(VideoCodec.AV1,
            settings with { CodecOverrides = [new(configured, "max-bitrate", "25000")] }));
    }

    [Theory]
    [InlineData(VideoCodec.H264, "openh264enc", "bitrate")]
    [InlineData(VideoCodec.AV1, "svtav1enc", "target-bitrate")]
    [InlineData(VideoCodec.VP8, "vp8enc", "target-bitrate")]
    [InlineData(VideoCodec.VP9, "vp9enc", "target-bitrate")]
    public void BitrateOverrideAppliesToEveryCodecIncludingSoftware(VideoCodec codec, string factory, string property)
    {
        IMediaCapabilities capabilities = new Capabilities();
        var settings = new VideoSettings { Encoder = factory, Av1Encoder = factory, Vp8Encoder = factory,
            Vp9Encoder = factory, CodecOverrides = [new(factory, property, "25000")] };
        Assert.Equal(new CodecPropertyOverride(factory, property, "25000"),
            capabilities.GetScreenShareBitrateOverride(codec, settings));
        Assert.Null(capabilities.GetScreenShareBitrateOverride(codec,
            settings with { CodecOverrides = [new(factory, property, "invalid")] }));
    }

    [Fact]
    public async Task EncoderOverrideDisablesBitrateCommandsAndPreservesStreamPreferences()
    {
        var previous = VideoSettings.Current;
        try
        {
            VideoSettings.Apply(new VideoSettings
            {
                Av1Encoder = "vaav1enc", CodecOverrides = [new("vaav1enc", "bitrate", "25000")]
            });
            var settings = new ScreenShareSettings { BitrateKbps = 1000 };
            var vm = new VideoSourceSelectWindowVM(null, null, null, false, settings,
                new DismissedPrompts(), mediaCapabilities: new Capabilities(), codec: VideoCodec.AV1);
            Assert.True(vm.IsBitrateOverridden);
            Assert.Contains("vaav1enc", vm.EncoderBitrateWarning);
            Assert.False(await vm.SelectAutoBitrateCommand.CanExecute.FirstAsync().ToTask());
            Assert.False(await vm.SelectCustomBitrateCommand.CanExecute.FirstAsync().ToTask());
            vm.SelectedResolution = vm.Resolutions.Single(option => option.Value == 720);
            Assert.Equal(720, vm.Settings.MaxHeight);
            Assert.Equal(1000, vm.Settings.BitrateKbps);

            VideoSettings.Apply(new VideoSettings());
            vm = new VideoSourceSelectWindowVM(null, null, null, false, settings,
                new DismissedPrompts(), mediaCapabilities: new Capabilities(), codec: VideoCodec.AV1);
            Assert.False(vm.IsBitrateOverridden);
            Assert.Null(vm.EncoderBitrateWarning);
            Assert.True(await vm.SelectAutoBitrateCommand.CanExecute.FirstAsync().ToTask());
            Assert.True(await vm.SelectCustomBitrateCommand.CanExecute.FirstAsync().ToTask());
            Assert.Equal(1000, vm.Settings.BitrateKbps);
        }
        finally { VideoSettings.Apply(previous); }
    }

    private sealed class Capabilities : IMediaCapabilities
    {
        public bool HasHardwareScreenShareEncoder => true;
        public IReadOnlyList<string> ScreenShareDecoders => [];
        public IReadOnlyList<string> ScreenShareAv1Decoders => [];
        public IReadOnlyList<string> ScreenShareEncoders => ["openh264enc"];
        public IReadOnlyList<string> ScreenShareAv1Encoders => ["vaav1enc", "qsvav1enc", "svtav1enc"];
        public IReadOnlyList<string> GetScreenShareEncoders(VideoCodec codec) => codec switch
        {
            VideoCodec.H264 => ScreenShareEncoders,
            VideoCodec.AV1 => ScreenShareAv1Encoders,
            VideoCodec.VP8 => ["vp8enc"],
            VideoCodec.VP9 => ["vp9enc"],
            _ => []
        };
        public bool CanCaptureScreenAudio => false;
        public IReadOnlyList<CodecParameter> GetCodecParameters(string factory) =>
            [new("bitrate", "Bitrate", "0", "integer", "0", "2048000"),
                new("target-bitrate", "Target bitrate", "0", "integer", "0", "2048000")];
    }

    private static VideoSourceSelectWindowVM Create(ScreenShareSettings settings) =>
        new(null, null, null, false, settings, new DismissedPrompts());

    private sealed class DismissedPrompts : IDismissedPromptsRepository
    {
        public Task<bool> IsDismissedAsync(string promptKey) => Task.FromResult(false);
        public Task SetAsync(string promptKey, bool isDismissed) => Task.CompletedTask;
    }
}

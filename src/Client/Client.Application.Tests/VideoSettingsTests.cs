using System.Reactive.Threading.Tasks;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Core.Settings;
using Client.ViewModels.Settings.Pages;
using Xunit;

namespace Client.Application.Tests;

[Collection("Media mode")]
public class VideoSettingsTests
{
    [Fact]
    public async Task Av1ChoicesLoadSaveIndependentlyAndReset()
    {
        var service = new VideoService(new VideoSettings
        {
            Encoder = "openh264enc", Av1Encoder = "svtav1enc",
            Decoder = "openh264dec", Av1Decoder = "dav1ddec"
        });
        var video = new VideoControlVM(service, new Capabilities());

        await video.LoadCommand.Execute().ToTask();

        Assert.Equal(1, video.EncoderIndex);
        Assert.Equal(2, video.Av1EncoderIndex);
        Assert.Equal(3, video.Av1EncoderChoices.Count);
        Assert.Equal(1, video.DecoderIndex);
        Assert.Equal(2, video.Av1DecoderIndex);
        Assert.Equal(0, service.SaveCount);

        video.Av1EncoderIndex = 1;

        Assert.Equal("vaav1enc", service.Settings.Av1Encoder);
        Assert.Equal("openh264enc", service.Settings.Encoder);
        Assert.Equal(1, service.SaveCount);
        Assert.Equal("dav1ddec", service.Settings.Av1Decoder);

        video.Av1DecoderIndex = 1;

        Assert.Equal("vaav1dec", service.Settings.Av1Decoder);
        Assert.Equal("openh264dec", service.Settings.Decoder);
        Assert.Equal("vaav1enc", service.Settings.Av1Encoder);
        Assert.Equal(2, service.SaveCount);

        await video.ResetAllAsync();

        Assert.Equal(0, video.Av1EncoderIndex);
        Assert.Equal(0, video.Av1DecoderIndex);
        Assert.Equal(new VideoSettings(), service.Settings);
    }

    [Fact]
    public async Task UnavailableAv1CodecsLoadAsAutomatic()
    {
        var service = new VideoService(new VideoSettings { Av1Encoder = "missing", Av1Decoder = "missing" });
        var video = new VideoControlVM(service, new Capabilities());

        await video.LoadCommand.Execute().ToTask();

        Assert.Equal(0, video.Av1EncoderIndex);
        Assert.Equal(0, video.Av1DecoderIndex);
        Assert.Equal(0, service.SaveCount);
    }

    [Fact]
    public async Task HardwareEncoderEditorPreservesOtherSettingsAndCancelIsIsolated()
    {
        var service = new VideoService(new VideoSettings
        {
            Encoder = "openh264enc", Av1Encoder = "vaav1enc",
            EncoderOverrides = [new("qsvav1enc", "qp", "10")]
        });
        var video = new VideoControlVM(service, new Capabilities());
        await video.LoadCommand.Execute().ToTask();
        Assert.True(video.CanConfigureAv1Encoder);
        var editor = Assert.IsType<EncoderSettingsVM>(video.CreateEncoderSettings());
        var qp = Assert.Single(editor.Parameters);
        qp.IsOverridden = true;
        qp.Value = "999";
        Assert.False(await editor.SaveAsync());
        Assert.Equal(0, service.SaveCount);
        qp.Value = "100";
        // Editing a draft never writes preferences until Save.
        Assert.Single(service.Settings.EncoderOverrides);
        Assert.True(await editor.SaveAsync());
        Assert.Contains(new EncoderPropertyOverride("vaav1enc", "qp", "100"), service.Settings.EncoderOverrides);
        Assert.Contains(new EncoderPropertyOverride("qsvav1enc", "qp", "10"), service.Settings.EncoderOverrides);
        video.IsZeroCopyEnabled = false;
        Assert.Equal(2, service.Settings.EncoderOverrides.Count);
        editor = Assert.IsType<EncoderSettingsVM>(video.CreateEncoderSettings());
        editor.Reset();
        Assert.True(await editor.SaveAsync());
        Assert.Single(service.Settings.EncoderOverrides);
        Assert.Equal("qsvav1enc", service.Settings.EncoderOverrides[0].Encoder);
        video.Av1EncoderIndex = 2;
        Assert.False(video.CanConfigureAv1Encoder);
        Assert.Null(video.CreateEncoderSettings());
        video.Av1EncoderIndex = 0;
        Assert.True(video.CanConfigureAv1Encoder);
        await video.ResetAllAsync();
        Assert.Empty(service.Settings.EncoderOverrides);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("255", true)]
    [InlineData("256", false)]
    [InlineData("-1", false)]
    [InlineData("1.5", false)]
    [InlineData("nan", false)]
    [InlineData("1 ! fakesink", false)]
    public void EncoderParameterEnforcesItsRuntimeRange(string value, bool valid)
    {
        var parameter = new EncoderParameter("qp", "Quantizer", "128", "integer", "0", "255");
        Assert.Equal(valid, parameter.IsValid(value));
    }

    private sealed class VideoService(VideoSettings settings) : IVideoService
    {
        public VideoSettings Settings { get; private set; } = settings;
        public int SaveCount { get; private set; }
        public Task ApplyAsync() => Task.CompletedTask;
        public Task<VideoSettings> GetSettingsAsync() => Task.FromResult(Settings);
        public Task SaveSettingsAsync(VideoSettings value)
        {
            Settings = value;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class Capabilities : IMediaCapabilities
    {
        public bool HasHardwareScreenShareEncoder => false;
        public IReadOnlyList<string> ScreenShareDecoders => ["openh264dec"];
        public IReadOnlyList<string> ScreenShareAv1Decoders => ["vaav1dec", "dav1ddec"];
        public IReadOnlyList<string> ScreenShareEncoders => ["openh264enc"];
        public IReadOnlyList<string> ScreenShareAv1Encoders => ["vaav1enc", "svtav1enc"];
        public bool CanCaptureScreenAudio => false;
        public IReadOnlyList<EncoderParameter> GetEncoderParameters(string encoder) =>
            [new("qp", "Quantizer", "128", "integer", "0", "255")];
    }
}

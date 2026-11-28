using System.Reactive.Threading.Tasks;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Core.Settings;
using Client.Core.Media;
using Client.ViewModels.Settings.Pages;
using Client.ViewModels.Dialogs;
using Xunit;

namespace Client.Application.Tests;

[Collection("Media mode")]
public class VideoSettingsTests
{
    [Fact]
    public async Task CameraCodecWarningAcknowledgmentRemainsSharedWhenChangingProfiles()
    {
        var service = new VideoService(new VideoSettings());
        var video = new VideoControlVM(service, new Capabilities());
        await video.LoadCommand.Execute().ToTask();
        video.VideoTargetIndex = 1;
        using (video.ShowMessageBox.RegisterHandler(context => context.SetOutput(Result.Ok)))
            Assert.NotNull(await video.OpenCodecSettingsAsync(video.Encoders[0]));
        video.Encoders[0].SelectedIndex = 1;
        Assert.True(service.Settings.IsCodecSettingsWarningAcknowledged);
        video.VideoTargetIndex = 0;
        Assert.NotNull(await video.OpenCodecSettingsAsync(video.Encoders[0]));
    }

    [Fact]
    public async Task SwitchingVideoTargetPreservesIndependentSelectionsAndOverrides()
    {
        var service = new VideoService(new VideoSettings
        {
            Av1Encoder = "svtav1enc", CodecOverrides = [new("vp8enc", "qp", "20")],
            Camera = new VideoSettings { Av1Encoder = "vaav1enc", CodecOverrides = [new("vp8enc", "qp", "40")] }
        });
        var video = new VideoControlVM(service, new Capabilities());
        await video.LoadCommand.Execute().ToTask();
        Assert.Equal("svtav1enc", video.Encoders.Single(e => e.Codec == VideoCodec.AV1).PreferredFactory);
        video.VideoTargetIndex = 1;
        Assert.Equal(0, service.SaveCount);
        var selection = video.Encoders.Single(e => e.Codec == VideoCodec.AV1);
        Assert.Equal("vaav1enc", selection.PreferredFactory);
        selection.SelectedIndex = 0;
        Assert.Null(service.Settings.ForCamera().Av1Encoder);
        Assert.Equal("svtav1enc", service.Settings.Av1Encoder);
        Assert.Equal("20", Assert.Single(service.Settings.CodecOverrides).Value);
        Assert.Equal("40", Assert.Single(service.Settings.ForCamera().CodecOverrides).Value);
        video.VideoTargetIndex = 0;
        Assert.Equal("svtav1enc", video.Encoders.Single(e => e.Codec == VideoCodec.AV1).PreferredFactory);
    }

    [Fact]
    public async Task CodecWarningCancellationDoesNotOpenOrSaveAndAcknowledgmentSurvivesReloadAndReset()
    {
        var service = new VideoService(new VideoSettings());
        var video = new VideoControlVM(service, new Capabilities());
        await video.LoadCommand.Execute().ToTask();
        using (video.ShowMessageBox.RegisterHandler(context => context.SetOutput(Result.Cancel)))
            Assert.Null(await video.OpenCodecSettingsAsync(video.Encoders[0]));
        Assert.Equal(0, service.SaveCount);
        using (video.ShowMessageBox.RegisterHandler(context =>
        {
            Assert.Equal(Buttons.OkCancel, context.Input.Buttons);
            context.SetOutput(Result.Ok);
        }))
            Assert.NotNull(await video.OpenCodecSettingsAsync(video.Decoders[0]));
        Assert.True(service.Settings.IsCodecSettingsWarningAcknowledged);
        // No handler is needed after acknowledgment, including after a new view model is loaded.
        video = new VideoControlVM(service, new Capabilities());
        await video.LoadCommand.Execute().ToTask();
        Assert.NotNull(await video.OpenCodecSettingsAsync(video.Encoders[0]));
        video.IsZeroCopyEnabled = false;
        Assert.True(service.Settings.IsCodecSettingsWarningAcknowledged);
        await video.ResetAllAsync();
        Assert.True(service.Settings.IsCodecSettingsWarningAcknowledged);
        Assert.NotNull(await video.OpenCodecSettingsAsync(video.Decoders[0]));
    }

    [Fact]
    public async Task OverrideIndicatorFollowsSavedParametersAndEffectiveFactory()
    {
        var service = new VideoService(new VideoSettings
        {
            CodecOverrides = [new("vaav1enc", "qp", "42")]
        });
        var video = new VideoControlVM(service, new Capabilities());
        await video.LoadCommand.Execute().ToTask();
        var selection = video.Encoders.Single(item => item.Codec == VideoCodec.AV1);
        Assert.True(selection.HasOverrides);
        Assert.False(video.Decoders[0].HasOverrides);
        selection.SelectedIndex = 2;
        Assert.False(selection.HasOverrides);
        selection.SelectedIndex = 0;
        Assert.True(selection.HasOverrides);
        var editor = video.CreateCodecSettings(selection)!;
        editor.Reset();
        Assert.True(selection.HasOverrides);
        Assert.True(await editor.SaveAsync());
        Assert.False(selection.HasOverrides);
        var parameter = Assert.Single(editor.Parameters);
        parameter.IsOverridden = true;
        parameter.Value = "42";
        Assert.True(await editor.SaveAsync());
        Assert.True(selection.HasOverrides);
        await video.ResetAllAsync();
        Assert.False(selection.HasOverrides);
    }

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

        Assert.Equal(1, video.Encoders.Single(item => item.Codec == VideoCodec.H264).SelectedIndex);
        Assert.Equal(2, video.Encoders.Single(item => item.Codec == VideoCodec.AV1).SelectedIndex);
        Assert.Equal(3, video.Encoders.Single(item => item.Codec == VideoCodec.AV1).Choices.Count);
        Assert.Equal(1, video.Decoders.Single(item => item.Codec == VideoCodec.H264).SelectedIndex);
        Assert.Equal(2, video.Decoders.Single(item => item.Codec == VideoCodec.AV1).SelectedIndex);
        Assert.Equal(0, service.SaveCount);

        video.Encoders.Single(item => item.Codec == VideoCodec.AV1).SelectedIndex = 1;

        Assert.Equal("vaav1enc", service.Settings.Av1Encoder);
        Assert.Equal("openh264enc", service.Settings.Encoder);
        Assert.Equal(1, service.SaveCount);
        Assert.Equal("dav1ddec", service.Settings.Av1Decoder);

        video.Decoders.Single(item => item.Codec == VideoCodec.AV1).SelectedIndex = 1;

        Assert.Equal("vaav1dec", service.Settings.Av1Decoder);
        Assert.Equal("openh264dec", service.Settings.Decoder);
        Assert.Equal("vaav1enc", service.Settings.Av1Encoder);
        Assert.Equal(2, service.SaveCount);

        await video.ResetAllAsync();

        Assert.Equal(0, video.Encoders.Single(item => item.Codec == VideoCodec.AV1).SelectedIndex);
        Assert.Equal(0, video.Decoders.Single(item => item.Codec == VideoCodec.AV1).SelectedIndex);
        Assert.Equal(new VideoSettings(), service.Settings);
    }

    [Fact]
    public async Task UnavailableAv1CodecsLoadAsAutomatic()
    {
        var service = new VideoService(new VideoSettings { Av1Encoder = "missing", Av1Decoder = "missing" });
        var video = new VideoControlVM(service, new Capabilities());

        await video.LoadCommand.Execute().ToTask();

        Assert.Equal(0, video.Encoders.Single(item => item.Codec == VideoCodec.AV1).SelectedIndex);
        Assert.Equal(0, video.Decoders.Single(item => item.Codec == VideoCodec.AV1).SelectedIndex);
        Assert.Equal(0, service.SaveCount);
    }

    [Fact]
    public async Task HardwareEncoderEditorPreservesOtherSettingsAndCancelIsIsolated()
    {
        var service = new VideoService(new VideoSettings
        {
            Encoder = "openh264enc", Av1Encoder = "vaav1enc",
            CodecOverrides = [new("qsvav1enc", "qp", "10")]
        });
        var video = new VideoControlVM(service, new Capabilities());
        await video.LoadCommand.Execute().ToTask();
        Assert.True(video.Encoders.Single(item => item.Codec == VideoCodec.AV1).CanConfigure);
        var editor = Assert.IsType<CodecSettingsVM>(video.CreateCodecSettings(video.Encoders.Single(item => item.Codec == VideoCodec.AV1)));
        var qp = Assert.Single(editor.Parameters);
        qp.IsOverridden = true;
        qp.Value = "999";
        Assert.False(await editor.SaveAsync());
        Assert.Equal(0, service.SaveCount);
        qp.Value = "100";
        // Editing a draft never writes preferences until Save.
        Assert.Single(service.Settings.CodecOverrides);
        Assert.True(await editor.SaveAsync());
        Assert.Contains(new CodecPropertyOverride("vaav1enc", "qp", "100"), service.Settings.CodecOverrides);
        Assert.Contains(new CodecPropertyOverride("qsvav1enc", "qp", "10"), service.Settings.CodecOverrides);
        video.IsZeroCopyEnabled = false;
        Assert.Equal(2, service.Settings.CodecOverrides.Count);
        editor = Assert.IsType<CodecSettingsVM>(video.CreateCodecSettings(video.Encoders.Single(item => item.Codec == VideoCodec.AV1)));
        editor.Reset();
        Assert.True(await editor.SaveAsync());
        Assert.Single(service.Settings.CodecOverrides);
        Assert.Equal("qsvav1enc", service.Settings.CodecOverrides[0].Factory);
        video.Encoders.Single(item => item.Codec == VideoCodec.AV1).SelectedIndex = 2;
        Assert.True(video.Encoders.Single(item => item.Codec == VideoCodec.AV1).CanConfigure);
        Assert.Equal("svtav1enc", video.CreateCodecSettings(video.Encoders.Single(item => item.Codec == VideoCodec.AV1))!.Factory);
        video.Encoders.Single(item => item.Codec == VideoCodec.AV1).SelectedIndex = 0;
        Assert.True(video.Encoders.Single(item => item.Codec == VideoCodec.AV1).CanConfigure);
        await video.ResetAllAsync();
        Assert.Empty(service.Settings.CodecOverrides);
    }

    [Theory]
    [InlineData(VideoCodec.H264, true)]
    [InlineData(VideoCodec.AV1, true)]
    [InlineData(VideoCodec.VP8, true)]
    [InlineData(VideoCodec.VP9, true)]
    [InlineData(VideoCodec.H264, false)]
    [InlineData(VideoCodec.AV1, false)]
    [InlineData(VideoCodec.VP8, false)]
    [InlineData(VideoCodec.VP9, false)]
    public async Task EveryCodecCanSaveParametersForItsEffectiveEncoderOrDecoder(VideoCodec codec, bool isEncoder)
    {
        var service = new VideoService(new VideoSettings());
        var video = new VideoControlVM(service, new Capabilities());
        await video.LoadCommand.Execute().ToTask();
        var selection = (isEncoder ? video.Encoders : video.Decoders).Single(item => item.Codec == codec);
        Assert.Null(selection.PreferredFactory);
        Assert.NotNull(Client.Avalonia.Localization.Strings.FindInvariant($"settings.{selection.SettingKey}.title"));
        var editor = Assert.IsType<CodecSettingsVM>(video.CreateCodecSettings(selection));
        var parameter = Assert.Single(editor.Parameters);
        parameter.IsOverridden = true;
        parameter.Value = "42";
        Assert.True(await editor.SaveAsync());
        Assert.Equal(new CodecPropertyOverride(selection.EffectiveFactory!, "qp", "42"),
            Assert.Single(service.Settings.CodecOverrides));

        selection.SelectedIndex = 1;
        Assert.Equal(selection.PreferredFactory, isEncoder ? service.Settings.GetEncoder(codec) : service.Settings.GetDecoder(codec));
        Assert.Null(isEncoder ? service.Settings.GetDecoder(codec) : service.Settings.GetEncoder(codec));
        await video.ResetAllAsync();
        Assert.Equal(0, selection.SelectedIndex);
        Assert.Empty(service.Settings.CodecOverrides);
    }

    [Fact]
    public async Task DecoderResetPreservesEncoderOverrides()
    {
        var service = new VideoService(new VideoSettings());
        var video = new VideoControlVM(service, new Capabilities());
        await video.LoadCommand.Execute().ToTask();
        var encoder = Assert.IsType<CodecSettingsVM>(video.CreateCodecSettings(video.Encoders[0]));
        var decoder = Assert.IsType<CodecSettingsVM>(video.CreateCodecSettings(video.Decoders[0]));
        foreach (var editor in new[] { encoder, decoder })
        {
            var parameter = Assert.Single(editor.Parameters);
            parameter.IsOverridden = true;
            parameter.Value = "42";
            Assert.True(await editor.SaveAsync());
        }
        Assert.Equal(2, service.Settings.CodecOverrides.Count);
        decoder.Reset();
        Assert.True(await decoder.SaveAsync());
        Assert.Equal(encoder.Factory, Assert.Single(service.Settings.CodecOverrides).Factory);
    }

    [Fact]
    public void OverrideValidationRejectsUnknownPropertiesAndUsesLastSavedValue()
    {
        CodecParameter[] parameters = [new("qp", "Quantizer", "128", "integer", "0", "255")];
        CodecPropertyOverride[] saved = [new("a", "qp", "10"), new("b", "qp", "20"),
            new("a", "unknown", "1"), new("a", "qp", "30")];
        Assert.Equal(new CodecPropertyOverride("a", "qp", "30"),
            Assert.Single(CodecParameter.ValidateOverrides("a", parameters, saved)));
        Assert.Empty(CodecParameter.ValidateOverrides("a", parameters, [..saved, new("a", "qp", "999")]));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("255", true)]
    [InlineData("256", false)]
    [InlineData("-1", false)]
    [InlineData("1.5", false)]
    [InlineData("nan", false)]
    [InlineData("1 ! fakesink", false)]
    public void CodecParameterEnforcesItsRuntimeRange(string value, bool valid)
    {
        var parameter = new CodecParameter("qp", "Quantizer", "128", "integer", "0", "255");
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
        public IReadOnlyList<string> GetScreenShareEncoders(VideoCodec codec) => codec switch
        {
            VideoCodec.H264 => ScreenShareEncoders,
            VideoCodec.AV1 => ScreenShareAv1Encoders,
            VideoCodec.VP8 => ["vp8enc"],
            VideoCodec.VP9 => ["vp9enc"],
            _ => []
        };
        public IReadOnlyList<string> GetScreenShareDecoders(VideoCodec codec) => codec switch
        {
            VideoCodec.H264 => ScreenShareDecoders,
            VideoCodec.AV1 => ScreenShareAv1Decoders,
            VideoCodec.VP8 => ["vp8dec"],
            VideoCodec.VP9 => ["vp9dec"],
            _ => []
        };
        public bool CanCaptureScreenAudio => false;
        public IReadOnlyList<CodecParameter> GetCodecParameters(string encoder) =>
            [new("qp", "Quantizer", "128", "integer", "0", "255")];
    }
}

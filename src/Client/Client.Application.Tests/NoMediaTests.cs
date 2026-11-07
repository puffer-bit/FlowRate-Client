using System.Reflection;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Application.Sounds;
using Client.Core.Media;
using Client.Core.Settings;
using Client.Core.Sessions.Hybrid;
using Client.LibWebRTC.Native;
using Client.Services;
using Client.ViewModels.Sessions;
using Client.ViewModels.Settings.Pages;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums.WebRTC;
using Xunit;

namespace Client.Application.Tests;

[CollectionDefinition("Media mode", DisableParallelization = true)]
public class MediaModeCollection;

[Collection("Media mode")]
public class NoMediaTests : IDisposable
{
    public NoMediaTests() => MediaRuntime.Configure(["--no-media"]);

    public void Dispose() => MediaRuntime.Configure([]);

    [Fact]
    public void VoiceSessionCannotBeCreatedOrRestored()
    {
        var coordinator = DispatchProxy.Create<ICoordinatorSession, EventOnlyCoordinator>();
        using var sessions = new OpenedSessions(coordinator, null!, null!,
            _ => throw new InvalidOperationException("Voice factory must not be called"), null!);
        var model = new HybridSessionModel
        {
            Id = 1, Name = "Voice", Capacity = 10, HostId = 1,
            SessionType = default, EngineType = WebRTCEngine.Mediasoup
        };

        Assert.Null(sessions.Open(model));
        Assert.Null(sessions.HybridSession);
        Assert.False(sessions.IsInHybridSession);
    }

    [Fact]
    public void VideoSettingsDoNotProbeNativeCodecs()
    {
        var capabilities = DispatchProxy.Create<IMediaCapabilities, RejectCalls>();
        var video = new VideoControlVM(null!, capabilities);

        Assert.False(video.IsMediaEnabled);
        Assert.Single(video.DecoderChoices);
        Assert.Single(video.EncoderChoices);
    }

    [Fact]
    public void LibWebRtcDoesNotLoadNativeLibraryOrStartAudio()
    {
        var audio = DispatchProxy.Create<IVoiceAudioDevice, RejectCalls>();
        var runtime = new LibWebRTCRuntime(audio, NullLogger<LibWebRTCRuntime>.Instance);

        Assert.False(runtime.TryLoad());
        Assert.Contains("--no-media", runtime.Load());
    }

    [Fact]
    public async Task ResetAudioSettingsResetsInterfaceSoundsAndPreservesMicrophoneSettings()
    {
        var service = DispatchProxy.Create<IAudioService, AudioSettingsOnly>();
        var recorder = (AudioSettingsOnly)(object)service;
        recorder.Settings.AreSoundsEnabled = false;
        recorder.Settings.SoundsVolume = 0.12;
        recorder.Settings.OutputVolume = 0.42;
        using var audio = new AudioControlVM(
            service,
            DispatchProxy.Create<IMicrophoneTester, IdleMicrophone>(),
            DispatchProxy.Create<ISoundService, RejectCalls>(),
            DispatchProxy.Create<ISoundPackCatalog, EmptySoundPacks>());

        await audio.LoadCommand.Execute().ToTask();
        await audio.ResetAllAsync();

        Assert.False(audio.IsMediaEnabled);
        Assert.False(await audio.ToggleTestCommand.CanExecute.FirstAsync());
        Assert.Equal(37, audio.InputVolumePercent);
        Assert.Equal(0.42, recorder.Settings.OutputVolume);
        Assert.Equal(0.37, recorder.Settings.InputVolume);
        Assert.Equal(1, recorder.Saves);
        Assert.True(recorder.Settings.AreSoundsEnabled);
        Assert.Equal(new AudioSettings().SoundsVolume, recorder.Settings.SoundsVolume);
        Assert.False(audio.IsTestRunning);
    }

    [Fact]
    public async Task InterfaceSoundChangesAreSavedWithoutChangingUnavailableMicrophoneSettings()
    {
        var service = DispatchProxy.Create<IAudioService, AudioSettingsOnly>();
        var recorder = (AudioSettingsOnly)(object)service;
        using var audio = new AudioControlVM(service,
            DispatchProxy.Create<IMicrophoneTester, IdleMicrophone>(),
            DispatchProxy.Create<ISoundService, RejectCalls>(),
            DispatchProxy.Create<ISoundPackCatalog, EmptySoundPacks>());
        await audio.LoadCommand.Execute().ToTask();

        audio.InputVolumePercent = 123;
        audio.AreSoundsEnabled = false;
        await recorder.Saved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(recorder.Settings.AreSoundsEnabled);
        Assert.Equal(0.37, recorder.Settings.InputVolume);
        Assert.False(audio.IsTestRunning);
    }

    [Fact]
    public void ChatPlaybackIsRejectedBeforeAccessingAttachmentOrCoordinator()
    {
        using var playback = new ChatMediaPlayback();

        Assert.False(playback.Begin(null!, null!));
        Assert.Null(playback.Current);
        Assert.Null(playback.File);
    }

    public class EventOnlyCoordinator : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name.StartsWith("add_") || targetMethod.Name.StartsWith("remove_"))
                return null;
            throw new InvalidOperationException($"Unexpected coordinator request: {targetMethod.Name}");
        }
    }

    public class RejectCalls : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Native media must not be accessed: {targetMethod!.Name}");
    }

    public class AudioSettingsOnly : DispatchProxy
    {
        public AudioSettings Settings { get; private set; } = new() { InputVolume = 0.37 };
        public int Saves { get; private set; }
        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "GetSettingsAsync" => Task.FromResult(Settings),
            "SaveSettingsAsync" => Save((AudioSettings)args![0]!),
            "add_InputDevicesChanged" or "remove_InputDevicesChanged" or
            "add_OutputDevicesChanged" or "remove_OutputDevicesChanged" => null,
            _ => throw new InvalidOperationException($"Unexpected audio access: {method.Name}")
        };

        private Task Save(AudioSettings settings)
        {
            Settings = settings;
            Saves++;
            Saved.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public class IdleMicrophone : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name.StartsWith("add_") || method.Name.StartsWith("remove_") || method.Name == "Stop")
                return null;
            throw new InvalidOperationException($"Microphone must remain idle: {method.Name}");
        }
    }

    public class EmptySoundPacks : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "GetPacks" => Array.Empty<string>(),
            _ => throw new InvalidOperationException($"Unexpected sound pack access: {method.Name}")
        };
    }
}

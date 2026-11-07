using System.Reflection;
using System.Reactive.Threading.Tasks;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Application.Sounds;
using Client.Core.Media;
using Client.ViewModels.Settings.Pages;
using Xunit;

namespace Client.Application.Tests;

[Collection("Media mode")]
public class AudioOutputDeviceTests
{
    [Theory]
    [InlineData(null, null, null, true)]
    [InlineData("speaker", "Speakers", "speaker", true)]
    [InlineData("old-id", "Speakers", "speaker", true)]
    [InlineData("missing", "Disconnected headset", "missing", false)]
    public async Task LoadsOutputChoiceIncludingDefaultRenamedAndDisconnectedDevices(
        string? savedId, string? savedName, string? expectedId, bool available)
    {
        MediaRuntime.Configure([]);
        var service = DispatchProxy.Create<IAudioService, AudioServiceProxy>();
        var state = (AudioServiceProxy)(object)service;
        state.Settings.OutputDeviceId = savedId;
        state.Settings.OutputDeviceName = savedName;
        using var audio = Create(service);

        await audio.LoadCommand.Execute().ToTask();

        Assert.NotNull(audio.SelectedOutputDevice);
        Assert.Equal(expectedId, audio.SelectedOutputDevice.Device?.Id);
        Assert.Equal(available, audio.SelectedOutputDevice.IsAvailable);
        Assert.Null(audio.OutputDevices[0].Device);
        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public async Task SavesOutputChoiceAndSystemDefaultWithoutLosingMicrophoneSettings()
    {
        MediaRuntime.Configure([]);
        var service = DispatchProxy.Create<IAudioService, AudioServiceProxy>();
        var state = (AudioServiceProxy)(object)service;
        state.Settings.InputDeviceId = "microphone";
        using var audio = Create(service);
        await audio.LoadCommand.Execute().ToTask();

        audio.SelectedOutputDevice = audio.OutputDevices.Single(item => item.Device?.Id == "speaker");

        await state.Saved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("speaker", state.Settings.OutputDeviceId);
        Assert.Equal("Speakers", state.Settings.OutputDeviceName);
        Assert.Equal("microphone", state.Settings.InputDeviceId);

        audio.SelectedOutputDevice = audio.OutputDevices[0];

        Assert.Null(state.Settings.OutputDeviceId);
        Assert.Null(state.Settings.OutputDeviceName);
        Assert.Equal(2, state.Saves);
    }

    [Fact]
    public async Task LoadsAndSavesOutputVolumeWithoutChangingDeviceChoices()
    {
        MediaRuntime.Configure([]);
        var service = DispatchProxy.Create<IAudioService, AudioServiceProxy>();
        var state = (AudioServiceProxy)(object)service;
        state.Settings.OutputVolume = 0.45;
        state.Settings.OutputDeviceId = "speaker";
        state.Settings.InputDeviceId = "microphone";
        using var audio = Create(service);
        await audio.LoadCommand.Execute().ToTask();

        Assert.Equal(45, audio.OutputVolumePercent);
        Assert.Equal(0, state.Saves);
        audio.OutputVolumePercent = 75;
        await state.Saved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(0.75, state.Settings.OutputVolume);
        Assert.Equal("speaker", state.Settings.OutputDeviceId);
        Assert.Equal("microphone", state.Settings.InputDeviceId);
        Assert.Equal(0.37, state.Settings.InputVolume);
    }

    [Fact]
    public async Task ResetRestoresOutputVolumeToOneHundredPercent()
    {
        MediaRuntime.Configure([]);
        var service = DispatchProxy.Create<IAudioService, AudioServiceProxy>();
        var state = (AudioServiceProxy)(object)service;
        state.Settings.OutputVolume = 0;
        using var audio = Create(service);
        await audio.LoadCommand.Execute().ToTask();

        await audio.ResetAllAsync();

        Assert.Equal(1, state.Settings.OutputVolume);
        Assert.Equal(100, audio.OutputVolumePercent);
    }

    private static AudioControlVM Create(IAudioService service) => new(service,
        DispatchProxy.Create<IMicrophoneTester, MicrophoneProxy>(),
        DispatchProxy.Create<ISoundService, NoMediaTests.RejectCalls>(),
        DispatchProxy.Create<ISoundPackCatalog, NoMediaTests.EmptySoundPacks>());

    public class AudioServiceProxy : NoMediaTests.AudioSettingsOnly
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "GetInputDevices" => Array.Empty<AudioDeviceInfo>(),
            "GetOutputDevices" => new AudioDeviceInfo[] { new("speaker", "Speakers", true) },
            _ => base.Invoke(method, args)
        };
    }

    public class MicrophoneProxy : NoMediaTests.IdleMicrophone
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name.StartsWith("Set") ? null : base.Invoke(method, args);
    }
}
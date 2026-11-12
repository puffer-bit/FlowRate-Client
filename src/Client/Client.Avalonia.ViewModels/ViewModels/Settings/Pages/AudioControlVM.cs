using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Application.Settings.Search;
using Client.Application.Sounds;
using Client.Avalonia.Localization;
using Client.Core.Media;
using Client.Core.Settings;
using Client.Services;
using ReactiveUI;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public class AudioControlVM : ReactiveObject, ISettingVM, IDisposable
{
    private static readonly TimeSpan SettingsSaveDelay = TimeSpan.FromMilliseconds(300);

    private static readonly SoundEvent[] SoundDisplayOrder =
    [
        // Connection and participants.
        SoundEvent.Connected, SoundEvent.Disconnected,
        SoundEvent.Join, SoundEvent.Leave, SoundEvent.UserKicked,
        // Calls and chat notifications.
        SoundEvent.CallIncoming, SoundEvent.Message, SoundEvent.MessageSent,
        SoundEvent.Mention, SoundEvent.Poke,
        // Microphone and playback controls.
        SoundEvent.Unmute, SoundEvent.Mute, SoundEvent.Undeafen, SoundEvent.Deafen,
        // Screen sharing and other status changes.
        SoundEvent.ScreenShareStart, SoundEvent.ScreenShareStop,
        SoundEvent.Afk, SoundEvent.Error
    ];

    public string Name => SettingText.TabName("audio");
    public SettingTabType Type => SettingTabType.Audio;
    public bool IsEnabled => true;
    public bool IsMediaEnabled => MediaRuntime.IsEnabled;

    // Only the rows the engine of the voice shows
    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("audio",
        ("microphone", ["inputDevice", "inputVolume", "noiseSuppression",
            "echoCancellation", "autoGainControl", "voiceGate", "testMicrophone", "inputLevel",
            "voiceThreshold", "hearMyself", "voiceDetection", "adaptiveThreshold"]),
        ("output", ["outputDevice", "outputVolume"]),
        ("sounds", ["soundsEnabled", "soundsVolume", "soundPack", "soundEvents"]));

    public ObservableCollection<AudioInputDeviceItem> InputDevices { get; } = new();

    public AudioInputDeviceItem? SelectedInputDevice
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<AudioInputDeviceItem> OutputDevices { get; } = new();

    public AudioInputDeviceItem? SelectedOutputDevice
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    private (string Id, string Name)? _savedOutputDevice;
    public double MinOutputVolumePercent => AudioSettings.MinOutputVolume * 100;
    public double MaxOutputVolumePercent => AudioSettings.MaxOutputVolume * 100;

    public double OutputVolumePercent
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 100;

    public double MinInputVolumePercent => AudioSettings.MinInputVolume * 100;
    public double MaxInputVolumePercent => AudioSettings.MaxInputVolume * 100;

    public double InputVolumePercent
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 100;

    // Stored enum values match the order of the suppression choices in the view.
    public int NoiseSuppressionIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = (int)NoiseSuppressionLevel.Moderate;

    public bool IsEchoCancellationEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    public bool IsAutoGainControlEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    public bool IsVoiceGateEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    public bool IsVoiceDetectionEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsAdaptiveThresholdEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public double MinVoiceGateThresholdDb => AudioSettings.MinVoiceGateThresholdDb;
    public double MaxVoiceGateThresholdDb => AudioSettings.MaxVoiceGateThresholdDb;

    public double VoiceGateThresholdDb
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = -32;

    public bool AreSoundsEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    public double MinSoundsVolumePercent => AudioSettings.MinSoundsVolume * 100;
    public double MaxSoundsVolumePercent => AudioSettings.MaxSoundsVolume * 100;

    public double SoundsVolumePercent
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 70;

    public ObservableCollection<SoundPackItem> SoundPacks { get; } = new();

    public SoundPackItem? SelectedSoundPack
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Every interface sound, to listen to and to switch off one by one.
    /// </summary>
    public IReadOnlyList<SoundItemVM> Sounds { get; }

    /// <summary>
    /// The folder the user puts sound packs into.
    /// </summary>
    public string SoundPacksDirectory => _soundPackCatalog.Directory;

    /// <summary>
    /// Whether the voice gate would send what the microphone test hears right now.
    /// </summary>
    public bool IsVoiceDetected
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsTestRunning
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(TestButtonText));
        }
    }

    public string TestButtonText => IsTestRunning ? Strings.Get("settings.audio.test.stop") : Strings.Get("settings.audio.test.start");

    /// <summary>
    /// Peak level from 0 to 1 on a decibel scale.
    /// </summary>
    public double InputLevel
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsLoopbackEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? TestError
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> ToggleTestCommand { get; init; }
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    private readonly IAudioService _audioService;
    private readonly IMicrophoneTester _microphoneTester;
    private readonly ISoundService _soundService;
    private readonly ISoundPackCatalog _soundPackCatalog;
    private readonly CompositeDisposable _disposables = new();

    // The chosen device as saved. Saves read it instead of SelectedInputDevice, which the combo box
    // briefly resets to null while the device list is rebuilt.
    private (string Id, string Name)? _savedInputDevice;

    // The chosen pack as saved, for the same reason as the device
    private string? _savedSoundPack;

    private SoundEvent? _lastVolumePreview;

    // Set by a change of the pack, so that the save that follows lets the user hear it
    private bool _isSoundPackPreviewPending;

    // Set while the view model changes its own properties, so that only user changes get saved
    private bool _isUpdating = true;

    public AudioControlVM(IAudioService audioService, IMicrophoneTester microphoneTester, ISoundService soundService,
        ISoundPackCatalog soundPackCatalog)
    {
        _soundPackCatalog = soundPackCatalog;

        // Not disposed with the rest: a late toggle of an item would throw on it
        var soundToggles = new Subject<RxVoid>();
        Sounds = SoundDisplayOrder
            .Select(soundEvent => new SoundItemVM(soundEvent,
                Strings.Get($"settings.audio.sound.{SoundFiles.GetName(soundEvent)}"), soundService,
                () => soundToggles.OnNext(RxVoid.Default)))
            .ToArray();
        _soundService = soundService;
        _audioService = audioService;
        _microphoneTester = microphoneTester;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);
        ToggleTestCommand = ReactiveCommand.Create(ToggleTest, Observable.Return(IsMediaEnabled));

        ObserveSettingsChanges(soundToggles);
        if (IsMediaEnabled)
            ObserveMicrophoneTest();

        _audioService.InputDevicesChanged += OnInputDevicesChanged;
        _audioService.OutputDevicesChanged += OnOutputDevicesChanged;
        _microphoneTester.LevelChanged += OnInputLevelChanged;
        _microphoneTester.GateChanged += OnTestGateChanged;
        _microphoneTester.Failed += OnTestFailed;
    }

    private IObservable<RxVoid> WatchUserChanges<T>(string propertyName, Func<AudioControlVM, T> read) =>
        this.WatchProperty(propertyName, read)
            .Where(_ => !_isUpdating)
            .Select(_ => RxVoid.Default);

    private void ObserveSettingsChanges(IObservable<RxVoid> soundToggles)
    {
        var deviceChanges = this.WatchProperty(nameof(SelectedInputDevice), x => x.SelectedInputDevice)
            .Where(item => item != null && !_isUpdating)
            .Do(item => _savedInputDevice = item!.Device == null ? null : (item.Device.Id, item.Device.Name))
            .Select(_ => RxVoid.Default);

        var outputChanges = this.WatchProperty(nameof(SelectedOutputDevice), x => x.SelectedOutputDevice)
            .Where(item => item != null && !_isUpdating)
            .Do(item => _savedOutputDevice = item!.Device == null ? null : (item.Device.Id, item.Device.Name))
            .Select(_ => RxVoid.Default);

        var outputVolumeChanges = WatchUserChanges(nameof(OutputVolumePercent), x => x.OutputVolumePercent)
            .Throttle(SettingsSaveDelay);

        var volumeChanges = WatchUserChanges(nameof(InputVolumePercent), x => x.InputVolumePercent)
            .Throttle(SettingsSaveDelay);

        var gateChanges = WatchUserChanges(nameof(IsVoiceGateEnabled), x => x.IsVoiceGateEnabled);

        var voiceDetectionChanges = WatchUserChanges(nameof(IsVoiceDetectionEnabled), x => x.IsVoiceDetectionEnabled);

        var adaptiveThresholdChanges = WatchUserChanges(nameof(IsAdaptiveThresholdEnabled), x => x.IsAdaptiveThresholdEnabled);

        var thresholdChanges = WatchUserChanges(nameof(VoiceGateThresholdDb), x => x.VoiceGateThresholdDb)
            .Throttle(SettingsSaveDelay);

        var noiseSuppressionChanges = WatchUserChanges(nameof(NoiseSuppressionIndex), x => x.NoiseSuppressionIndex);

        var echoCancellationChanges = WatchUserChanges(nameof(IsEchoCancellationEnabled), x => x.IsEchoCancellationEnabled);

        var autoGainControlChanges = WatchUserChanges(nameof(IsAutoGainControlEnabled), x => x.IsAutoGainControlEnabled);

        var soundsEnabledChanges = WatchUserChanges(nameof(AreSoundsEnabled), x => x.AreSoundsEnabled);

        var soundsVolumeChanges = WatchUserChanges(nameof(SoundsVolumePercent), x => x.SoundsVolumePercent)
            .Throttle(SettingsSaveDelay);

        var soundPackChanges = this.WatchProperty(nameof(SelectedSoundPack), x => x.SelectedSoundPack)
            .Where(item => item != null && !_isUpdating && item.Name != _savedSoundPack)
            .Do(item =>
            {
                _savedSoundPack = item!.Name;
                _isSoundPackPreviewPending = true;
            })
            .Select(_ => RxVoid.Default);

        // Each save writes the whole tab, one save at a time
        _disposables.Add(Observable.Merge(deviceChanges, outputChanges, outputVolumeChanges, volumeChanges, noiseSuppressionChanges,
                echoCancellationChanges, autoGainControlChanges, gateChanges, voiceDetectionChanges,
                adaptiveThresholdChanges, thresholdChanges, soundsEnabledChanges, soundsVolumeChanges,
                soundPackChanges, soundToggles.Where(_ => !_isUpdating))
            .Select(_ => Observable.FromAsync(SaveAsync))
            .Concat()
            .Subscribe());

        // Lets the user hear the volume they stopped at, without waiting for the save
        _disposables.Add(soundsVolumeChanges
            .Subscribe(_ =>
            {
                _soundService.Volume = (float)(SoundsVolumePercent / 100);
                _soundService.Preview(NextVolumePreview());
            }));
    }

    private void ObserveMicrophoneTest()
    {
        // Device and processing changes restart the test immediately, without waiting for a save.
        var deviceChanges = WatchUserChanges(nameof(SelectedInputDevice), x => x.SelectedInputDevice)
            .Where(_ => SelectedInputDevice != null);
        _disposables.Add(Observable.Merge(deviceChanges,
                WatchUserChanges(nameof(NoiseSuppressionIndex), x => x.NoiseSuppressionIndex),
                WatchUserChanges(nameof(IsEchoCancellationEnabled), x => x.IsEchoCancellationEnabled),
                WatchUserChanges(nameof(IsAutoGainControlEnabled), x => x.IsAutoGainControlEnabled))
            .Where(_ => IsTestRunning)
            .Subscribe(_ => StartTest()));

        // The gate of the test decides as the one of a call, with the same options
        _disposables.Add(this.WatchProperties(nameof(IsVoiceGateEnabled), nameof(VoiceGateThresholdDb),
                nameof(IsVoiceDetectionEnabled), nameof(IsAdaptiveThresholdEnabled))
            .Subscribe(_ => _microphoneTester.SetVoiceGate(TestVoiceGate)));

        _disposables.Add(this.WatchProperty(nameof(InputVolumePercent), x => x.InputVolumePercent)
            .Subscribe(percent => _microphoneTester.SetVolume(percent / 100)));

        _disposables.Add(this.WatchProperty(nameof(IsLoopbackEnabled), x => x.IsLoopbackEnabled)
            .Subscribe(_microphoneTester.SetLoopbackEnabled));

        _disposables.Add(this.WatchProperties(nameof(IsVoiceGateEnabled), nameof(IsTestRunning))
            .Subscribe(_ => ResetVoiceDetected()));
    }

    /// <summary>
    /// A random sound to try the volume on, never the one that was played just before.
    /// </summary>
    private SoundEvent NextVolumePreview()
    {
        var events = Enum.GetValues<SoundEvent>();
        SoundEvent next;
        do
        {
            next = events[Random.Shared.Next(events.Length)];
        } while (next == _lastVolumePreview);

        _lastVolumePreview = next;
        return next;
    }

    private async Task SaveAsync()
    {
        var settings = ToSettings();
        if (!IsMediaEnabled)
        {
            // Interface sounds remain editable without changing the unavailable microphone settings.
            var saved = await _audioService.GetSettingsAsync();
            CopySoundSettings(settings, saved);
            settings = saved;
        }
        await _audioService.SaveSettingsAsync(settings);

        if (!_isSoundPackPreviewPending)
            return;

        _isSoundPackPreviewPending = false;
        _soundService.Preview(SoundEvent.Message);
    }

    /// <summary>
    /// Looks the installed packs up again, for the ones the user added while the settings are open.
    /// </summary>
    public void RefreshSoundPacks()
    {
        var packs = _soundPackCatalog.GetPacks();

        // Rebuilding resets the selection of the combo box, so an unchanged list is left alone
        if (SoundPacks.Count > 0 && SoundPacks.Skip(1).Select(item => item.Name).SequenceEqual(packs))
            return;

        var wasUpdating = _isUpdating;
        _isUpdating = true;

        SoundPacks.Clear();
        SoundPacks.Add(new SoundPackItem(null, Strings.Get("settings.audio.soundPack.builtIn")));

        foreach (var pack in packs)
            SoundPacks.Add(new SoundPackItem(pack, pack));

        // A pack that was removed plays the built-in sounds, which is what the first item is
        SelectedSoundPack = SoundPacks.FirstOrDefault(item => item.Name == _savedSoundPack) ?? SoundPacks[0];

        _isUpdating = wasUpdating;
    }

    private void ToggleTest()
    {
        if (IsTestRunning)
        {
            _microphoneTester.Stop();
            IsTestRunning = false;
            InputLevel = 0;
            return;
        }

        StartTest();
    }

    private void StartTest()
    {
        if (!IsMediaEnabled)
            return;

        TestError = DescribeTestError(_microphoneTester.Start(SelectedInputDevice?.Device?.Id,
            InputVolumePercent / 100, IsLoopbackEnabled, NoiseSuppression, TestVoiceGate, IsEchoCancellationEnabled,
            IsAutoGainControlEnabled));
        IsTestRunning = TestError == null;
        InputLevel = 0;
    }

    private static string? DescribeTestError(MicrophoneTestError? error) => error switch
    {
        null => null,
        MicrophoneTestError.DeviceNotConnected => Strings.Get("settings.audio.test.notConnected"),
        MicrophoneTestError.DeviceLinkFailed => Strings.Get("settings.audio.test.linkFailed"),
        MicrophoneTestError.DeviceOpenFailed => Strings.Get("settings.audio.test.openFailed"),
        _ => Strings.Get("settings.audio.test.unexpected")
    };

    private void OnInputLevelChanged(double level)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsTestRunning)
                return;

            InputLevel = level;
        });
    }

    private void OnTestGateChanged(bool isOpen)
    {
        Dispatcher.UIThread.Post(() => IsVoiceDetected = isOpen && IsVoiceGateEnabled && IsTestRunning);
    }

    // Desktop calls and the test use the same GStreamer microphone processing.
    private VoiceGateOptions? TestVoiceGate => IsVoiceGateEnabled
        ? new VoiceGateOptions(VoiceGateThresholdDb, IsVoiceDetectionEnabled, IsAdaptiveThresholdEnabled)
        : null;

    private void ResetVoiceDetected()
    {
        IsVoiceDetected = false;
    }

    private void OnTestFailed(string error)
    {
        var devices = _audioService.GetInputDevices();

        Dispatcher.UIThread.Post(() =>
        {
            var deviceId = SelectedInputDevice?.Device?.Id;

            // Backends report an unplugged device in their own words, e.g. "target not found"
            var isDisconnected = deviceId != null && devices.All(d => d.Id != deviceId);

            IsTestRunning = false;
            InputLevel = 0;
            TestError = isDisconnected
                ? Strings.Get("settings.audio.test.disconnected")
                : Strings.Format("settings.audio.test.failed", ("reason", error));
        });
    }

    private async Task LoadAsync()
    {
        _isUpdating = true;

        var settings = await _audioService.GetSettingsAsync();
        Apply(settings);
        RefreshSoundPacks();

        // The first call starts the device monitor, which may take a while to reach the sound server
        var devices = IsMediaEnabled ? await Task.Run(_audioService.GetInputDevices) : [];
        RebuildInputDevices(devices);
        RebuildOutputDevices(IsMediaEnabled ? await Task.Run(_audioService.GetOutputDevices) : []);

        _isUpdating = false;
    }

    private void Apply(AudioSettings settings)
    {
        _savedInputDevice = settings.InputDeviceId == null
            ? null
            : (settings.InputDeviceId, settings.InputDeviceName ?? settings.InputDeviceId);
        _savedOutputDevice = settings.OutputDeviceId == null ? null
            : (settings.OutputDeviceId, settings.OutputDeviceName ?? settings.OutputDeviceId);
        OutputVolumePercent = settings.OutputVolume * 100;
        InputVolumePercent = settings.InputVolume * 100;
        IsVoiceGateEnabled = settings.IsVoiceGateEnabled;
        VoiceGateThresholdDb = settings.VoiceGateThresholdDb;
        IsVoiceDetectionEnabled = settings.IsVoiceDetectionEnabled;
        IsAdaptiveThresholdEnabled = settings.IsAdaptiveThresholdEnabled;
        NoiseSuppressionIndex = (int)settings.NoiseSuppression;
        IsEchoCancellationEnabled = settings.IsEchoCancellationEnabled;
        IsAutoGainControlEnabled = settings.IsAutoGainControlEnabled;
        AreSoundsEnabled = settings.AreSoundsEnabled;
        SoundsVolumePercent = settings.SoundsVolume * 100;
        _savedSoundPack = settings.SoundPack;
        foreach (var sound in Sounds)
            sound.IsEnabled = !settings.DisabledSounds.Contains(SoundFiles.GetName(sound.Event));
    }

    private void OnInputDevicesChanged()
    {
        var devices = _audioService.GetInputDevices();

        Dispatcher.UIThread.Post(() =>
        {
            var wasUpdating = _isUpdating;
            _isUpdating = true;

            RebuildInputDevices(devices);

            _isUpdating = wasUpdating;
        });
    }

    private void RebuildInputDevices(IReadOnlyList<AudioDeviceInfo> devices)
    {
        var defaultDevice = devices.FirstOrDefault(d => d.IsDefault);

        InputDevices.Clear();

        var systemDefault = new AudioInputDeviceItem(null,
            defaultDevice == null
                ? Strings.Get("settings.audio.device.systemDefault")
                : Strings.Format("settings.audio.device.systemDefaultNamed", ("device", defaultDevice.Name)), true);
        InputDevices.Add(systemDefault);

        foreach (var device in devices)
        {
            InputDevices.Add(new AudioInputDeviceItem(device, device.Name, true));
        }

        if (_savedInputDevice == null)
        {
            SelectedInputDevice = systemDefault;
            return;
        }

        var (savedId, savedName) = _savedInputDevice.Value;
        var selected = InputDevices.FirstOrDefault(i => i.Device?.Id == savedId)
                       ?? InputDevices.FirstOrDefault(i => i.Device?.Name == savedName);

        if (selected == null)
        {
            // Keep the choice visible while the device is unplugged instead of silently switching
            // to another one
            selected = new AudioInputDeviceItem(new AudioDeviceInfo(savedId, savedName, false),
                Strings.Format("settings.audio.device.disconnected", ("device", savedName)), false);
            InputDevices.Add(selected);
        }

        SelectedInputDevice = selected;
    }

    private void OnOutputDevicesChanged()
    {
        var devices = _audioService.GetOutputDevices();

        Dispatcher.UIThread.Post(() =>
        {
            var wasUpdating = _isUpdating;
            _isUpdating = true;

            RebuildOutputDevices(devices);

            _isUpdating = wasUpdating;
        });
    }

    private void RebuildOutputDevices(IReadOnlyList<AudioDeviceInfo> devices)
    {
        var defaultDevice = devices.FirstOrDefault(d => d.IsDefault);

        OutputDevices.Clear();

        var systemDefault = new AudioInputDeviceItem(null,
            defaultDevice == null
                ? Strings.Get("settings.audio.device.systemDefault")
                : Strings.Format("settings.audio.device.systemDefaultNamed", ("device", defaultDevice.Name)), true);
        OutputDevices.Add(systemDefault);

        foreach (var device in devices)
        {
            OutputDevices.Add(new AudioInputDeviceItem(device, device.Name, true));
        }

        if (_savedOutputDevice == null)
        {
            SelectedOutputDevice = systemDefault;
            return;
        }

        var (savedId, savedName) = _savedOutputDevice.Value;
        var selected = OutputDevices.FirstOrDefault(i => i.Device?.Id == savedId)
                       ?? OutputDevices.FirstOrDefault(i => i.Device?.Name == savedName);

        if (selected == null)
        {
            // Keep the choice visible while the device is unplugged instead of silently switching
            // to another one
            selected = new AudioInputDeviceItem(new AudioDeviceInfo(savedId, savedName, false),
                Strings.Format("settings.audio.device.disconnected", ("device", savedName)), false);
            OutputDevices.Add(selected);
        }

        SelectedOutputDevice = selected;
    }

    private NoiseSuppressionLevel NoiseSuppression => (NoiseSuppressionLevel)NoiseSuppressionIndex;

    private AudioSettings ToSettings() => new()
    {
        OutputVolume = OutputVolumePercent / 100,
        OutputDeviceId = _savedOutputDevice?.Id,
        OutputDeviceName = _savedOutputDevice?.Name,
        InputDeviceId = _savedInputDevice?.Id,
        InputDeviceName = _savedInputDevice?.Name,
        InputVolume = InputVolumePercent / 100,
        IsVoiceGateEnabled = IsVoiceGateEnabled,
        VoiceGateThresholdDb = VoiceGateThresholdDb,
        IsVoiceDetectionEnabled = IsVoiceDetectionEnabled,
        IsAdaptiveThresholdEnabled = IsAdaptiveThresholdEnabled,
        NoiseSuppression = NoiseSuppression,
        IsEchoCancellationEnabled = IsEchoCancellationEnabled,
        IsAutoGainControlEnabled = IsAutoGainControlEnabled,
        AreSoundsEnabled = AreSoundsEnabled,
        SoundsVolume = SoundsVolumePercent / 100,
        SoundPack = _savedSoundPack,
        DisabledSounds = Sounds.Where(sound => !sound.IsEnabled).Select(sound => SoundFiles.GetName(sound.Event)).ToArray()
    };

    public async Task ResetAllAsync()
    {
        var defaults = new AudioSettings();
        if (!IsMediaEnabled)
        {
            var saved = await _audioService.GetSettingsAsync();
            CopySoundSettings(defaults, saved);
            defaults = saved;
        }
        await _audioService.SaveSettingsAsync(defaults);
        await LoadAsync();
    }

    private static void CopySoundSettings(AudioSettings source, AudioSettings target)
    {
        target.AreSoundsEnabled = source.AreSoundsEnabled;
        target.SoundsVolume = source.SoundsVolume;
        target.SoundPack = source.SoundPack;
        target.DisabledSounds = source.DisabledSounds;
    }

    public void Dispose()
    {
        _microphoneTester.LevelChanged -= OnInputLevelChanged;
        _microphoneTester.GateChanged -= OnTestGateChanged;
        _microphoneTester.Failed -= OnTestFailed;
        _microphoneTester.Stop();

        _audioService.InputDevicesChanged -= OnInputDevicesChanged;
        _audioService.OutputDevicesChanged -= OnOutputDevicesChanged;
        _disposables.Dispose();
    }
}

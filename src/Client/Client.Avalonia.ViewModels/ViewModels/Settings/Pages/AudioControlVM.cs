using Client.Avalonia.Localization;
using Client.Services;
using Client.Application.Settings.Search;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using Client.Core.Media;
using Client.Core.Settings;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Application.Sounds;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public class AudioInputDeviceItem
{
    /// <summary>
    /// Null for the system default device.
    /// </summary>
    public AudioDeviceInfo? Device { get; }
    public string Name { get; }
    public bool IsAvailable { get; }

    public AudioInputDeviceItem(AudioDeviceInfo? device, string name, bool isAvailable)
    {
        Device = device;
        Name = name;
        IsAvailable = isAvailable;
    }
}

public class SoundPackItem
{
    /// <summary>
    /// Null for the built-in sounds.
    /// </summary>
    public string? Name { get; }
    public string Title { get; }

    public SoundPackItem(string? name, string title)
    {
        Name = name;
        Title = title;
    }
}

public class SoundItemVM : ReactiveObject
{
    public SoundEvent Event { get; }
    public string Title { get; }
    public ReactiveCommand<RxVoid, RxVoid> PlayCommand { get; }

    public bool IsEnabled
    {
        get;
        set
        {
            if (field == value)
                return;

            this.RaiseAndSetIfChanged(ref field, value);
            _changed();
        }
    } = true;

    private readonly Action _changed;

    public SoundItemVM(SoundEvent soundEvent, string title, ISoundService soundService, Action changed)
    {
        Event = soundEvent;
        Title = title;
        _changed = changed;
        PlayCommand = ReactiveCommand.Create(() => soundService.Preview(soundEvent));
    }
}

public class AudioControlVM : ReactiveObject, ISettingVM, IDisposable
{
    public string Name => SettingText.TabName("audio");
    public SettingTabType Type => SettingTabType.Audio;
    public bool IsEnabled => true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("audio",
        ("microphone", ["inputDevice", "inputVolume", "noiseSuppression", "echoCancellation"]),
        ("voiceActivity", ["voiceGate", "voiceDetection", "adaptiveThreshold"]),
        ("sounds", ["soundsEnabled", "soundsVolume", "soundPack", "soundEvents"]),
        ("microphoneTest", ["testMicrophone", "inputLevel", "hearMyself"]));

    public ObservableCollection<AudioInputDeviceItem> InputDevices { get; } = new();
    
    public AudioInputDeviceItem? SelectedInputDevice
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public double MinInputVolumePercent => AudioSettings.MinInputVolume * 100;
    public double MaxInputVolumePercent => AudioSettings.MaxInputVolume * 100;
    
    public double InputVolumePercent
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 100;
    
    // Index into the noise suppression choices of the view, in the order of NoiseSuppressionLevel
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
        Sounds = Enum.GetValues<SoundEvent>()
            .Select(soundEvent => new SoundItemVM(soundEvent,
                Strings.Get($"settings.audio.sound.{SoundFiles.GetName(soundEvent)}"), soundService,
                () => soundToggles.OnNext(RxVoid.Default)))
            .ToArray();
        _soundService = soundService;
        _audioService = audioService;
        _microphoneTester = microphoneTester;
        
        LoadCommand = ReactiveCommand
            .CreateFromTask(LoadAsync);
        
        ToggleTestCommand = ReactiveCommand
            .Create(ToggleTest);

        var deviceChanges = this.WatchProperty(nameof(SelectedInputDevice), x => x.SelectedInputDevice)
            .Where(item => item != null && !_isUpdating)
            .Do(item => _savedInputDevice = item!.Device == null ? null : (item.Device.Id, item.Device.Name))
            .Select(_ => RxVoid.Default);

        var volumeChanges = this.WatchProperty(nameof(InputVolumePercent), x => x.InputVolumePercent)
            .Where(_ => !_isUpdating)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Select(_ => RxVoid.Default);

        var gateChanges = this.WatchProperty(nameof(IsVoiceGateEnabled), x => x.IsVoiceGateEnabled)
            .Where(_ => !_isUpdating)
            .Select(_ => RxVoid.Default);

        var voiceDetectionChanges = this.WatchProperty(nameof(IsVoiceDetectionEnabled), x => x.IsVoiceDetectionEnabled)
            .Where(_ => !_isUpdating)
            .Select(_ => RxVoid.Default);

        var adaptiveThresholdChanges = this.WatchProperty(nameof(IsAdaptiveThresholdEnabled), x => x.IsAdaptiveThresholdEnabled)
            .Where(_ => !_isUpdating)
            .Select(_ => RxVoid.Default);

        var thresholdChanges = this.WatchProperty(nameof(VoiceGateThresholdDb), x => x.VoiceGateThresholdDb)
            .Where(_ => !_isUpdating)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Select(_ => RxVoid.Default);

        var noiseSuppressionChanges = this.WatchProperty(nameof(NoiseSuppressionIndex), x => x.NoiseSuppressionIndex)
            .Where(_ => !_isUpdating)
            .Select(_ => RxVoid.Default);

        var echoCancellationChanges = this.WatchProperty(nameof(IsEchoCancellationEnabled), x => x.IsEchoCancellationEnabled)
            .Where(_ => !_isUpdating)
            .Select(_ => RxVoid.Default);

        var soundsEnabledChanges = this.WatchProperty(nameof(AreSoundsEnabled), x => x.AreSoundsEnabled)
            .Where(_ => !_isUpdating)
            .Select(_ => RxVoid.Default);

        var soundsVolumeChanges = this.WatchProperty(nameof(SoundsVolumePercent), x => x.SoundsVolumePercent)
            .Where(_ => !_isUpdating)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Select(_ => RxVoid.Default);

        var soundPackChanges = this.WatchProperty(nameof(SelectedSoundPack), x => x.SelectedSoundPack)
            .Where(item => item != null && !_isUpdating && item.Name != _savedSoundPack)
            .Do(item =>
            {
                _savedSoundPack = item!.Name;
                _isSoundPackPreviewPending = true;
            })
            .Select(_ => RxVoid.Default);

        // Each save writes the whole tab, one save at a time
        _disposables.Add(deviceChanges
            .Merge(volumeChanges)
            .Merge(noiseSuppressionChanges)
            .Merge(echoCancellationChanges)
            .Merge(gateChanges)
            .Merge(voiceDetectionChanges)
            .Merge(adaptiveThresholdChanges)
            .Merge(thresholdChanges)
            .Merge(soundsEnabledChanges)
            .Merge(soundsVolumeChanges)
            .Merge(soundPackChanges)
            .Merge(soundToggles.Where(_ => !_isUpdating))
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

        // The test follows the settings right away, unlike saving
        _disposables.Add(this.WatchProperty(nameof(SelectedInputDevice), x => x.SelectedInputDevice)
            .Where(item => item != null && !_isUpdating && IsTestRunning)
            .Subscribe(_ => StartTest()));

        _disposables.Add(this.WatchProperty(nameof(NoiseSuppressionIndex), x => x.NoiseSuppressionIndex)
            .Where(_ => !_isUpdating && IsTestRunning)
            .Subscribe(_ => StartTest()));

        _disposables.Add(this.WatchProperty(nameof(IsEchoCancellationEnabled), x => x.IsEchoCancellationEnabled)
            .Where(_ => !_isUpdating && IsTestRunning)
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
        
        _audioService.InputDevicesChanged += OnInputDevicesChanged;
        _microphoneTester.LevelChanged += OnInputLevelChanged;
        _microphoneTester.GateChanged += OnTestGateChanged;
        _microphoneTester.Failed += OnTestFailed;
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
        await _audioService.SaveSettingsAsync(ToSettings());

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
        TestError = DescribeTestError(_microphoneTester.Start(SelectedInputDevice?.Device?.Id,
            InputVolumePercent / 100, IsLoopbackEnabled, NoiseSuppression, TestVoiceGate, IsEchoCancellationEnabled));
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
        _savedInputDevice = settings.InputDeviceId == null 
            ? null 
            : (settings.InputDeviceId, settings.InputDeviceName ?? settings.InputDeviceId);
        InputVolumePercent = settings.InputVolume * 100;
        IsVoiceGateEnabled = settings.IsVoiceGateEnabled;
        VoiceGateThresholdDb = settings.VoiceGateThresholdDb;
        IsVoiceDetectionEnabled = settings.IsVoiceDetectionEnabled;
        IsAdaptiveThresholdEnabled = settings.IsAdaptiveThresholdEnabled;
        NoiseSuppressionIndex = (int)settings.NoiseSuppression;
        IsEchoCancellationEnabled = settings.IsEchoCancellationEnabled;
        AreSoundsEnabled = settings.AreSoundsEnabled;
        SoundsVolumePercent = settings.SoundsVolume * 100;
        _savedSoundPack = settings.SoundPack;
        foreach (var sound in Sounds)
            sound.IsEnabled = !settings.DisabledSounds.Contains(SoundFiles.GetName(sound.Event));
        RefreshSoundPacks();
        
        // The first call starts the device monitor, which may take a while to reach the sound server
        var devices = await Task.Run(_audioService.GetInputDevices);
        RebuildInputDevices(devices);
        
        _isUpdating = false;
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

    private NoiseSuppressionLevel NoiseSuppression => (NoiseSuppressionLevel)NoiseSuppressionIndex;

    private AudioSettings ToSettings() => new()
    {
        InputDeviceId = _savedInputDevice?.Id,
        InputDeviceName = _savedInputDevice?.Name,
        InputVolume = InputVolumePercent / 100,
        IsVoiceGateEnabled = IsVoiceGateEnabled,
        VoiceGateThresholdDb = VoiceGateThresholdDb,
        IsVoiceDetectionEnabled = IsVoiceDetectionEnabled,
        IsAdaptiveThresholdEnabled = IsAdaptiveThresholdEnabled,
        NoiseSuppression = NoiseSuppression,
        IsEchoCancellationEnabled = IsEchoCancellationEnabled,
        AreSoundsEnabled = AreSoundsEnabled,
        SoundsVolume = SoundsVolumePercent / 100,
        SoundPack = _savedSoundPack,
        DisabledSounds = Sounds.Where(sound => !sound.IsEnabled).Select(sound => SoundFiles.GetName(sound.Event)).ToArray()
    };
    
    public async Task ResetAllAsync()
    {
        await _audioService.SaveSettingsAsync(new AudioSettings());
        await LoadAsync();
    }

    public void Dispose()
    {
        _microphoneTester.LevelChanged -= OnInputLevelChanged;
        _microphoneTester.GateChanged -= OnTestGateChanged;
        _microphoneTester.Failed -= OnTestFailed;
        _microphoneTester.Stop();
        
        _audioService.InputDevicesChanged -= OnInputDevicesChanged;
        _disposables.Dispose();
    }
}

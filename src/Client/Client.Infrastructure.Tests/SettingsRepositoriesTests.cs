using Xunit;
using Client.Core.Coordinator;
using Client.Core.Media;
using Client.Core.Settings;
using Client.Core.Settings.Themes;
using Client.Infrastructure.Database.Repositories;

namespace Client.Infrastructure.Tests;

public class SettingsRepositoriesTests
{
    [Fact]
    public async Task StorageSettingsStartWithTheDefaultsAndRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new StorageSettingsRepository(database.Database);

        var defaults = await repository.GetAsync();
        Assert.True(defaults.IsChatCacheEnabled);
        Assert.Equal(2000, defaults.ChatCacheMessagesPerChat);
        Assert.Equal(512, defaults.MediaCacheLimitMegabytes);

        await repository.SaveAsync(new StorageSettings
        {
            IsChatCacheEnabled = false, ChatCacheMessagesPerChat = 500, MediaCacheLimitMegabytes = 4096
        });

        var saved = await repository.GetAsync();
        Assert.False(saved.IsChatCacheEnabled);
        Assert.Equal(500, saved.ChatCacheMessagesPerChat);
        Assert.Equal(4096, saved.MediaCacheLimitMegabytes);
    }

    [Fact]
    public async Task HotKeysStartWithNothingAssignedAndRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new HotKeysSettingsRepository(database.Database);

        var defaults = await repository.GetAsync();
        Assert.True(defaults.IsEnabled);
        Assert.True(defaults.IsGlobalEnabled);
        Assert.All(HotKeysSettings.Actions, action => Assert.Null(defaults.Get(action).Gesture));

        await repository.SaveAsync(new HotKeysSettings { IsEnabled = false, IsGlobalEnabled = false }
            .With(new HotKeyBinding(HotKeyAction.ToggleMicrophone, "M", IsGlobal: false))
            .With(new HotKeyBinding(HotKeyAction.ToggleSound, "Ctrl+Shift+D")));

        var saved = await repository.GetAsync();
        Assert.False(saved.IsEnabled);
        Assert.False(saved.IsGlobalEnabled);
        Assert.Equal(new HotKeyBinding(HotKeyAction.ToggleMicrophone, "M", false), saved.Get(HotKeyAction.ToggleMicrophone));
        Assert.Equal(new HotKeyBinding(HotKeyAction.ToggleSound, "Ctrl+Shift+D", true), saved.Get(HotKeyAction.ToggleSound));

        // A cleared key stays cleared
        await repository.SaveAsync(saved.With(new HotKeyBinding(HotKeyAction.ToggleMicrophone, null)));
        Assert.Null((await repository.GetAsync()).Get(HotKeyAction.ToggleMicrophone).Gesture);
    }

    [Fact]
    public async Task AudioSettingsStartWithTheDefaults()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();

        var settings = await new AudioSettingsRepository(database.Database).GetAsync();

        Assert.Null(settings.InputDeviceId);
        Assert.Null(settings.InputDeviceName);
        Assert.Equal(1.0, settings.InputVolume);
        Assert.True(settings.IsVoiceGateEnabled);
        Assert.Equal(-32.0, settings.VoiceGateThresholdDb);
        Assert.False(settings.IsVoiceDetectionEnabled);
        Assert.False(settings.IsAdaptiveThresholdEnabled);
        Assert.Equal(NoiseSuppressionLevel.Moderate, settings.NoiseSuppression);
        Assert.True(settings.IsEchoCancellationEnabled);
        Assert.True(settings.AreSoundsEnabled);
        Assert.Equal(0.7, settings.SoundsVolume);
        Assert.Null(settings.SoundPack);
        Assert.Empty(settings.DisabledSounds);
    }

    [Fact]
    public async Task AudioSettingsRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AudioSettingsRepository(database.Database);

        await repository.SaveAsync(new AudioSettings
        {
            InputDeviceId = "device", InputDeviceName = "Microphone", InputVolume = 1.5,
            IsVoiceGateEnabled = false, VoiceGateThresholdDb = -32.5,
            IsVoiceDetectionEnabled = true, IsAdaptiveThresholdEnabled = true,
            NoiseSuppression = NoiseSuppressionLevel.NeuralAndWebRtc, IsEchoCancellationEnabled = false,
            AreSoundsEnabled = false, SoundsVolume = 0.35, SoundPack = "Retro",
            DisabledSounds = ["join", "message_sent"]
        });
        var settings = await repository.GetAsync();

        Assert.Equal("device", settings.InputDeviceId);
        Assert.Equal("Microphone", settings.InputDeviceName);
        Assert.Equal(1.5, settings.InputVolume);
        Assert.False(settings.IsVoiceGateEnabled);
        Assert.Equal(-32.5, settings.VoiceGateThresholdDb);
        Assert.True(settings.IsVoiceDetectionEnabled);
        Assert.True(settings.IsAdaptiveThresholdEnabled);
        Assert.Equal(NoiseSuppressionLevel.NeuralAndWebRtc, settings.NoiseSuppression);
        Assert.False(settings.IsEchoCancellationEnabled);
        Assert.False(settings.AreSoundsEnabled);
        Assert.Equal(0.35, settings.SoundsVolume);
        Assert.Equal("Retro", settings.SoundPack);
        Assert.Equal(["join", "message_sent"], settings.DisabledSounds);
    }

    [Fact]
    public async Task AudioSettingsDropTheNameOfTheDefaultDevice()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AudioSettingsRepository(database.Database);

        await repository.SaveAsync(new AudioSettings { InputDeviceId = null, InputDeviceName = "Stale" });

        Assert.Null((await repository.GetAsync()).InputDeviceName);
    }

    [Fact]
    public async Task AudioSettingsAreRecreatedWhenTheRowIsMissing()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("DELETE FROM AudioSettings");
        var repository = new AudioSettingsRepository(database.Database);

        Assert.Equal(1.0, (await repository.GetAsync()).InputVolume);

        await repository.SaveAsync(new AudioSettings { InputVolume = 0.5 });

        Assert.Equal(0.5, (await repository.GetAsync()).InputVolume);
    }

    [Fact]
    public async Task ScreenShareSettingsStartWithTheDefaults()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();

        var settings = await new ScreenShareSettingsRepository(database.Database).GetAsync();

        Assert.Equal(1080, settings.MaxHeight);
        Assert.Equal(30, settings.Framerate);
        Assert.Null(settings.BitrateKbps);
    }

    [Fact]
    public async Task ScreenShareSettingsRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ScreenShareSettingsRepository(database.Database);

        await repository.SaveAsync(new ScreenShareSettings { MaxHeight = 720, Framerate = 60, BitrateKbps = 90000 });
        var settings = await repository.GetAsync();

        Assert.Equal(720, settings.MaxHeight);
        Assert.Equal(60, settings.Framerate);
        Assert.Equal(90000, settings.BitrateKbps);

        await repository.SaveAsync(settings with { BitrateKbps = null });

        Assert.Null((await repository.GetAsync()).BitrateKbps);
    }

    [Fact]
    public async Task ScreenShareSettingsReplaceAValueThatCannotBeChosen()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("UPDATE ScreenShareSettings SET MaxHeight = 4320, Framerate = 60, BitrateKbps = -5");

        var settings = await new ScreenShareSettingsRepository(database.Database).GetAsync();

        Assert.Equal(1080, settings.MaxHeight);
        Assert.Equal(60, settings.Framerate);
        Assert.Null(settings.BitrateKbps);
    }

    [Fact]
    public async Task CollapsedSectionsRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new CollapsedSectionsRepository(database.Database);

        // The list of single sounds is the only thing that starts folded
        Assert.Equal(["audio.soundEvents"], await repository.GetAsync());

        await repository.SetAsync("audio.soundEvents", false);
        await repository.SetAsync("audio.microphone", true);
        await repository.SetAsync("audio.microphone", true);
        await repository.SetAsync("behavior.tray", true);
        await repository.SetAsync("behavior.tray", false);

        Assert.Equal(["audio.microphone"], await repository.GetAsync());
    }

    [Fact]
    public async Task VideoSettingsRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new VideoSettingsRepository(database.Database);

        Assert.Equal(new VideoSettings(), await repository.GetAsync());

        var changed = new VideoSettings
        {
            IsHardwareDecodingEnabled = false,
            Decoder = "vah264dec",
            Encoder = "openh264enc",
            IsZeroCopyEnabled = false,
            IsTimedPresentationEnabled = false,
            PresentationBufferFrames = 4,
            ShowFrameStats = true
        };
        await repository.SaveAsync(changed);

        Assert.Equal(changed, await repository.GetAsync());

        // A buffer that cannot be chosen any more reads as the default one
        await repository.SaveAsync(changed with { PresentationBufferFrames = 9, Decoder = " " });
        var normalized = await repository.GetAsync();

        Assert.Equal(new VideoSettings().PresentationBufferFrames, normalized.PresentationBufferFrames);
        Assert.Null(normalized.Decoder);
    }

    [Fact]
    public async Task DismissedPromptsRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new DismissedPromptsRepository(database.Database);

        Assert.False(await repository.IsDismissedAsync("chat.openLink"));

        await repository.SetAsync("chat.openLink", true);
        await repository.SetAsync("chat.openLink", true);

        Assert.True(await repository.IsDismissedAsync("chat.openLink"));
        Assert.False(await repository.IsDismissedAsync("chat.other"));

        await repository.SetAsync("chat.openLink", false);

        Assert.False(await repository.IsDismissedAsync("chat.openLink"));
    }

    [Fact]
    public async Task CoordinatorStateKeepsTheMicrophoneApartFromTheLastSession()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new CoordinatorStateRepository(database.Database);

        Assert.False(await repository.IsMicrophoneMutedAsync("coordinator-a"));
        Assert.Null(await repository.GetLastSessionAsync("coordinator-a"));

        await repository.SetMicrophoneMutedAsync("coordinator-a", true);
        await repository.SetLastSessionAsync("coordinator-a", new LastSession(7, false, 3));
        await repository.SetLastSessionAsync("coordinator-b", new LastSession(null, true, 9));

        Assert.True(await repository.IsMicrophoneMutedAsync("coordinator-a"));
        Assert.False(await repository.IsMicrophoneMutedAsync("coordinator-b"));
        Assert.Equal(new LastSession(7, false, 3), await repository.GetLastSessionAsync("coordinator-a"));
        Assert.Equal(new LastSession(null, true, 9), await repository.GetLastSessionAsync("coordinator-b"));

        // Forgetting where the user was leaves the microphone as it is
        await repository.SetLastSessionAsync("coordinator-a", null);

        Assert.Null(await repository.GetLastSessionAsync("coordinator-a"));
        Assert.True(await repository.IsMicrophoneMutedAsync("coordinator-a"));

        await repository.SetMicrophoneMutedAsync("coordinator-a", false);

        Assert.False(await repository.IsMicrophoneMutedAsync("coordinator-a"));

        // The sound is remembered apart from the microphone
        Assert.False(await repository.IsSoundMutedAsync("coordinator-a"));

        await repository.SetSoundMutedAsync("coordinator-a", true);

        Assert.True(await repository.IsSoundMutedAsync("coordinator-a"));
        Assert.False(await repository.IsSoundMutedAsync("coordinator-b"));
        Assert.False(await repository.IsMicrophoneMutedAsync("coordinator-a"));

        await repository.SetSoundMutedAsync("coordinator-a", false);

        Assert.False(await repository.IsSoundMutedAsync("coordinator-a"));

        // Being away is remembered with its reason, which goes once the user is back
        Assert.Equal((false, null), await repository.GetAfkAsync("coordinator-a"));

        await repository.SetAfkAsync("coordinator-a", true, "lunch");

        Assert.Equal((true, "lunch"), await repository.GetAfkAsync("coordinator-a"));
        Assert.Equal((false, null), await repository.GetAfkAsync("coordinator-b"));

        await repository.SetAfkAsync("coordinator-a", false, "lunch");

        Assert.Equal((false, null), await repository.GetAfkAsync("coordinator-a"));
    }

    [Fact]
    public async Task MutedChatsRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new MutedChatsRepository(database.Database);

        Assert.Empty(await repository.GetAsync("coordinator-a"));

        await repository.SetAsync("coordinator-a", 7, true);
        await repository.SetAsync("coordinator-a", 7, true);
        await repository.SetAsync("coordinator-a", 3, true);
        await repository.SetAsync("coordinator-a", 5, true);
        await repository.SetAsync("coordinator-a", 5, false);
        // Chat ids are only unique within a coordinator
        await repository.SetAsync("coordinator-b", 9, true);

        Assert.Equal([3, 7], await repository.GetAsync("coordinator-a"));
        Assert.Equal([9], await repository.GetAsync("coordinator-b"));
    }

    [Fact]
    public async Task BehaviorSettingsStartWithTheDefaults()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();

        var settings = await new BehaviorSettingsRepository(database.Database).GetAsync();

        Assert.False(settings.IsAutoConnectEnabled);
        Assert.False(settings.IsFallbackConnectionEnabled);
        Assert.True(settings.IsFallbackConnectionWarningEnabled);
        Assert.True(settings.IsPropagatedConnectionWarningEnabled);
        Assert.Equal(CloseButtonAction.Ask, settings.CloseButtonAction);
        Assert.True(settings.IsUpdateCheckOnStartupEnabled);
        Assert.Equal(UpdateChannel.Release, settings.UpdateChannel);
    }

    [Fact]
    public async Task BehaviorSettingsRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new BehaviorSettingsRepository(database.Database);

        await repository.SaveAsync(new BehaviorSettings
        {
            IsAutoConnectEnabled = true,
            IsFallbackConnectionEnabled = true,
            IsFallbackConnectionWarningEnabled = false,
            IsPropagatedConnectionWarningEnabled = false,
            CloseButtonAction = CloseButtonAction.MinimizeToTray,
            IsUpdateCheckOnStartupEnabled = false,
            UpdateChannel = UpdateChannel.Dev
        });
        var settings = await repository.GetAsync();

        Assert.True(settings.IsAutoConnectEnabled);
        Assert.True(settings.IsFallbackConnectionEnabled);
        Assert.False(settings.IsFallbackConnectionWarningEnabled);
        Assert.False(settings.IsPropagatedConnectionWarningEnabled);
        Assert.Equal(CloseButtonAction.MinimizeToTray, settings.CloseButtonAction);
        Assert.False(settings.IsUpdateCheckOnStartupEnabled);
        Assert.Equal(UpdateChannel.Dev, settings.UpdateChannel);
    }

    [Fact]
    public async Task AutoConnectInstanceIsKeptApartFromTheSettings()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new BehaviorSettingsRepository(database.Database);
        await repository.SaveAsync(new BehaviorSettings { CloseButtonAction = CloseButtonAction.Exit });

        Assert.Null(await repository.GetAutoConnectInstanceAsync());

        await repository.SetAutoConnectInstanceAsync(("coordinator", 7));

        Assert.Equal(("coordinator", 7), await repository.GetAutoConnectInstanceAsync());
        Assert.Equal(CloseButtonAction.Exit, (await repository.GetAsync()).CloseButtonAction);

        await repository.SetAutoConnectInstanceAsync(null);

        Assert.Null(await repository.GetAutoConnectInstanceAsync());
    }

    [Fact]
    public async Task ThemeTypeRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AppearanceSettingsRepository(database.Database);

        Assert.Equal(ThemeType.Dark, await repository.GetCurrentThemeTypeAsync());

        await repository.UpdateCurrentThemeTypeAsync(ThemeType.Light);

        Assert.Equal(ThemeType.Light, await repository.GetCurrentThemeTypeAsync());
    }

    [Fact]
    public async Task LanguageRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AppearanceSettingsRepository(database.Database);

        // Not chosen yet: the language of the operating system
        Assert.Null(await repository.GetLanguageAsync());

        await repository.UpdateLanguageAsync("pt-BR");
        Assert.Equal("pt-BR", await repository.GetLanguageAsync());

        await repository.UpdateLanguageAsync(null);
        Assert.Null(await repository.GetLanguageAsync());
    }

    [Fact]
    public async Task LanguageChangeKeepsTheTheme()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AppearanceSettingsRepository(database.Database);

        await repository.UpdateCurrentThemeTypeAsync(ThemeType.Light);
        await repository.UpdateLanguageAsync("ru");

        Assert.Equal(ThemeType.Light, await repository.GetCurrentThemeTypeAsync());
    }

    [Fact]
    public async Task CustomColorsRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AppearanceSettingsRepository(database.Database);

        // Nothing chosen yet: every color is the one of the theme
        Assert.Equal(new CustomColors(), await repository.GetCustomColorsAsync());

        var colors = new CustomColors { Primary = "#FF8800", Save = "#00AA55", Error = "#CC2244" };
        await repository.UpdateCustomColorsAsync(colors);
        Assert.Equal(colors, await repository.GetCustomColorsAsync());

        await repository.UpdateCustomColorsAsync(new CustomColors());
        Assert.Equal(new CustomColors(), await repository.GetCustomColorsAsync());
    }

    [Fact]
    public async Task CustomColorsChangeKeepsTheThemeAndTheLanguage()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AppearanceSettingsRepository(database.Database);

        await repository.UpdateCurrentThemeTypeAsync(ThemeType.Light);
        await repository.UpdateLanguageAsync("ru");
        await repository.UpdateCustomColorsAsync(new CustomColors { Danger = "#AA0000" });

        Assert.Equal(ThemeType.Light, await repository.GetCurrentThemeTypeAsync());
        Assert.Equal("ru", await repository.GetLanguageAsync());
    }

    [Fact]
    public async Task AnimationSettingsRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AppearanceSettingsRepository(database.Database);

        // Nothing chosen yet: animated at the normal speed
        Assert.Equal(new AnimationSettings(), await repository.GetAnimationSettingsAsync());

        var settings = new AnimationSettings { IsEnabled = false, Speed = AnimationSpeed.Fast };
        await repository.UpdateAnimationSettingsAsync(settings);
        Assert.Equal(settings, await repository.GetAnimationSettingsAsync());
    }

    [Fact]
    public async Task AnimationSettingsChangeKeepsTheThemeAndTheColors()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AppearanceSettingsRepository(database.Database);

        var colors = new CustomColors { Primary = "#FF8800" };
        await repository.UpdateCurrentThemeTypeAsync(ThemeType.Light);
        await repository.UpdateCustomColorsAsync(colors);
        await repository.UpdateAnimationSettingsAsync(new AnimationSettings { Speed = AnimationSpeed.Slow });

        Assert.Equal(ThemeType.Light, await repository.GetCurrentThemeTypeAsync());
        Assert.Equal(colors, await repository.GetCustomColorsAsync());
    }

    [Fact]
    public async Task InterfaceScaleRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AppearanceSettingsRepository(database.Database);

        // Nothing chosen yet: the usual size
        Assert.Equal(InterfaceScale.Default, await repository.GetInterfaceScaleAsync());

        await repository.UpdateInterfaceScaleAsync(130);
        Assert.Equal(130, await repository.GetInterfaceScaleAsync());
    }

    [Fact]
    public async Task InterfaceScaleOutsideTheChoicesIsReadAsTheClosestOne()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AppearanceSettingsRepository(database.Database);

        await repository.UpdateInterfaceScaleAsync(400);
        Assert.Equal(InterfaceScale.Max, await repository.GetInterfaceScaleAsync());

        await repository.UpdateInterfaceScaleAsync(94);
        Assert.Equal(90, await repository.GetInterfaceScaleAsync());
    }

    [Fact]
    public async Task UseCustomTitleBarRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AppearanceSettingsRepository(database.Database);

        // Nothing chosen yet: the system draws the title bar
        Assert.False(await repository.GetUseCustomTitleBarAsync());

        await repository.UpdateUseCustomTitleBarAsync(true);
        Assert.True(await repository.GetUseCustomTitleBarAsync());
    }

    [Fact]
    public async Task IsExperimentalEnabledRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AppearanceSettingsRepository(database.Database);

        // Nothing chosen yet: the experimental features are off
        Assert.False(await repository.GetIsExperimentalEnabledAsync());

        await repository.UpdateIsExperimentalEnabledAsync(true);
        Assert.True(await repository.GetIsExperimentalEnabledAsync());
    }
}

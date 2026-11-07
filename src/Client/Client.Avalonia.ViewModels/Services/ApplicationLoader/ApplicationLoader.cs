using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Sounds;
using Client.Application.Settings;
using Client.Avalonia.Localization;
using Client.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Client.Services;

public partial class ApplicationLoader
{
    public bool IsLoaded;
    
    private readonly IMediaRuntimeLoader _mediaRuntimeLoader;
    private readonly IDatabaseInitializer _databaseInitializer;
    private readonly IDatabaseSecurityService _databaseSecurity;
    private readonly IAudioPlayerService _audioPlayerService;
    private readonly IAppearanceService _appearanceService;
    private readonly IStorageService _storageService;
    private readonly IVideoService _videoService;
    private readonly IDebugAndLogService _debugAndLogService;
    private readonly IHotKeysService _hotKeysService;
    private readonly IAudioSettingsRepository _audioSettings;
    private readonly IAudioDeviceProvider _audioDevices;
    private readonly ISoundService _soundService;
    private readonly IThemeService _themeService;
    private readonly ILanguageService _languageService;
    private readonly ILibWebRTCLoader _libWebRTCLoader;
    private readonly ILogger<ApplicationLoader> _logger;

    public ApplicationLoader(
        IMediaRuntimeLoader mediaRuntimeLoader,
        IDatabaseInitializer databaseInitializer,
        IDatabaseSecurityService databaseSecurity,
        IAudioPlayerService audioPlayerService,
        IAppearanceService appearanceService,
        IStorageService storageService,
        IVideoService videoService,
        IDebugAndLogService debugAndLogService,
        IHotKeysService hotKeysService,
        IAudioSettingsRepository audioSettings,
        IAudioDeviceProvider audioDevices,
        ISoundService soundService,
        IThemeService themeService,
        ILanguageService languageService,
        ILibWebRTCLoader libWebRTCLoader,
        ILogger<ApplicationLoader> logger)
    {
        _mediaRuntimeLoader = mediaRuntimeLoader;
        _databaseInitializer = databaseInitializer;
        _databaseSecurity = databaseSecurity;
        _audioPlayerService = audioPlayerService;
        _appearanceService = appearanceService;
        _storageService = storageService;
        _videoService = videoService;
        _debugAndLogService = debugAndLogService;
        _hotKeysService = hotKeysService;
        _audioSettings = audioSettings;
        _audioDevices = audioDevices;
        _soundService = soundService;
        _themeService = themeService;
        _languageService = languageService;
        _libWebRTCLoader = libWebRTCLoader;
        _logger = logger;
    }

    /// <param name="askDatabasePassword">
    /// Shows the dialog that asks for the password of an encrypted database the keyring does not
    /// open; true once the database is unlocked, false when the user gave up.
    /// </param>
    /// <param name="reportStage">Told on the caller's thread each time the start goes on to its next stage.</param>
    public async Task<ApplicationLoadResult> InitializeAsync(Func<DatabasePasswordVM, Task<bool>> askDatabasePassword,
        Action<ApplicationLoadStage>? reportStage = null)
    {
        var stopwatch = Stopwatch.StartNew();

        // Both steps are blocking native/SQLite calls: they run off the UI thread and in parallel
        var assembliesTask = Task.Run(LoadAssemblies);

        reportStage?.Invoke(ApplicationLoadStage.Database);

        // Nothing reads the database before it is unlocked
        var unlock = await UnlockDatabaseAsync(askDatabasePassword);
        if (unlock != ApplicationLoadResult.NoError)
        {
            await assembliesTask;
            return unlock;
        }

        reportStage?.Invoke(ApplicationLoadStage.Components);

        var databaseTask = Task.Run(LoadDatabase);
        await Task.WhenAll(assembliesTask, databaseTask);
    
        if (!assembliesTask.Result)
        {
            return _mediaRuntimeLoader.UsesBundledRuntime
                ? ApplicationLoadResult.MediaComponentsBroken
                : ApplicationLoadResult.NativeAssembliesFailedToLoad;
        }
    
        var databaseLoadResult = databaseTask.Result;
        if (databaseLoadResult != DatabaseState.NoError)
        {
            switch (databaseLoadResult)
            {
                case DatabaseState.NewerThanApplication:
                    return ApplicationLoadResult.DatabaseNewerThanApplication;

                case DatabaseState.Corrupted:
                    return ApplicationLoadResult.DatabaseCorrupted;

                case DatabaseState.InUse:
                    return ApplicationLoadResult.DatabaseInUse;
            
                case DatabaseState.UnexpectedError:
                    return ApplicationLoadResult.DatabaseError;
            }
        }

        reportStage?.Invoke(ApplicationLoadStage.Settings);

        // Back on the caller's (UI) thread: the theme touches Avalonia resources
        try
        {
            // First: the level of the journal and the native logs the user asked for, so the rest of the
            // start is written as they want it
            await _debugAndLogService.ApplyAsync();
            await LetFrameThroughAsync();

            // Before a chat is opened: what is kept of the chats and how much
            await _storageService.ApplyAsync();

            // Before a session is joined: how its screens are decoded and shown
            await _videoService.ApplyAsync();
            await LetFrameThroughAsync();

            var language = await _appearanceService.GetLanguageAsync();
            var theme = await _appearanceService.GetCurrentThemeTypeAsync();
            var colors = await _appearanceService.GetCustomColorsAsync();
            var animations = await _appearanceService.GetAnimationSettingsAsync();
            var scale = await _appearanceService.GetInterfaceScaleAsync();
            var experimental = await _appearanceService.GetIsExperimentalEnabledAsync();
            var customTitleBar = await _appearanceService.GetUseCustomTitleBarAsync();
            var useGStreamer = await _audioSettings.GetUseGStreamerAsync();

            // Apply the complete appearance together, without rendering intermediate settings.
            _languageService.Apply(language);
            _themeService.ApplyAnimations(animations);
            _themeService.ApplyTheme(theme);
            _themeService.ApplyCustomColors(colors);
            _themeService.ApplyInterfaceScale(scale);
            ExperimentalFeatures.Apply(experimental, customTitleBar, useGStreamer);
            await LetFrameThroughAsync();
        }
        catch (Exception exception)
        {
            LogThemeApplyFailed(exception);
        }

        if (Client.Core.Media.MediaRuntime.IsEnabled && ExperimentalFeatures.UseLibWebRTC)
            _ = LoadLibWebRTCAsync();

        try
        {
            var audioSettings = await _audioSettings.GetAsync();
            await Task.Run(() => _audioDevices.SetOutputDevice(audioSettings.OutputDeviceId, audioSettings.OutputDeviceName));
            _audioDevices.SetOutputVolume(audioSettings.OutputVolume);
            _soundService.Apply(audioSettings);
            await LetFrameThroughAsync();
        }
        catch (Exception exception)
        {
            LogSoundSettingsApplyFailed(exception);
        }

        try
        {
            await _hotKeysService.ApplyAsync();
        }
        catch (Exception exception)
        {
            LogHotKeysApplyFailed(exception);
        }

        LogLoaded(stopwatch.ElapsedMilliseconds);
        return ApplicationLoadResult.NoError;
    }
    
    // Every step of applying the settings is short, but they would hold the UI thread for a number of frames
    // all together: the continuations of their awaits come before the render. So a frame due goes through
    // between the steps, and the startup screen does not stall
    private static async Task LetFrameThroughAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private bool LoadAssemblies()
    {
        if (IsLoaded)
            return true; // Returns true if application already loaded 
        
        try
        {
            bool gstResult = !Client.Core.Media.MediaRuntime.IsEnabled || _mediaRuntimeLoader.Load();
            bool openAlResult = LoadOpenAL();

            IsLoaded = true;
            return gstResult && openAlResult;
        }
        catch (Exception exception)
        {
            LogLibrariesLoadFailed(exception);
            return false;
        }
    }

    private async Task<ApplicationLoadResult> UnlockDatabaseAsync(Func<DatabasePasswordVM, Task<bool>> askPassword)
    {
        DatabaseUnlockResult unlock;

        try
        {
            // The keyring may wait for the user to unlock it
            unlock = await Task.Run(_databaseSecurity.UnlockWithStoredPasswordAsync);
        }
        catch (Exception exception)
        {
            LogSettingsLoadFailed(exception);
            return ApplicationLoadResult.DatabaseError;
        }

        switch (unlock)
        {
            case DatabaseUnlockResult.Unlocked:
                return ApplicationLoadResult.NoError;

            case DatabaseUnlockResult.InUse:
                return ApplicationLoadResult.DatabaseInUse;
        }

        var dialog = new DatabasePasswordVM(DatabasePasswordPurpose.Unlock, async password =>
            await Task.Run(() => _databaseSecurity.UnlockAsync(password)) switch
            {
                DatabaseUnlockResult.Unlocked => null,
                DatabaseUnlockResult.InUse => Strings.Get("main.startup.databaseInUse"),
                _ => Strings.Get("databasePassword.wrong")
            });

        return await askPassword(dialog) ? ApplicationLoadResult.NoError : ApplicationLoadResult.DatabaseLocked;
    }

    /// <summary>
    /// Whether the user can get out of <paramref name="result"/> by starting with a new database.
    /// A file another copy holds is not one: that copy keeps using it.
    /// </summary>
    public static bool CanResetDatabase(ApplicationLoadResult result) => result is ApplicationLoadResult.DatabaseError
        or ApplicationLoadResult.DatabaseCorrupted or ApplicationLoadResult.DatabaseNewerThanApplication
        or ApplicationLoadResult.DatabaseLocked;

    /// <summary>
    /// Moves the database aside so that <see cref="InitializeAsync"/> starts with a new one.
    /// </summary>
    /// <returns>Whether the file was moved.</returns>
    public async Task<bool> ResetDatabaseAsync()
    {
        try
        {
            await Task.Run(_databaseInitializer.ResetDatabase);
            return true;
        }
        catch (Exception exception)
        {
            LogDatabaseResetFailed(exception);
            return false;
        }
    }

    private async Task<DatabaseState> LoadDatabase()
    {
        try
        {
            var state = await _databaseInitializer.VerifyDatabaseAsync();

            // The lock of the file and the keyring follow the stored settings
            if (state == DatabaseState.NoError)
                await _databaseSecurity.ApplyAsync();

            return state;
        }
        catch (Exception exception)
        {
            LogSettingsLoadFailed(exception);
            return DatabaseState.UnexpectedError;
        }
    }

    // Loaded at the start and not at the first session, so a libwebrtc that fails takes the voice to
    // GStreamer before the audio settings show the options of libwebrtc. Back on the UI thread to tell it
    private async Task LoadLibWebRTCAsync()
    {
        if (await Task.Run(_libWebRTCLoader.Load) is { } error)
            ExperimentalFeatures.ReportLibWebRTCFailed(error);
    }
}

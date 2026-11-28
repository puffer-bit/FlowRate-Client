using Client.Api;
using Client.Application.Coordinator.Users;
using Client.GStreamer.Loader;
using Client.GStreamer.Audio;
using Client.GStreamer.Video;
using Client.LibWebRTC.Native;
using Client.Platform;
using Client.TestBot;
using Client.Tools.AppInfo;
using Client.Tools.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Client.Application.Ports;
using Client.Application.Security;
using Client.Database.Sqlite;
using Client.TestBot.Accounts;

BotOptions? options;

try
{
    options = BotOptions.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(BotOptions.Usage);
    return 2;
}

if (options == null)
{
    Console.WriteLine(BotOptions.Usage);
    return 0;
}

// The bot runs on the GStreamer installed on the system, not on the bundle of the client
GstNativeLibraryResolver.Register(useInstalledRuntime: true);

// Branches are built for these kinds whenever the decoder can deliver them
if (options.GpuFrames)
    Client.Core.Media.VideoSurfaceSupport.Set(Client.Core.Media.VideoSurfaceKinds.DmaBuf |
        Client.Core.Media.VideoSurfaceKinds.CudaGl | Client.Core.Media.VideoSurfaceKinds.D3D11Shared |
        Client.Core.Media.VideoSurfaceKinds.YuvMemory);

if (options.GpuFramesFailAfter is { } failAfter)
    _ = Task.Delay(failAfter).ContinueWith(_ =>
        Client.Core.Media.VideoSurfaceSupport.ReportFailure(Client.Core.Media.VideoSurfaceKinds.DmaBuf |
            Client.Core.Media.VideoSurfaceKinds.CudaGl | Client.Core.Media.VideoSurfaceKinds.D3D11Shared |
            Client.Core.Media.VideoSurfaceKinds.YuvMemory));

Log.Logger = ClientLogging.CreateSerilogLogger(new LoggingOptions
{
    MinimumLevel = options.Verbose ? LogEventLevel.Verbose : LogEventLevel.Information
});

var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "FlowRate", "TestBot");

// The same transient registrations the desktop client uses, so every bot gets its own connection. The accounts and
// the keys of the devices go into the database of the bot, never into the keyring
var services = new ServiceCollection()
    .AddApi(new ClientInfo(DeviceInformation.Name, DevicePlatform.Name, AppInformation.GetClientVersion(),
        DevicePlatform.OsVersion, DevicePlatform.Architecture))
    .AddSqliteDatabase(dataDirectory)
    .AddSingleton<ISecretVault>(provider => new DatabaseSecretVault(provider.GetRequiredService<ISecretsRepository>()))
    .AddSingleton<IDeviceKeyStore, VaultDeviceKeyStore>()
    .AddSingleton(provider => new BotAccountStore(provider.GetRequiredService<ISecretsRepository>(), dataDirectory))
    .AddClientLogging(Log.Logger)
    .BuildServiceProvider();

var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Client.TestBot");

var databaseState = await services.GetRequiredService<IDatabaseInitializer>().VerifyDatabaseAsync();
if (databaseState != DatabaseState.NoError)
{
    ProgramLog.DatabaseFailed(logger, databaseState.ToString(), dataDirectory);
    return 1;
}

await using var owner = options.BotAccounts || options.DeleteBots
    ? new BotOwner(options, services, services.GetRequiredService<BotAccountStore>(),
        services.GetRequiredService<ILogger<BotOwner>>())
    : null;

if (options.DeleteBots)
{
    try
    {
        ProgramLog.BotsDeleted(logger, await owner!.DeleteBotsAsync(CancellationToken.None));
        return 0;
    }
    catch (InvalidOperationException exception)
    {
        ProgramLog.BotFailed(logger, exception.Message);
        return 1;
    }
    finally
    {
        await Log.CloseAndFlushAsync();
    }
}

if (!GstLoader.LoadGstreamer(services.GetRequiredService<ILogger<GstLoader>>()))
{
    ProgramLog.GStreamerInitFailed(logger);
    return 1;
}

AudioInput input;

try
{
    input = options.File == null
        ? new ToneAudioInput(options.ToneFrequency)
        : new FileAudioInput(await FileAudioSource.PrepareAsync(options.File, options.RawSampleRate, options.RawChannels));
}
catch (Exception exception) when (exception is IOException or InvalidOperationException)
{
    ProgramLog.InputFailed(logger, exception.Message);
    return 1;
}

// The statistics of the media are read only while something shows or writes them
if (options.PerfJournal != Client.Core.Settings.MediaPerfJournalMode.Off)
    Client.Core.Settings.DebugSettings.Apply(Client.Core.Settings.DebugSettings.Current with
    {
        MediaPerfJournal = options.PerfJournal,
        MediaPerfSummarySeconds = 5
    });

using var devices = new GstAudioDeviceProvider(services.GetRequiredService<ILogger<GstAudioDeviceProvider>>());
var video = new GstVideoMedia(services.GetRequiredService<ILoggerFactory>());
var transports = new LibWebRTCTransportFactory(
    new LibWebRTCRuntime(new GstVoiceAudioDevice(devices,
        services.GetRequiredService<ILogger<GstVoiceAudioDevice>>()),
        services.GetRequiredService<ILogger<LibWebRTCRuntime>>()), video,
    new GstAudioMedia(services.GetRequiredService<ILoggerFactory>()), new NativeVideoReceiver(),
    services.GetRequiredService<ILoggerFactory>());

if (!transports.IsAvailable)
{
    ProgramLog.LibWebRTCMissing(logger);
    return 1;
}
using var stop = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stop.Cancel();
};

var stats = new LoadStats(services.GetRequiredService<ILogger<LoadStats>>());
var bots = Enumerable.Range(0, options.Count)
    .Select(index => new VoiceBot(index, options, ToneOf(index) ?? input, services, transports, stats, owner))
    .ToList();

// Equal tones drift in phase against each other and their sum swells and fades, so each bot gets
// its own pitch: a step that is not a simple ratio keeps them from beating together. The pitches
// wrap around within a few octaves, so a crowd of bots stays well below the Nyquist frequency
const double ToneStep = 37;
const double ToneRange = 4000;

AudioInput? ToneOf(int index) =>
    options.File == null && index > 0 ? new ToneAudioInput(options.ToneFrequency + index * ToneStep % ToneRange) : null;

ProgramLog.Starting(logger, bots.Count, options.Host, options.Port);

var exitCode = 0;
using var reportStop = new CancellationTokenSource();
var report = stats.ReportForeverAsync(options.StatsInterval, reportStop.Token);

try
{
    await Task.WhenAll(bots.Select((bot, index) => RunBotAsync(bot, index * options.Ramp, stop.Token)));
}
finally
{
    await Task.WhenAll(bots.Select(bot => bot.StopAsync()));
    await reportStop.CancelAsync();
    await report;
    await Log.CloseAndFlushAsync();
}

return exitCode;

async Task RunBotAsync(VoiceBot bot, TimeSpan delay, CancellationToken cancellationToken)
{
    try
    {
        await Task.Delay(delay, cancellationToken);
        await bot.RunAsync(cancellationToken);
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception exception)
    {
        // One failing bot must not take the others down
        ProgramLog.BotFailed(logger, exception.Message);
        stats.BotFailed();
        exitCode = 1;
    }
}

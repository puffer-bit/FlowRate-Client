using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Settings;
using Client.Core.Settings;
using Client.Tools.Logging;
using Serilog.Events;
using Xunit;

namespace Client.Application.Tests;

// The journal is one for the process; the tests of it must not run beside each other
[Collection(nameof(JournalCollection))]
public class DebugAndLogServiceTests : IDisposable
{
    private readonly MemorySettings _settings = new();
    private readonly RecordingNativeLog _gstreamer = new(NativeLogSource.GStreamer);
    private readonly RecordingNativeLog _libWebRTC = new(NativeLogSource.LibWebRTC);
    private readonly DebugAndLogService _service;

    public DebugAndLogServiceTests()
    {
        _service = new DebugAndLogService(_settings, [_gstreamer, _libWebRTC]);
    }

    public void Dispose()
    {
        ClientLogging.LevelSwitch.MinimumLevel = ClientLogging.DefaultLevel;
        ClientLogging.IsWritingToFiles = true;
    }

    [Fact]
    public async Task SavingPutsTheSettingsIntoEffect()
    {
        await _service.SaveSettingsAsync(new DebugSettings
        {
            JournalLevel = JournalLevel.Warning,
            WriteToFiles = false,
            GStreamerLogLevel = NativeLogLevel.Debug,
            LibWebRTCLogLevel = NativeLogLevel.Error
        });

        Assert.Equal(LogEventLevel.Warning, ClientLogging.LevelSwitch.MinimumLevel);
        Assert.False(ClientLogging.IsWritingToFiles);
        Assert.Equal(NativeLogLevel.Debug, _gstreamer.Level);
        Assert.Equal(NativeLogLevel.Error, _libWebRTC.Level);
        Assert.Equal(JournalLevel.Warning, (await _settings.GetAsync()).JournalLevel);
    }

    [Fact]
    public async Task NoLevelIsTheOneOfTheBuild()
    {
        ClientLogging.LevelSwitch.MinimumLevel = LogEventLevel.Error;

        await _service.ApplyAsync();

        Assert.Equal(ClientLogging.DefaultLevel, ClientLogging.LevelSwitch.MinimumLevel);
        Assert.Equal(NativeLogLevel.Off, _gstreamer.Level);
    }

    [Fact]
    public void ListsTheNativeLogsOfThePlatform()
    {
        Assert.Equal([NativeLogSource.GStreamer, NativeLogSource.LibWebRTC], _service.NativeLogSources);
        Assert.Empty(new DebugAndLogService(_settings, []).NativeLogSources);
    }

    private sealed class MemorySettings : IDebugSettingsRepository
    {
        private DebugSettings _settings = new();

        public Task<DebugSettings> GetAsync() => Task.FromResult(_settings);

        public Task SaveAsync(DebugSettings settings)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNativeLog(NativeLogSource source) : INativeLog
    {
        public NativeLogLevel? Level { get; private set; }

        public NativeLogSource Source => source;

        public void SetLevel(NativeLogLevel level) => Level = level;
    }
}

[CollectionDefinition(nameof(JournalCollection), DisableParallelization = true)]
public sealed class JournalCollection;

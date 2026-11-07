using Client.Tools.AppInfo;
using Serilog.Events;
using Xunit;

namespace Client.Infrastructure.Tests;

public sealed class LaunchOptionsTests
{
    [Fact]
    public void Defaults_KeepNormalStartup()
    {
        var options = LaunchOptions.Parse([]);
        Assert.False(options.DisableMedia);
        Assert.False(options.PreventAutoConnect);
        Assert.False(options.DisableUpdates);
        Assert.False(options.NoFileLogging);
        Assert.False(options.AllowMultipleInstances);
        Assert.Null(options.Language);
        Assert.Null(options.LogLevel);
        Assert.Empty(options.SettingsNotice);
    }

    [Fact]
    public void SafeMode_DisablesMediaGlobalHotKeysAndAutomaticConnections()
    {
        var options = LaunchOptions.Parse(["--safe-mode"]);
        Assert.True(options.SafeMode);
        Assert.True(options.DisableMedia);
        Assert.True(options.PreventAutoConnect);
        Assert.Contains("--safe-mode", options.SettingsNotice);
    }

    [Fact]
    public void AllOptions_CanBeCombined()
    {
        var options = LaunchOptions.Parse(["--minimized", "--no-media", "--no-auto-connect",
            "--disable-updates", "--no-file-logging", "--software-rendering", "--lang", "ru",
            "--log-level=debug", "--data-dir", "custom data", "--allow-multiple-instances"]);
        Assert.True(options.Minimized);
        Assert.True(options.AllowMultipleInstances);
        Assert.True(options.DisableMedia);
        Assert.True(options.PreventAutoConnect);
        Assert.True(options.DisableUpdates);
        Assert.True(options.NoFileLogging);
        Assert.True(options.SoftwareRendering);
        Assert.Equal("ru", options.Language);
        Assert.Equal(LogEventLevel.Debug, options.LogLevel);
        Assert.Equal("custom data", options.DataDirectory);
    }

    [Theory]
    [InlineData("verbose", LogEventLevel.Verbose)]
    [InlineData("DEBUG", LogEventLevel.Debug)]
    [InlineData("info", LogEventLevel.Information)]
    [InlineData("information", LogEventLevel.Information)]
    [InlineData("warn", LogEventLevel.Warning)]
    [InlineData("warning", LogEventLevel.Warning)]
    [InlineData("error", LogEventLevel.Error)]
    [InlineData("fatal", LogEventLevel.Fatal)]
    public void LogLevel_AcceptsNamedLevels(string value, LogEventLevel expected) =>
        Assert.Equal(expected, LaunchOptions.Parse(["--log-level", value]).LogLevel);

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("es")]
    [InlineData("ja")]
    [InlineData("zh")]
    public void Language_AcceptsSupportedCodes(string code) =>
        Assert.Equal(code, LaunchOptions.Parse([$"--lang={code}"]).Language);

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_IsRecognized(string flag) => Assert.True(LaunchOptions.Parse([flag]).Help);

    [Fact]
    public void Version_IsRecognized() => Assert.True(LaunchOptions.Parse(["--version"]).Version);

    [Theory]
    [InlineData("--lang")]
    [InlineData("--lang=")]
    [InlineData("--lang=de")]
    [InlineData("--log-level")]
    [InlineData("--log-level=42")]
    [InlineData("--log-level=invalid")]
    [InlineData("--minimized=true")]
    [InlineData("--allow-multiple-instances=true")]
    [InlineData("--allow-multiple-instances", "--allow-multiple-instances")]
    [InlineData("--unknown")]
    [InlineData("--data-dir")]
    [InlineData("--data-dir=")]
    [InlineData("--lang", "--safe-mode")]
    [InlineData("--lang=ru", "--lang=en")]
    [InlineData("--safe-mode", "--safe-mode")]
    public void InvalidArguments_FailClearly(params string[] args) =>
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(args));
}

using Client.Tools.AppInfo;
using Xunit;

namespace Client.Infrastructure.Tests;

public sealed class AppDirectoriesTests
{
    [Fact]
    public void NoOption_UsesDefaultDirectory()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify), "FlowRate");
        Assert.Equal(Path.GetFullPath(expected), AppDirectories.ResolveDataDirectory(["--no-media"]));
    }

    [Fact]
    public void SeparateValue_ResolvesRelativePathAndPreservesSpaces()
    {
        Assert.Equal(Path.GetFullPath("custom data"),
            AppDirectories.ResolveDataDirectory(["--no-media", "--data-dir", "custom data"]));
    }

    [Fact]
    public void EqualsValue_PreservesAbsolutePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "FlowRate data");
        Assert.Equal(Path.GetFullPath(path), AppDirectories.ResolveDataDirectory([$"--data-dir={path}"]));
    }

    [Theory]
    [InlineData("--data-dir")]
    [InlineData("--data-dir=")]
    [InlineData("--data-dir", " ")]
    [InlineData("--data-dir", "--no-media")]
    [InlineData("--data-dir=a", "--data-dir=b")]
    [InlineData("--data-dir", "a", "--data-dir", "b")]
    [InlineData("--data-dir=bad\0path")]
    public void InvalidOption_IsRejected(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => AppDirectories.ResolveDataDirectory(args));
    }
}

using Client.Core;
using Xunit;

namespace Client.Infrastructure.Tests;

public sealed class ApplicationRuntimeTests
{
    [Fact]
    public void BrowserMode_CannotBeEnabledByADesktopHost()
    {
        Assert.False(OperatingSystem.IsBrowser());

        ApplicationRuntime.Configure([]);
        Assert.False(ApplicationRuntime.IsBrowser);
        Assert.True(ApplicationRuntime.SupportsUpdates);

        Assert.Throws<PlatformNotSupportedException>(() => ApplicationRuntime.Configure(["--browser"]));

        Assert.False(ApplicationRuntime.IsBrowser);
        Assert.True(ApplicationRuntime.SupportsUpdates);
    }
}

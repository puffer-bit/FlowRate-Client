using Client.Avalonia.Controls.LoadingIndicators;
using Xunit;

namespace Client.Application.Tests;

public class ProgressRingTests
{
    [Fact]
    public Task SweepFollowsAnglesAndRangeWithoutChangingTheRequestedAngles() => TestUiThread.RunAsync(() =>
    {
        var ring = new ProgressRing { Minimum = 0, Maximum = 100, Value = 50 };
        Assert.Equal(180, ring.ValueAngle);

        ring.StartAngle = 90;
        ring.EndAngle = 270;
        Assert.Equal(90, ring.ValueAngle);

        ring.Maximum = 200;
        Assert.Equal(45, ring.ValueAngle);
        Assert.Equal(90, ring.StartAngle);
        Assert.Equal(270, ring.EndAngle);

        ring.Minimum = 50;
        Assert.Equal(0, ring.ValueAngle);
        ring.Value = 125;
        Assert.Equal(90, ring.ValueAngle);
    });

    [Fact]
    public Task CollapsedRangeHasAnEmptySweepAndRecoversWhenExpanded() => TestUiThread.RunAsync(() =>
    {
        var ring = new ProgressRing { Minimum = 50, Maximum = 50, Value = 50 };
        Assert.Equal(0, ring.ValueAngle);
        Assert.True(double.IsFinite(ring.ValueAngle));

        ring.Maximum = 100;
        ring.Value = 75;
        Assert.Equal(180, ring.ValueAngle);
    });
}

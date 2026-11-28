using Client.Application.Coordinator.Users;
using Client.Core.Settings;
using Shared.Enums.User;
using Xunit;

namespace Client.Application.Tests;

public class IdleLevelsTests
{
    private static readonly BehaviorSettings Defaults = new();

    [Theory]
    [InlineData(0, IdleLevel.Active)]
    [InlineData(9, IdleLevel.Active)]
    [InlineData(10, IdleLevel.Idle)]
    [InlineData(119, IdleLevel.Idle)]
    [InlineData(120, IdleLevel.Sleeping)]
    public void FollowsTheThresholds(int minutes, IdleLevel expected)
    {
        Assert.Equal(expected, IdleLevels.Of(TimeSpan.FromMinutes(minutes), Defaults, isInCall: false));
    }

    [Fact]
    public void UnknownIdleTime_StaysActive()
    {
        Assert.Equal(IdleLevel.Active, IdleLevels.Of(null, Defaults, isInCall: false));
    }

    [Fact]
    public void Disabled_StaysActive()
    {
        var settings = new BehaviorSettings { IsAutoIdleEnabled = false };

        Assert.Equal(IdleLevel.Active, IdleLevels.Of(TimeSpan.FromHours(5), settings, isInCall: false));
    }

    [Fact]
    public void Call_KeepsActive_OnlyWhenTheUserAsks()
    {
        Assert.Equal(IdleLevel.Sleeping, IdleLevels.Of(TimeSpan.FromHours(5), Defaults, isInCall: true));

        var settings = new BehaviorSettings { KeepActiveDuringCalls = true };
        Assert.Equal(IdleLevel.Active, IdleLevels.Of(TimeSpan.FromHours(5), settings, isInCall: true));
    }

    [Fact]
    public void SleepingNever_StopsAtIdle()
    {
        var settings = new BehaviorSettings { SleepAfterMinutes = 0 };

        Assert.Equal(IdleLevel.Idle, IdleLevels.Of(TimeSpan.FromDays(1), settings, isInCall: false));
    }
}

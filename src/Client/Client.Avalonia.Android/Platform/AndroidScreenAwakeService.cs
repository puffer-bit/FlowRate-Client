using Client.Application.Settings;
using Client.Services;
using Microsoft.Extensions.Logging;

namespace Client.Droid.Platform;

internal sealed class AndroidScreenAwakeService(IBehaviorService behavior, ILogger<AndroidScreenAwakeService> logger)
    : ScreenAwakeService(behavior, logger)
{
    protected override void SetRequired(bool required) => MainActivity.SetScreenAwakeRequired(required);
}

using System;
using Client.Application.Ports;
using Client.Services.ScreenCast.Linux;
using Client.Services.ScreenCast.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Client.Services.ScreenCast;

internal static class DependencyInjection
{
    /// <summary>
    /// How the screen to share is chosen: through the desktop portal on Linux, from the list of
    /// windows and monitors on Windows. Also where the sound of the share comes from.
    /// </summary>
    public static IServiceCollection AddScreenCast(this IServiceCollection services)
    {
        if (OperatingSystem.IsLinux())
        {
            // A factory rather than a transient: the container keeps every disposable transient it
            // creates until the application exits, and a client is made for each session joined
            services.AddSingleton<Func<IScreenCastClient>>(provider => () =>
                new XdgDesktopPortalClient(provider.GetRequiredService<ILogger<XdgDesktopPortalClient>>()));

            // The portal shares no sound, so the application to take it from is chosen apart
            services.AddSingleton<IAudioAppProvider, PipeWireAudioAppProvider>();
        }
        else if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IScreenSourceProvider, WindowsScreenSourceProvider>();
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported OS platform");
        }

        return services;
    }
}

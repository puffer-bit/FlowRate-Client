using Client.OpenAL.Services.AudioPlayerService;
using Client.OpenAL.Services.SoundPlayer;
using Microsoft.Extensions.DependencyInjection;
using Client.Application;
using Client.Application.Ports;

namespace Client.OpenAL;

public static class DependencyInjection
{
    public static IServiceCollection AddOpenAL(this IServiceCollection services)
    {
        services.AddSingleton<AudioPlayerService>();
        services.AddSingleton<IAudioPlayerService>(provider => provider.GetRequiredService<AudioPlayerService>());
        services.AddSingleton<ISoundPlayer, OpenALSoundPlayer>();

        return services;
    }
}

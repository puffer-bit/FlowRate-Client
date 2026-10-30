using Microsoft.Extensions.DependencyInjection;
using Client.Application;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;
using Client.GStreamer.Audio;
using Client.GStreamer.Video;

namespace Client.GStreamer.Loader;

public class GstRuntimeLoader : IMediaRuntimeLoader
{
    private readonly ILogger<GstLoader> _logger;

    public GstRuntimeLoader(ILogger<GstLoader> logger)
    {
        _logger = logger;
    }

    public bool Load() => GstLoader.LoadGstreamer(_logger);

    public bool UsesBundledRuntime =>
        GstNativeLibraryResolver.LibraryDirectory == null || GstNativeLibraryResolver.IsBundled;
}

public static class DependencyInjection
{
    public static IServiceCollection AddGStreamer(this IServiceCollection services)
    {
        services.AddSingleton<IMediaRuntimeLoader, GstRuntimeLoader>();
        services.AddSingleton<INativeLog, GstNativeLog>();

        services.AddSingleton<GstAudioDeviceProvider>();
        services.AddSingleton<IAudioDeviceProvider>(provider => provider.GetRequiredService<GstAudioDeviceProvider>());
        services.AddSingleton<IMicrophoneTester, GstMicrophoneTester>();
        services.AddSingleton<IMediaCapabilities, GstMediaCapabilities>();
        services.AddSingleton<IVideoMedia, GstVideoMedia>();
        services.AddSingleton<IAudioMedia, GstAudioMedia>();

        return services;
    }
}
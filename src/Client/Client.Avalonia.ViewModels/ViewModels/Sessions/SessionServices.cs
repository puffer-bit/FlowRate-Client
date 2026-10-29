using System;
using Client.Application.Chat;
using Client.Application.Coordinator.Users;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Sessions.Hybrid;
using Client.Application.Settings;
using Client.Application.Sounds;
using Client.Services;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.ViewModels.Sessions;

/// <summary>
/// The services every opened chat uses.
/// </summary>
public sealed record ChatSessionServices(
    IChatCacheStore Caches,
    IChatMediaUploader MediaUploader,
    IChatImageService Images,
    IDismissedPromptsRepository DismissedPrompts);

/// <summary>
/// The services the hybrid session we are in uses. Resolved only when a session is entered: a
/// platform without voice does not register all of them.
/// </summary>
/// <param name="CreateScreenCastClient">Null where screens cannot be shared through a portal.</param>
/// <param name="ScreenSources">Null where the screens to share cannot be listed.</param>
/// <param name="AudioApps">Null where the sound of an application cannot be shared.</param>
public sealed record HybridSessionServices(
    Func<IScreenCastClient>? CreateScreenCastClient,
    IAudioPlayerService AudioPlayer,
    IAudioService Audio,
    ISoundService Sounds,
    INotificationService Notifications,
    IVoiceActivityService VoiceActivity,
    IScreenWatchService ScreenWatch,
    PeerStatsService PeerStats,
    IPeerPlaybackService Playback,
    IHybridSessionFactory SessionFactory,
    IScreenSourceProvider? ScreenSources,
    IAudioAppProvider? AudioApps,
    IMediaCapabilities MediaCapabilities,
    IScreenShareSettingsRepository ScreenShareSettings,
    IDismissedPromptsRepository DismissedPrompts,
    ICoordinatorStateRepository CoordinatorStates);

using System;
using System.Threading.Tasks;
using Client.Core.Media;
using Client.ViewModels.Sessions.Chat;

namespace Client.Services;

/// <summary>The playback operations used by the video attachment players of every platform.</summary>
public interface IChatVideoSession : IChatMediaSession
{
    /// <summary>Raised on a thread of the decoder with each frame of the video being played.</summary>
    event Action<RawVideoFrame>? FrameReady;

    bool IsBuffering { get; }

    /// <summary>How much of a video that plays while it downloads is there, null for a whole file.</summary>
    double? StreamProgress { get; }

    bool IsCurrent(ChatFileVM? file);
    Task InitializeAsync();

    /// <summary>Shows the last frame again, to a surface that has just been attached or resized.</summary>
    void RedisplayFrame();
}

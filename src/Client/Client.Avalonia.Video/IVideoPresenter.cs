using Client.Core.Media;

namespace Client.Avalonia.Video;

/// <summary>
/// Shows the frames of one stream.
/// </summary>
internal interface IVideoPresenter
{
    /// <summary>Takes a frame on the thread of its stream, which keeps it only during the call.</summary>
    /// <returns>False when this presenter cannot show frames of that kind.</returns>
    bool PushFrame(RawVideoFrame frame);

    /// <summary>Stops showing the last frame. Called on the UI thread.</summary>
    void Clear();

    FrameStats Stats { get; }

    StretchMode Stretch { get; set; }
}

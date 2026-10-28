namespace Client.Core.Media;

/// <summary>
/// One encoded picture: an H.264 access unit in Annex B, with its parameter sets before a keyframe, or
/// a whole VP8 frame. The bytes are only valid during the call that delivers it.
/// </summary>
/// <param name="Timestamp">
/// When the picture was captured or is to be shown, in nanoseconds of its stream, or -1 when it is
/// not known. Only the differences between the frames of one stream mean something.
/// </param>
public readonly record struct EncodedVideoFrame(IntPtr Data, int Length, long Timestamp, bool IsKeyFrame);

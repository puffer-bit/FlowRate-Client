using System;
using Android.Runtime;
using Client.Core.Media;
using Com.Flowrate.Rtc;

namespace Client.Droid.Platform.Media.WebRTC;

/// <summary>
/// A picture libwebrtc decoded, its I420 planes still in the memory of libwebrtc: they are given back
/// with the last reference, so a view that keeps the frame to draw it later needs no copy.
/// </summary>
internal sealed class AndroidPictureFrame : MemoryYuvVideoFrame
{
    private readonly VoicePeer.Picture _picture;

    private AndroidPictureFrame(VoicePeer.Picture picture, VideoPlane[] planes)
        : base(picture.Width, picture.Height, YuvPlaneLayout.I420, planes)
    {
        _picture = picture;
    }

    /// <summary>Takes the picture over; it is released with the last reference of the frame.</summary>
    public static AndroidPictureFrame Wrap(VoicePeer.Picture picture)
    {
        try
        {
            return new AndroidPictureFrame(picture,
            [
                Plane(picture.DataY, picture.StrideY),
                Plane(picture.DataU, picture.StrideU),
                Plane(picture.DataV, picture.StrideV)
            ]);
        }
        catch
        {
            picture.Release();
            picture.Dispose();
            throw;
        }
    }

    // Direct buffers: their address stays valid until the picture is released
    private static VideoPlane Plane(Java.Nio.ByteBuffer? buffer, int stride)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        using (buffer)
            return new VideoPlane(JNIEnv.GetDirectBufferAddress(buffer.Handle), stride);
    }

    protected override void Release()
    {
        _picture.Release();
        _picture.Dispose();
    }
}

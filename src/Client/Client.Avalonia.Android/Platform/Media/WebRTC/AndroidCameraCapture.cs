using System;
using Android;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.Content;
using Client.Application.Ports;
using Client.Core.Media;
using Client.LibWebRTC.Voice;
using Com.Flowrate.Rtc;

namespace Client.Droid.Platform.Media.WebRTC;

/// <summary>
/// The camera of the phone, over CameraCapture.java: Camera2 captures it and libwebrtc encodes and sends
/// it, so nothing is pushed here and the bitrate and the keyframes stay with libwebrtc. The preview is
/// handed out upright, as I420 pictures.
/// </summary>
internal sealed class AndroidCameraCapture : IPeerVideoCapture
{
    private readonly CameraCapture _camera;
    private readonly CameraListener _listener;
    private string _device;
    private int _isDisposed;

    private AndroidCameraCapture(CameraCapture camera, CameraListener listener, string device)
    {
        _camera = camera;
        _listener = listener;
        _device = device;
        listener.Owner = this;
    }

    /// <summary>
    /// The camera, once the user allowed it; null for another input, without the permission, or where
    /// libwebrtc cannot capture it here. Called off the main thread, which the permission dialog is
    /// waited on.
    /// </summary>
    public static AndroidCameraCapture? Create(VideoInput video, ScreenShareSettings settings)
    {
        if (video is not CameraVideoInput input)
            return null;

        var context = global::Android.App.Application.Context;

        if (ContextCompat.CheckSelfPermission(context, Manifest.Permission.Camera) != Permission.Granted &&
            (Looper.MyLooper() == Looper.MainLooper || MainActivity.Current is not { } activity ||
             !activity.RequestPermissionAsync(Manifest.Permission.Camera).GetAwaiter().GetResult()))
            return null;

        // The peer may still wait for the permission of the microphone; the camera needs libwebrtc all the same
        VoicePeer.Initialize(context);

        // Camera2 picks the size nearest to the one asked for, which it gives as the sensor lies
        var height = settings.MaxHeight;
        var width = (height * 16 / 9 + 1) & ~1;
        var listener = new CameraListener();
        var camera = VoicePeer.CreateCamera(input.DeviceId, width, height, settings.Framerate, listener);

        if (camera == null)
        {
            listener.Dispose();
            return null;
        }

        return new AndroidCameraCapture(camera, listener, input.DeviceId);
    }

    /// <summary>What goes into the section of the camera, see VoicePeer.negotiate.</summary>
    public CameraCapture Java => _camera;

    public event Action<Exception>? Failed;
    public event Action<RawVideoFrame>? PreviewFrameReady;

    // libwebrtc encodes the camera itself
    public event Action<EncodedVideoFrame>? EncodedFrameReady
    {
        add { }
        remove { }
    }

    public string Description => $"Camera2 {_device}, encoded by libwebrtc";

    public bool Start()
    {
        try
        {
            _camera.Start();
            return true;
        }
        catch (Java.Lang.Exception)
        {
            return false;
        }
    }

    public void RequestKeyFrame(bool isUrgent = false)
    {
    }

    public void SetBitrate(int kbps)
    {
    }

    public void SetPreviewShown(bool isShown) => _camera.SetPreviewShown(isShown);

    public bool SwitchInput(VideoInput input)
    {
        if (input is not CameraVideoInput camera || _isDisposed != 0)
            return false;

        _camera.SwitchTo(camera.DeviceId);
        _device = camera.DeviceId;
        return true;
    }

    public void Push(EncodedVideoFrame frame)
    {
    }

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        try
        {
            _camera.Close();
        }
        finally
        {
            _camera.Dispose();
            _listener.Dispose();
        }
    }

    private void OnPicture(VoicePeer.Picture picture)
    {
        AndroidPictureFrame frame;

        try
        {
            frame = AndroidPictureFrame.Wrap(picture);
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            PreviewFrameReady?.Invoke(new RawVideoFrame(IntPtr.Zero, frame.Width, frame.Height, 0)
            {
                Timestamp = picture.TimestampNs,
                Gpu = frame
            });
        }
        catch (Exception)
        {
            // The picture is dropped
        }
        finally
        {
            // A view that keeps the picture took a reference of its own
            frame.Dispose();
        }
    }

    // On the threads of the camera, where an exception would reach libwebrtc
    private sealed class CameraListener : Java.Lang.Object, CameraCapture.IListener
    {
        public AndroidCameraCapture? Owner { get; set; }

        public void OnPicture(VoicePeer.Picture? picture)
        {
            if (picture == null)
                return;

            if (Owner is { } owner)
                owner.OnPicture(picture);
            else
            {
                picture.Release();
                picture.Dispose();
            }
        }

        public void OnFailed(string? error)
        {
            try
            {
                Owner?.Failed?.Invoke(new InvalidOperationException(error ?? "camera failed"));
            }
            catch (Exception)
            {
                // The capture is stopped by whoever listens
            }
        }
    }
}

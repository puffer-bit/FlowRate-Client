using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using Client.Core.Media;

namespace Client.Avalonia.Video;

/// <summary>
/// Brings the shared Direct3D 11 textures of the decoder into the ANGLE context Avalonia renders
/// with on Windows, without a copy: ANGLE opens each texture itself, by its share handle, as a
/// pbuffer (<c>EGL_ANGLE_d3d_share_handle_client_buffer</c>), and the pbuffer is bound to an OpenGL
/// texture for as long as the frame is drawn.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is done on ANGLE's Direct3D device but through EGL: ANGLE opens the texture on its own
/// device and keeps its own state. The keyed mutex is the one ANGLE opened with the texture
/// (<c>EGL_ANGLE_keyed_mutex</c>); it is taken with the reader key before the draw and given back
/// with the writer key right after, so the decoder writes the texture again while the picture is on
/// the screen in a texture of the renderer.
/// </para>
/// <para>Runs on the render thread with the ANGLE context current.</para>
/// </remarks>
internal sealed unsafe class AngleD3D11Import : IDisposable
{
    private const int D3DTexture2DShareHandleAngle = 0x3200;
    private const int DxgiKeyedMutexAngle = 0x33A2;
    private const int Extensions = 0x3055;
    private const int None = 0x3038;
    private const int Width = 0x3057;
    private const int Height = 0x3056;
    private const int TextureFormat = 0x3080;
    private const int TextureRgba = 0x305E;
    private const int TextureTarget = 0x3081;
    private const int Texture2D = 0x305F;
    private const int BackBuffer = 0x3084;
    private const int RedSize = 0x3024;
    private const int GreenSize = 0x3023;
    private const int BlueSize = 0x3022;
    private const int AlphaSize = 0x3021;
    private const int BindToTextureRgba = 0x303A;
    private const int SurfaceType = 0x3033;
    private const int PbufferBit = 0x0001;

    private const int GlTexture2D = 0x0DE1;
    private const uint AcquireTimeoutMilliseconds = 4;
    private const int AcquireSyncSlot = 8;
    private const int ReleaseSyncSlot = 9;

    // More pbuffers than the decoder has textures means it made new ones: the old ones are let go
    private const int MaxOpened = 12;

    private static readonly string[] RequiredExtensions =
        ["EGL_ANGLE_d3d_share_handle_client_buffer", "EGL_ANGLE_keyed_mutex", "EGL_ANGLE_query_surface_pointer"];

    private readonly IntPtr _display;
    private readonly IntPtr _config;
    private readonly delegate* unmanaged<IntPtr, int, IntPtr, IntPtr, int*, IntPtr> _createPbuffer;
    private readonly delegate* unmanaged<IntPtr, IntPtr, int> _destroySurface;
    private readonly delegate* unmanaged<IntPtr, IntPtr, int, int> _bindTexImage;
    private readonly delegate* unmanaged<IntPtr, IntPtr, int, int> _releaseTexImage;
    private readonly delegate* unmanaged<IntPtr, IntPtr, int, IntPtr*, int> _querySurfacePointer;
    private readonly delegate* unmanaged<void> _flush;
    private readonly delegate* unmanaged<int, uint, void> _bindTexture;

    // The pbuffers ANGLE opened, by the share handles of their textures, with ANGLE's keyed mutexes
    private readonly Dictionary<IntPtr, Opened> _opened = new();

    // The pbuffer bound for the frame being drawn
    private Opened? _reading;

    private readonly record struct Opened(IntPtr Surface, IntPtr Mutex, int Width, int Height);

    private AngleD3D11Import(IntPtr display, IntPtr config, GlInterface gl)
    {
        _display = display;
        _config = config;
        _createPbuffer = (delegate* unmanaged<IntPtr, int, IntPtr, IntPtr, int*, IntPtr>)gl.GetProcAddress("eglCreatePbufferFromClientBuffer");
        _destroySurface = (delegate* unmanaged<IntPtr, IntPtr, int>)gl.GetProcAddress("eglDestroySurface");
        _bindTexImage = (delegate* unmanaged<IntPtr, IntPtr, int, int>)gl.GetProcAddress("eglBindTexImage");
        _releaseTexImage = (delegate* unmanaged<IntPtr, IntPtr, int, int>)gl.GetProcAddress("eglReleaseTexImage");
        _querySurfacePointer = (delegate* unmanaged<IntPtr, IntPtr, int, IntPtr*, int>)gl.GetProcAddress("eglQuerySurfacePointerANGLE");
        _flush = (delegate* unmanaged<void>)gl.GetProcAddress("glFlush");
        _bindTexture = (delegate* unmanaged<int, uint, void>)gl.GetProcAddress("glBindTexture");
    }

    /// <summary>The step that failed last, for the log.</summary>
    public static string? LastFailure { get; private set; }

    private static AngleD3D11Import? Fail(string step)
    {
        LastFailure = step;
        return null;
    }

    /// <summary>Whether the current context is ANGLE that can open shared Direct3D 11 textures.</summary>
    public static bool IsSupported(GlInterface gl) => Create(gl) is { } import && import.DisposeAndTrue();

    /// <returns>Null when the current context is not ANGLE on Direct3D 11 or lacks the extensions.</returns>
    public static AngleD3D11Import? Create(GlInterface gl)
    {
        var getDisplay = (delegate* unmanaged<IntPtr>)gl.GetProcAddress("eglGetCurrentDisplay");
        var queryString = (delegate* unmanaged<IntPtr, int, IntPtr>)gl.GetProcAddress("eglQueryString");
        var chooseConfig = (delegate* unmanaged<IntPtr, int*, IntPtr*, int, int*, int>)gl.GetProcAddress("eglChooseConfig");

        if (getDisplay == null || queryString == null || chooseConfig == null)
            return Fail("EGL entry points missing");

        foreach (var name in (ReadOnlySpan<string>)["eglCreatePbufferFromClientBuffer", "eglDestroySurface",
                     "eglBindTexImage", "eglReleaseTexImage", "eglQuerySurfacePointerANGLE", "glFlush", "glBindTexture"])
        {
            if (gl.GetProcAddress(name) == IntPtr.Zero)
                return Fail($"no {name}");
        }

        var display = getDisplay();

        if (display == IntPtr.Zero)
            return Fail("no current EGL display");

        var extensions = Marshal.PtrToStringAnsi(queryString(display, Extensions)) ?? "";

        foreach (var extension in RequiredExtensions)
        {
            if (!extensions.Contains(extension, StringComparison.Ordinal))
                return Fail($"no {extension}");
        }

        // Any configuration of 8-bit RGBA pbuffers that bind to textures
        var attributes = stackalloc int[]
        {
            RedSize, 8, GreenSize, 8, BlueSize, 8, AlphaSize, 8, BindToTextureRgba, 1, SurfaceType, PbufferBit, None
        };
        IntPtr config;
        int count;

        if (chooseConfig(display, attributes, &config, 1, &count) == 0 || count < 1)
            return Fail("no EGL configuration for pbuffers bound to textures");

        return new AngleD3D11Import(display, config, gl);
    }

    /// <summary>
    /// Takes the shared texture for this frame and binds it to <paramref name="texture"/>; it stays
    /// taken until <see cref="EndRead"/>.
    /// </summary>
    /// <returns>False when the frame cannot be imported, or was not released in time.</returns>
    public bool BeginRead(IntPtr sharedHandle, int width, int height, uint texture, out bool isBroken)
    {
        isBroken = false;
        EndRead();

        if (!_opened.TryGetValue(sharedHandle, out var opened) || opened.Width != width || opened.Height != height)
        {
            if (opened.Surface != IntPtr.Zero)
                Destroy(sharedHandle, opened);

            if (_opened.Count >= MaxOpened)
                ForgetShared();

            if (Open(sharedHandle, width, height) is not { } made)
            {
                isBroken = true;
                return false;
            }

            opened = made;
            _opened[sharedHandle] = opened;
        }

        var acquire = (delegate* unmanaged[Stdcall]<IntPtr, ulong, uint, int>)VTable(opened.Mutex, AcquireSyncSlot);

        if (acquire(opened.Mutex, D3D11SharedVideoFrame.ReaderKey, AcquireTimeoutMilliseconds) != 0)
            return false;

        _bindTexture(GlTexture2D, texture);

        if (_bindTexImage(_display, opened.Surface, BackBuffer) == 0)
        {
            ReleaseMutex(opened);
            LastFailure = "eglBindTexImage failed";
            isBroken = true;
            return false;
        }

        _reading = opened;
        return true;
    }

    /// <summary>
    /// Gives the texture of the frame back to the decoder, once its draw is queued. Nothing happens
    /// when no frame is taken.
    /// </summary>
    public void EndRead()
    {
        if (_reading is not { } opened)
            return;

        _reading = null;
        _releaseTexImage(_display, opened.Surface, BackBuffer);

        // The draw goes to the GPU before the decoder may write the texture again
        _flush();
        ReleaseMutex(opened);
    }

    private Opened? Open(IntPtr sharedHandle, int width, int height)
    {
        var attributes = stackalloc int[]
        {
            Width, width, Height, height, TextureFormat, TextureRgba, TextureTarget, Texture2D, None
        };
        var surface = _createPbuffer(_display, D3DTexture2DShareHandleAngle, sharedHandle, _config, attributes);

        if (surface == IntPtr.Zero)
        {
            LastFailure = "eglCreatePbufferFromClientBuffer failed";
            return null;
        }

        // ANGLE's own interface of the mutex, which it keeps as long as the pbuffer lives
        IntPtr mutex;

        if (_querySurfacePointer(_display, surface, DxgiKeyedMutexAngle, &mutex) == 0 || mutex == IntPtr.Zero)
        {
            _destroySurface(_display, surface);
            LastFailure = "no keyed mutex of the pbuffer";
            return null;
        }

        return new Opened(surface, mutex, width, height);
    }

    private static void ReleaseMutex(Opened opened) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, ulong, int>)VTable(opened.Mutex, ReleaseSyncSlot))(
            opened.Mutex, D3D11SharedVideoFrame.WriterKey);

    private void Destroy(IntPtr sharedHandle, Opened opened)
    {
        _destroySurface(_display, opened.Surface);
        _opened.Remove(sharedHandle);
    }

    /// <summary>Lets go of the textures the decoder stopped writing, after a size change.</summary>
    public void ForgetShared()
    {
        EndRead();

        foreach (var opened in _opened.Values)
            _destroySurface(_display, opened.Surface);

        _opened.Clear();
    }

    public void Dispose() => ForgetShared();

    private bool DisposeAndTrue()
    {
        Dispose();
        return true;
    }

    private static IntPtr VTable(IntPtr instance, int slot) => ((IntPtr*)*(IntPtr*)instance)[slot];
}

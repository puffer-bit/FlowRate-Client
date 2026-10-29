using System;
using Avalonia.OpenGL;

namespace Client.Views.Sessions.Hybrid.Video;

/// <summary>
/// Brings the shared Direct3D 11 textures of the decoder into the ANGLE context Avalonia renders
/// with on Windows: each frame is copied, on the GPU and inside the keyed mutex, into a texture of
/// ANGLE's own device, which is bound to an OpenGL texture once through an EGL image.
/// </summary>
/// <remarks>
/// Runs on the render thread with the ANGLE context current. The copy keeps the mutex only for as
/// long as it is queued: the writer gets the texture back with the writer key right after.
/// </remarks>
internal sealed unsafe class AngleD3D11Import : IDisposable
{
    private const int DeviceExt = 0x322C;
    private const int D3D11DeviceAngle = 0x33A1;
    private const int D3D11TextureAngle = 0x3484;
    private const int Extensions = 0x3055;
    private const int None = 0x3038;

    private const uint FormatB8G8R8A8 = 87;
    private const uint BindShaderResource = 0x8;
    private const uint AcquireTimeoutMilliseconds = 4;

    private const int QueryInterfaceSlot = 0;
    private const int ReleaseSlot = 2;
    private const int CreateTexture2DSlot = 5;
    private const int GetImmediateContextSlot = 40;
    private const int OpenSharedResource1Slot = 48;
    private const int AcquireSyncSlot = 8;
    private const int ReleaseSyncSlot = 9;
    private const int CopyResourceSlot = 47;

    private static readonly Guid D3D11Device1 = new("a04bfb29-08ef-43d6-a49c-a9bdbdcbe686");
    private static readonly Guid D3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid DxgiKeyedMutex = new("9d8e1289-d7b3-465f-8126-250e349af85d");

    private readonly IntPtr _display;
    private readonly IntPtr _device;
    private readonly IntPtr _context;
    private readonly delegate* unmanaged<IntPtr, IntPtr, int, IntPtr, int*, IntPtr> _createImage;
    private readonly delegate* unmanaged<IntPtr, IntPtr, int> _destroyImage;

    // Opened shared textures by their handles, with their mutexes
    private readonly System.Collections.Generic.Dictionary<IntPtr, (IntPtr Texture, IntPtr Mutex)> _opened = new();

    // ANGLE's own copy of the frame and its OpenGL face
    private IntPtr _copy;
    private IntPtr _image;
    private int _width;
    private int _height;

    private AngleD3D11Import(IntPtr display, IntPtr device, IntPtr context,
        delegate* unmanaged<IntPtr, IntPtr, int, IntPtr, int*, IntPtr> createImage,
        delegate* unmanaged<IntPtr, IntPtr, int> destroyImage)
    {
        _display = display;
        _device = device;
        _context = context;
        _createImage = createImage;
        _destroyImage = destroyImage;
    }

    /// <summary>The step that failed last, for the log.</summary>
    public static string? LastFailure { get; private set; }

    private static AngleD3D11Import? Fail(string step)
    {
        LastFailure = step;
        return null;
    }

    /// <summary>Whether the current context is ANGLE on Direct3D 11 that can import textures.</summary>
    public static bool IsSupported(GlInterface gl) => Create(gl) is { } import && import.DisposeAndTrue();

    /// <returns>Null when the current context is not ANGLE on Direct3D 11 or lacks the extensions.</returns>
    public static AngleD3D11Import? Create(GlInterface gl)
    {
        var getDisplay = (delegate* unmanaged<IntPtr>)gl.GetProcAddress("eglGetCurrentDisplay");
        var queryString = (delegate* unmanaged<IntPtr, int, IntPtr>)gl.GetProcAddress("eglQueryString");
        var queryDisplay = (delegate* unmanaged<IntPtr, int, IntPtr*, int>)gl.GetProcAddress("eglQueryDisplayAttribEXT");
        var queryDevice = (delegate* unmanaged<IntPtr, int, IntPtr*, int>)gl.GetProcAddress("eglQueryDeviceAttribEXT");
        var createImage = (delegate* unmanaged<IntPtr, IntPtr, int, IntPtr, int*, IntPtr>)gl.GetProcAddress("eglCreateImageKHR");
        var destroyImage = (delegate* unmanaged<IntPtr, IntPtr, int>)gl.GetProcAddress("eglDestroyImageKHR");

        if (getDisplay == null || queryString == null || queryDisplay == null || queryDevice == null ||
            createImage == null || destroyImage == null)
            return Fail("EGL entry points missing");

        var display = getDisplay();

        if (display == IntPtr.Zero)
            return Fail("no current EGL display");

        var extensions = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(queryString(display, Extensions)) ?? "";

        if (!extensions.Contains("EGL_ANGLE_image_d3d11_texture", StringComparison.Ordinal))
            return Fail("no EGL_ANGLE_image_d3d11_texture: " + extensions);

        IntPtr eglDevice, d3dDevice;

        if (queryDisplay(display, DeviceExt, &eglDevice) == 0 || queryDevice(eglDevice, D3D11DeviceAngle, &d3dDevice) == 0 ||
            d3dDevice == IntPtr.Zero)
            return Fail("no Direct3D 11 device behind EGL");

        var device1 = Query(d3dDevice, D3D11Device1);

        if (device1 == IntPtr.Zero)
            return Fail("no ID3D11Device1");

        IntPtr context;
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)VTable(device1, GetImmediateContextSlot))(device1, &context);

        return context == IntPtr.Zero ? Release(device1) : new AngleD3D11Import(display, device1, context, createImage, destroyImage);
    }

    /// <summary>
    /// Copies the shared texture into ANGLE's one and returns the EGL image of it, made once per size.
    /// </summary>
    /// <returns>Zero when the frame cannot be imported, or was not released in time.</returns>
    public IntPtr Import(IntPtr sharedHandle, int width, int height, out bool isBroken)
    {
        isBroken = false;

        // Before the shared texture is looked up: a new size forgets the opened ones, and one taken
        // before would be used after its release
        if (!EnsureCopy(width, height))
        {
            LastFailure ??= "copy texture or EGL image not created";
            isBroken = true;
            return IntPtr.Zero;
        }

        if (!_opened.TryGetValue(sharedHandle, out var shared))
        {
            IntPtr texture;
            var id = D3D11Texture2D;
            var open = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)VTable(_device, OpenSharedResource1Slot);

            var opened = open(_device, sharedHandle, &id, &texture);

            if (opened < 0)
            {
                LastFailure = $"OpenSharedResource1 0x{opened:X8}";
                isBroken = true;
                return IntPtr.Zero;
            }

            var mutex = Query(texture, DxgiKeyedMutex);

            if (mutex == IntPtr.Zero)
            {
                LastFailure = "no keyed mutex";
                ComRelease(texture);
                isBroken = true;
                return IntPtr.Zero;
            }

            shared = (texture, mutex);
            _opened[sharedHandle] = shared;
        }

        var acquire = (delegate* unmanaged[Stdcall]<IntPtr, ulong, uint, int>)VTable(shared.Mutex, AcquireSyncSlot);

        if (acquire(shared.Mutex, Client.Core.Media.D3D11SharedVideoFrame.ReaderKey, AcquireTimeoutMilliseconds) != 0)
            return IntPtr.Zero;

        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)VTable(_context, CopyResourceSlot))(_context, _copy, shared.Texture);
        ((delegate* unmanaged[Stdcall]<IntPtr, ulong, int>)VTable(shared.Mutex, ReleaseSyncSlot))(
            shared.Mutex, Client.Core.Media.D3D11SharedVideoFrame.WriterKey);

        return _image;
    }

    /// <summary>Forgets the shared textures the decoder stopped writing, after a size change.</summary>
    public void ForgetShared()
    {
        foreach (var (texture, mutex) in _opened.Values)
        {
            ComRelease(mutex);
            ComRelease(texture);
        }

        _opened.Clear();
    }

    private bool EnsureCopy(int width, int height)
    {
        if (_copy != IntPtr.Zero && width == _width && height == _height)
            return true;

        DestroyCopy();
        ForgetShared();

        var description = stackalloc uint[11] { (uint)width, (uint)height, 1, 1, FormatB8G8R8A8, 1, 0, 0, BindShaderResource, 0, 0 };
        IntPtr texture;

        if (((delegate* unmanaged[Stdcall]<IntPtr, uint*, IntPtr, IntPtr*, int>)VTable(_device, CreateTexture2DSlot))(
                _device, description, IntPtr.Zero, &texture) < 0)
            return false;

        var attributes = stackalloc int[1] { None };
        var image = _createImage(_display, IntPtr.Zero, D3D11TextureAngle, texture, attributes);

        if (image == IntPtr.Zero)
        {
            ComRelease(texture);
            return false;
        }

        _copy = texture;
        _image = image;
        _width = width;
        _height = height;
        return true;
    }

    private void DestroyCopy()
    {
        if (_image != IntPtr.Zero)
            _destroyImage(_display, _image);

        ComRelease(_copy);
        _image = _copy = IntPtr.Zero;
    }

    public void Dispose()
    {
        DestroyCopy();
        ForgetShared();
        ComRelease(_context);
        ComRelease(_device);
    }

    private bool DisposeAndTrue()
    {
        Dispose();
        return true;
    }

    private static AngleD3D11Import? Release(IntPtr instance)
    {
        ComRelease(instance);
        return null;
    }

    private static IntPtr Query(IntPtr instance, Guid id)
    {
        IntPtr result;
        var query = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)VTable(instance, QueryInterfaceSlot);

        return query(instance, &id, &result) < 0 ? IntPtr.Zero : result;
    }

    private static void ComRelease(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(instance, ReleaseSlot))(instance);
    }

    private static IntPtr VTable(IntPtr instance, int slot) => ((IntPtr*)*(IntPtr*)instance)[slot];
}

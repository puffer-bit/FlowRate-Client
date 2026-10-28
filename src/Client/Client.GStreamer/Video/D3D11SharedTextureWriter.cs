using System.Runtime.InteropServices;
using Client.Core.Media;
using Client.GStreamer.Interop;

namespace Client.GStreamer.Video;

/// <summary>
/// Copies BGRA frames of Direct3D 11 memory, on the GPU, into a few textures the compositor of the
/// user interface opens through their NT handles.
/// </summary>
/// <remarks>
/// The textures carry a keyed mutex: the writer releases one with
/// <see cref="D3D11SharedVideoFrame.ReaderKey"/> after the copy, the reader takes it with that key
/// and gives it back with <see cref="D3D11SharedVideoFrame.WriterKey"/>. A frame the reader never
/// took is taken back by the writer with the reader key. Runs on the thread of one appsink.
/// </remarks>
internal sealed unsafe class D3D11SharedTextureWriter : IDisposable
{
    private const int SlotCount = 6;

    // GST_MAP_FLAG_LAST << 1: map for Direct3D 11 access, which finishes pending writes
    private const int MapD3D11 = (1 << 16) << 1;
    private const int MapRead = 1;
    private const int MapInfoSize = 128;

    // GstD3D11Memory: the GstMemory (112 bytes), then its device. The library has no getter for it
    private const int D3D11MemoryDeviceOffset = 112;

    private const uint FormatB8G8R8A8 = 87;
    private const uint BindShaderResource = 0x8;
    private const uint BindRenderTarget = 0x20;
    private const uint MiscSharedKeyedMutex = 0x100;
    private const uint MiscSharedNtHandle = 0x800;
    private const uint SharedResourceRead = 0x80000000;
    private const uint SharedResourceWrite = 1;
    private const uint WaitTimeout = 0x102;
    private const uint AcquireTimeoutMilliseconds = 8;

    // Slots of the vtables used here
    private const int QueryInterfaceSlot = 0;
    private const int ReleaseSlot = 2;
    private const int CreateTexture2DSlot = 5;
    private const int CreateSharedHandleSlot = 13;
    private const int AcquireSyncSlot = 8;
    private const int ReleaseSyncSlot = 9;
    private const int CopySubresourceRegionSlot = 46;

    private static readonly Guid DxgiResource1 = new("30961379-4609-4a41-998e-54fe567ee0c1");
    private static readonly Guid DxgiKeyedMutex = new("9d8e1289-d7b3-465f-8126-250e349af85d");

    private readonly Slot?[] _slots = new Slot?[SlotCount];
    private IntPtr _device;
    private int _width;
    private int _height;
    private int _next;

    /// <summary>Copies cannot work on this system; frames should be delivered in memory instead.</summary>
    public bool IsBroken { get; private set; }

    /// <returns>Null when the frame was skipped, or when <see cref="IsBroken"/> is set.</returns>
    public D3D11SharedVideoFrame? Write(IntPtr buffer, VideoCapsInfo caps)
    {
        if (IsBroken)
            return null;

        try
        {
            return WriteCore(buffer, caps);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            LastFailure = exception.Message;
            IsBroken = true;
            return null;
        }
    }

    private D3D11SharedVideoFrame? WriteCore(IntPtr buffer, VideoCapsInfo caps)
    {
        if (GstVideoNative.GetMemoryCount(buffer) != 1)
            return Broken($"{GstVideoNative.GetMemoryCount(buffer)} memories in a buffer");

        var memory = gst_buffer_peek_memory(buffer, 0);

        if (memory == IntPtr.Zero || gst_is_d3d11_memory(memory) == 0)
            return Broken("not Direct3D 11 memory");

        var gstDevice = Marshal.ReadIntPtr(memory, D3D11MemoryDeviceOffset);

        if (gstDevice == IntPtr.Zero)
            return Broken("no device");

        var device = gst_d3d11_device_get_device_handle(gstDevice);
        var context = gst_d3d11_device_get_device_context_handle(gstDevice);

        if (device == IntPtr.Zero || context == IntPtr.Zero)
            return Broken("no device or context");

        if (device != _device || caps.Width != _width || caps.Height != _height)
        {
            DisposeSlots();
            _device = device;
            _width = caps.Width;
            _height = caps.Height;
        }

        var slot = TakeSlot(device);

        if (slot == null)
            return null;

        var mapInfo = stackalloc byte[MapInfoSize];

        // Finishes what the converter still has queued for the texture
        if (gst_memory_map(memory, (IntPtr)mapInfo, MapRead | MapD3D11) == 0)
        {
            Release(slot, D3D11SharedVideoFrame.WriterKey);
            return null;
        }

        try
        {
            var source = gst_d3d11_memory_get_resource_handle(memory);
            var subresource = gst_d3d11_memory_get_subresource_index(memory);

            gst_d3d11_device_lock(gstDevice);

            try
            {
                var copy = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, IntPtr, void>)
                    VTable(context, CopySubresourceRegionSlot);
                copy(context, slot.Texture, 0, 0, 0, 0, source, subresource, IntPtr.Zero);

                // The reader may take it now
                Release(slot, D3D11SharedVideoFrame.ReaderKey);
            }
            finally
            {
                gst_d3d11_device_unlock(gstDevice);
            }
        }
        finally
        {
            gst_memory_unmap(memory, (IntPtr)mapInfo);
        }

        slot.InUse = true;
        slot.ReaderHasIt = true;

        return new SharedFrame(slot, caps);
    }

    // The next slot the reader is done with, made on first use
    private Slot? TakeSlot(IntPtr device)
    {
        for (var attempt = 0; attempt < SlotCount; attempt++)
        {
            var index = _next;
            _next = (_next + 1) % SlotCount;

            var slot = _slots[index] ??= CreateSlot(device);

            if (slot == null)
                return null;

            // Still on the screen
            if (slot.InUse)
                continue;

            // A texture handed to the reader comes back with the writer key, one it never took
            // with the reader key it was left with
            var key = !slot.ReaderHasIt ? D3D11SharedVideoFrame.WriterKey
                : slot.WasRead ? D3D11SharedVideoFrame.WriterKey
                : D3D11SharedVideoFrame.ReaderKey;

            var acquire = (delegate* unmanaged[Stdcall]<IntPtr, ulong, uint, int>)VTable(slot.Mutex, AcquireSyncSlot);
            var result = acquire(slot.Mutex, key, AcquireTimeoutMilliseconds);

            if (result == unchecked((int)WaitTimeout) || result < 0)
                continue;

            slot.ReaderHasIt = false;
            slot.WasRead = false;
            return slot;
        }

        return null;
    }

    private Slot? CreateSlot(IntPtr device)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = FormatB8G8R8A8,
            SampleCount = 1,
            BindFlags = BindShaderResource | BindRenderTarget,
            MiscFlags = MiscSharedKeyedMutex | MiscSharedNtHandle
        };

        IntPtr texture;
        var create = (delegate* unmanaged[Stdcall]<IntPtr, Texture2DDescription*, IntPtr, IntPtr*, int>)
            VTable(device, CreateTexture2DSlot);

        var created = create(device, &description, IntPtr.Zero, &texture);

        if (created < 0)
        {
            Broken($"CreateTexture2D 0x{created:X8}");
            return null;
        }

        var resource = Query(texture, DxgiResource1);
        var mutex = Query(texture, DxgiKeyedMutex);
        var handle = IntPtr.Zero;

        if (resource != IntPtr.Zero)
        {
            var share = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr, IntPtr*, int>)
                VTable(resource, CreateSharedHandleSlot);

            if (share(resource, IntPtr.Zero, SharedResourceRead | SharedResourceWrite, IntPtr.Zero, &handle) < 0)
                handle = IntPtr.Zero;

            ComRelease(resource);
        }

        if (mutex == IntPtr.Zero || handle == IntPtr.Zero)
        {
            ComRelease(mutex);
            ComRelease(texture);

            if (handle != IntPtr.Zero)
                CloseHandle(handle);

            Broken(mutex == IntPtr.Zero ? "no keyed mutex" : "CreateSharedHandle failed");
            return null;
        }

        return new Slot(texture, mutex, handle);
    }

    private static void Release(Slot slot, uint key)
    {
        var release = (delegate* unmanaged[Stdcall]<IntPtr, ulong, int>)VTable(slot.Mutex, ReleaseSyncSlot);
        release(slot.Mutex, key);
    }

    private static IntPtr Query(IntPtr instance, Guid id)
    {
        IntPtr result;
        var query = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)VTable(instance, QueryInterfaceSlot);

        return query(instance, &id, &result) < 0 ? IntPtr.Zero : result;
    }

    private static void ComRelease(IntPtr instance)
    {
        if (instance == IntPtr.Zero)
            return;

        var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(instance, ReleaseSlot);
        release(instance);
    }

    private static IntPtr VTable(IntPtr instance, int slot) => ((IntPtr*)*(IntPtr*)instance)[slot];

    /// <summary>The step that broke the writer, for the log.</summary>
    public static string? LastFailure { get; private set; }

    private D3D11SharedVideoFrame? Broken(string reason)
    {
        LastFailure = reason;
        IsBroken = true;
        return null;
    }

    private void DisposeSlots()
    {
        for (var index = 0; index < SlotCount; index++)
        {
            var slot = _slots[index];
            _slots[index] = null;

            // A slot still on the screen is let go by its frame
            if (slot == null)
                continue;

            lock (slot)
            {
                slot.IsOrphaned = true;

                if (!slot.InUse)
                    slot.Destroy();
            }
        }

        _next = 0;
    }

    public void Dispose() => DisposeSlots();

    private sealed class Slot(IntPtr texture, IntPtr mutex, IntPtr handle)
    {
        public IntPtr Texture { get; } = texture;
        public IntPtr Mutex { get; } = mutex;
        public IntPtr Handle { get; } = handle;

        public volatile bool InUse;
        public volatile bool ReaderHasIt;
        public volatile bool WasRead;
        public bool IsOrphaned;
        private bool _isDestroyed;

        public void Destroy()
        {
            if (_isDestroyed)
                return;

            _isDestroyed = true;
            ComRelease(Mutex);
            ComRelease(Texture);
            CloseHandle(Handle);
        }
    }

    private sealed class SharedFrame : D3D11SharedVideoFrame
    {
        private readonly Slot _slot;

        public SharedFrame(Slot slot, VideoCapsInfo caps) : base(caps.Width, caps.Height, slot.Handle)
        {
            _slot = slot;
            Matrix = caps.Matrix;
            IsFullRange = caps.IsFullRange;
        }

        protected override void Release()
        {
            lock (_slot)
            {
                _slot.WasRead = IsRead;
                _slot.InUse = false;

                if (_slot.IsOrphaned)
                    _slot.Destroy();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture2DDescription
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleCount;
        public uint SampleQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [DllImport("kernel32.dll")]
    private static extern int CloseHandle(IntPtr handle);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_buffer_peek_memory(IntPtr buffer, uint index);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_memory_map(IntPtr memory, IntPtr info, int flags);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_memory_unmap(IntPtr memory, IntPtr info);

    [DllImport(NativeLibraries.GstD3D11)]
    private static extern int gst_is_d3d11_memory(IntPtr memory);

    [DllImport(NativeLibraries.GstD3D11)]
    private static extern IntPtr gst_d3d11_memory_get_resource_handle(IntPtr memory);

    [DllImport(NativeLibraries.GstD3D11)]
    private static extern uint gst_d3d11_memory_get_subresource_index(IntPtr memory);

    [DllImport(NativeLibraries.GstD3D11)]
    private static extern IntPtr gst_d3d11_device_get_device_handle(IntPtr device);

    [DllImport(NativeLibraries.GstD3D11)]
    private static extern IntPtr gst_d3d11_device_get_device_context_handle(IntPtr device);

    [DllImport(NativeLibraries.GstD3D11)]
    private static extern void gst_d3d11_device_lock(IntPtr device);

    [DllImport(NativeLibraries.GstD3D11)]
    private static extern void gst_d3d11_device_unlock(IntPtr device);
}

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Client.Core.Media;
using Client.GStreamer.Interop;
using Gst;

namespace Client.GStreamer.Video;

/// <summary>
/// An NV12 frame in CUDA memory of an NVIDIA decoder, copied into OpenGL textures on the GPU.
/// </summary>
/// <remarks>
/// The memory stays mapped for CUDA from the moment the frame is made until it is released; the
/// copy registers the textures with CUDA once and reuses the registration for every frame.
/// </remarks>
internal sealed class CudaGlFrame : GlCopyVideoFrame
{
    private const string CudaLibrary = "libcuda.so.1";

    // GST_MAP_FLAG_LAST << 1: the data of the map is the device pointer
    private const int MapCuda = (1 << 16) << 1;
    private const int MapRead = 1;

    // GstMapInfo: memory, flags with padding, then the data pointer
    private const int MapInfoSize = 128;
    private const int MapInfoDataOffset = 16;

    // GstCudaMemory: the GstMemory (112 bytes), the context, then the GstVideoInfo whose offsets and
    // strides start 72 and 104 bytes in
    private const int CudaMemoryContextOffset = 112;
    private const int CudaMemoryInfoOffset = 120;
    private const int VideoInfoOffsetsOffset = 72;
    private const int VideoInfoStridesOffset = 104;

    private const uint GlTexture2D = 0x0DE1;
    private const uint RegisterWriteDiscard = 2;
    private const int MemoryTypeDevice = 2;
    private const int MemoryTypeArray = 3;

    // Registrations of textures with CUDA, by texture name and the CUDA context they were made in
    private static readonly ConcurrentDictionary<(uint Texture, IntPtr Context), IntPtr> Registrations = new();

    static CudaGlFrame()
    {
        TextureDeleting += Unregister;
    }

    private IntPtr _sample;
    private readonly IntPtr _memory;
    private readonly IntPtr _mapInfo;
    private readonly IntPtr _cudaContext;
    private readonly IntPtr _device;
    private readonly (long Offset, int Stride)[] _planes;

    private CudaGlFrame(IntPtr sample, IntPtr memory, IntPtr mapInfo, IntPtr cudaContext,
        (long, int)[] planes, VideoCapsInfo caps) : base(caps.Width, caps.Height)
    {
        _sample = sample;
        _memory = memory;
        _mapInfo = mapInfo;
        _cudaContext = cudaContext;
        _device = Marshal.ReadIntPtr(mapInfo, MapInfoDataOffset);
        _planes = planes;
    }

    /// <returns>Null when the buffer is not a single CUDA memory that can be mapped.</returns>
    public static CudaGlFrame? TryCreate(Sample sample, IntPtr buffer, VideoCapsInfo caps)
    {
        if (GstVideoNative.GetMemoryCount(buffer) != 1)
            return null;

        var memory = gst_buffer_peek_memory(buffer, 0);

        if (memory == IntPtr.Zero || gst_is_cuda_memory(memory) == 0)
            return null;

        var context = gst_cuda_context_get_handle(Marshal.ReadIntPtr(memory, CudaMemoryContextOffset));

        if (context == IntPtr.Zero)
            return null;

        var planes = GstVideoNative.GetPlaneLayout(buffer) ?? ReadInfoLayout(memory);

        if (planes.Length < 2)
            return null;

        // The decoder may still be writing on its stream
        gst_cuda_memory_sync(memory);

        var mapInfo = Marshal.AllocHGlobal(MapInfoSize);

        if (gst_memory_map(memory, mapInfo, MapRead | MapCuda) == 0)
        {
            Marshal.FreeHGlobal(mapInfo);
            return null;
        }

        // The memory belongs to the buffer of the sample, which the frame holds
        return new CudaGlFrame(GstVideoNative.Ref(sample.Handle), memory, mapInfo, context, planes, caps)
        {
            Matrix = caps.Matrix,
            IsFullRange = caps.IsFullRange
        };
    }

    private static (long, int)[] ReadInfoLayout(IntPtr memory)
    {
        var info = memory + CudaMemoryInfoOffset;

        return
        [
            (Marshal.ReadInt64(info, VideoInfoOffsetsOffset), Marshal.ReadInt32(info, VideoInfoStridesOffset)),
            (Marshal.ReadInt64(info, VideoInfoOffsetsOffset + 8), Marshal.ReadInt32(info, VideoInfoStridesOffset + 4))
        ];
    }

    public override bool CopyTo(uint lumaTexture, uint chromaTexture)
    {
        try
        {
            if (cuCtxPushCurrent_v2(_cudaContext) != 0)
                return false;

            try
            {
                return CopyPlane(lumaTexture, _planes[0], Width, Height) &&
                       CopyPlane(chromaTexture, _planes[1], (Width + 1) / 2 * 2, (Height + 1) / 2);
            }
            finally
            {
                cuCtxPopCurrent_v2(out _);
            }
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <param name="widthInBytes">Bytes of a row: the luma plane has one per pixel, the chroma one two per pair.</param>
    private unsafe bool CopyPlane(uint texture, (long Offset, int Stride) plane, int widthInBytes, int height)
    {
        if (!Registrations.TryGetValue((texture, _cudaContext), out var resource))
        {
            if (cuGraphicsGLRegisterImage(out resource, texture, GlTexture2D, RegisterWriteDiscard) != 0)
                return false;

            Registrations[(texture, _cudaContext)] = resource;
        }

        var resources = resource;

        if (cuGraphicsMapResources(1, &resources, IntPtr.Zero) != 0)
            return false;

        try
        {
            if (cuGraphicsSubResourceGetMappedArray(out var array, resource, 0, 0) != 0)
                return false;

            var copy = new CudaMemcpy2D
            {
                SrcMemoryType = MemoryTypeDevice,
                SrcDevice = (nuint)(_device + (nint)plane.Offset),
                SrcPitch = (nuint)plane.Stride,
                DstMemoryType = MemoryTypeArray,
                DstArray = array,
                WidthInBytes = (nuint)widthInBytes,
                Height = (nuint)height
            };

            return cuMemcpy2D_v2(&copy) == 0;
        }
        finally
        {
            cuGraphicsUnmapResources(1, &resources, IntPtr.Zero);
        }
    }

    private static void Unregister(uint texture)
    {
        foreach (var key in Registrations.Keys)
        {
            if (key.Texture != texture || !Registrations.TryRemove(key, out var resource))
                continue;

            try
            {
                if (cuCtxPushCurrent_v2(key.Context) != 0)
                    continue;

                cuGraphicsUnregisterResource(resource);
                cuCtxPopCurrent_v2(out _);
            }
            catch (DllNotFoundException)
            {
            }
        }
    }

    protected override void Release()
    {
        var sample = Interlocked.Exchange(ref _sample, IntPtr.Zero);

        if (sample == IntPtr.Zero)
            return;

        gst_memory_unmap(_memory, _mapInfo);
        Marshal.FreeHGlobal(_mapInfo);
        GstVideoNative.Unref(sample);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CudaMemcpy2D
    {
        public nuint SrcXInBytes;
        public nuint SrcY;
        public int SrcMemoryType;
        public IntPtr SrcHost;
        public nuint SrcDevice;
        public IntPtr SrcArray;
        public nuint SrcPitch;
        public nuint DstXInBytes;
        public nuint DstY;
        public int DstMemoryType;
        public IntPtr DstHost;
        public nuint DstDevice;
        public IntPtr DstArray;
        public nuint DstPitch;
        public nuint WidthInBytes;
        public nuint Height;
    }

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_buffer_peek_memory(IntPtr buffer, uint index);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_memory_map(IntPtr memory, IntPtr info, int flags);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_memory_unmap(IntPtr memory, IntPtr info);

    [DllImport(NativeLibraries.GstCuda)]
    private static extern int gst_is_cuda_memory(IntPtr memory);

    [DllImport(NativeLibraries.GstCuda)]
    private static extern int gst_cuda_memory_sync(IntPtr memory);

    [DllImport(NativeLibraries.GstCuda)]
    private static extern IntPtr gst_cuda_context_get_handle(IntPtr context);

    [DllImport(CudaLibrary)]
    private static extern int cuCtxPushCurrent_v2(IntPtr context);

    [DllImport(CudaLibrary)]
    private static extern int cuCtxPopCurrent_v2(out IntPtr context);

    [DllImport(CudaLibrary)]
    private static extern int cuGraphicsGLRegisterImage(out IntPtr resource, uint image, uint target, uint flags);

    [DllImport(CudaLibrary)]
    private static extern int cuGraphicsUnregisterResource(IntPtr resource);

    [DllImport(CudaLibrary)]
    private static extern unsafe int cuGraphicsMapResources(uint count, IntPtr* resources, IntPtr stream);

    [DllImport(CudaLibrary)]
    private static extern unsafe int cuGraphicsUnmapResources(uint count, IntPtr* resources, IntPtr stream);

    [DllImport(CudaLibrary)]
    private static extern int cuGraphicsSubResourceGetMappedArray(out IntPtr array, IntPtr resource, uint index, uint level);

    [DllImport(CudaLibrary)]
    private static extern unsafe int cuMemcpy2D_v2(CudaMemcpy2D* copy);
}

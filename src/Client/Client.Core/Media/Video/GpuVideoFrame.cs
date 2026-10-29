namespace Client.Core.Media;

/// <summary>
/// How the colors of a YUV frame are turned into RGB.
/// </summary>
public enum VideoColorMatrix
{
    Bt709,
    Bt601
}

/// <summary>
/// A decoded frame that stays in GPU memory. It holds the buffer of the decoder, which is reused
/// once every reference is given back.
/// </summary>
public abstract class GpuVideoFrame : IDisposable
{
    private int _references = 1;

    protected GpuVideoFrame(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public VideoColorMatrix Matrix { get; init; } = VideoColorMatrix.Bt709;

    /// <summary>The values use the whole range rather than the 16-235 one of video.</summary>
    public bool IsFullRange { get; init; }

    /// <summary>
    /// Takes one more reference, given back by <see cref="Dispose"/>.
    /// </summary>
    /// <returns>Null when the frame was released already.</returns>
    public GpuVideoFrame? Retain()
    {
        while (true)
        {
            var current = Volatile.Read(ref _references);

            if (current <= 0)
                return null;

            if (Interlocked.CompareExchange(ref _references, current + 1, current) == current)
                return this;
        }
    }

    /// <summary>
    /// Gives back one reference; the buffer goes back to the decoder with the last one.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Decrement(ref _references) == 0)
            Release();
    }

    protected abstract void Release();
}

/// <summary>
/// One plane of a DMA-BUF frame.
/// </summary>
public readonly record struct DmaBufPlane(int Fd, uint Offset, uint Pitch);

/// <summary>
/// A frame in DMA-BUF memory of the decoder, imported into OpenGL through EGL.
/// </summary>
public abstract class DmaBufVideoFrame : GpuVideoFrame
{
    // fourcc of the DRM formats the frames come in
    public const uint FourccNv12 = 0x3231564E;
    public const uint FourccP010 = 0x30313050;

    protected DmaBufVideoFrame(int width, int height, uint fourcc, ulong modifier, IReadOnlyList<DmaBufPlane> planes)
        : base(width, height)
    {
        Fourcc = fourcc;
        Modifier = modifier;
        Planes = planes;
    }

    /// <summary>The DRM format of the whole frame.</summary>
    public uint Fourcc { get; }

    /// <summary>The DRM modifier, which says how the planes are tiled.</summary>
    public ulong Modifier { get; }

    /// <summary>The luma plane first, then the chroma one.</summary>
    public IReadOnlyList<DmaBufPlane> Planes { get; }
}

/// <summary>
/// A frame that the decoder copies into OpenGL textures itself, on the GPU.
/// </summary>
public abstract class GlCopyVideoFrame : GpuVideoFrame
{
    protected GlCopyVideoFrame(int width, int height) : base(width, height)
    {
    }

    /// <summary>
    /// Copies the luma plane into an R8 texture and the chroma plane into an RG8 texture of half
    /// the size, both already allocated. Called on the thread whose OpenGL context owns them.
    /// </summary>
    /// <returns>False when the copy failed, in which case it will not work for later frames either.</returns>
    public abstract bool CopyTo(uint lumaTexture, uint chromaTexture);

    /// <summary>
    /// Raised before textures that frames were copied into are deleted, on the thread of their
    /// context, so whatever the decoder registered for them is let go.
    /// </summary>
    public static event Action<uint>? TextureDeleting;

    public static void NotifyTextureDeleting(uint texture) => TextureDeleting?.Invoke(texture);
}

/// <summary>
/// A BGRA frame in a Direct3D 11 texture that another device can open through its share handle;
/// access is ordered by the keyed mutex of the texture.
/// </summary>
public abstract class D3D11SharedVideoFrame : GpuVideoFrame
{
    protected D3D11SharedVideoFrame(int width, int height, IntPtr sharedHandle) : base(width, height)
    {
        SharedHandle = sharedHandle;
    }

    /// <summary>
    /// The share handle of the texture, of the older kind rather than an NT handle. It stays the same
    /// for every frame written into that texture, so an importer opens it once.
    /// </summary>
    public IntPtr SharedHandle { get; }

    /// <summary>The key the reader acquires: the writer released the texture with it.</summary>
    public const uint ReaderKey = 1;

    /// <summary>The key the reader releases the texture with, for the writer to take it again.</summary>
    public const uint WriterKey = 0;

    /// <summary>
    /// Set by the reader once it acquired the texture, so the writer knows the reader gives it back
    /// with <see cref="WriterKey"/>; a frame that was never read is taken back by the writer itself.
    /// </summary>
    public void MarkRead() => Interlocked.Exchange(ref _isRead, 1);

    protected bool IsRead => Volatile.Read(ref _isRead) == 1;

    private int _isRead;
}

/// <summary>
/// How the planes of a <see cref="MemoryYuvVideoFrame"/> hold the colors.
/// </summary>
public enum YuvPlaneLayout
{
    /// <summary>Luma, then U and V interleaved in one plane of half the size.</summary>
    Nv12,

    /// <summary>Luma, then U and V in a plane each, of half the size.</summary>
    I420
}

/// <summary>
/// One plane of a frame in memory: where its first row starts and how many bytes a row takes.
/// </summary>
public readonly record struct VideoPlane(IntPtr Data, int Stride);

/// <summary>
/// A frame whose 8-bit YUV planes stay in the memory of the decoder. Its pixels are not on the GPU,
/// but it is held the same way: the buffer stays mapped until the last reference is given back, so
/// the renderer uploads the planes without a copy before.
/// </summary>
public abstract class MemoryYuvVideoFrame : GpuVideoFrame
{
    protected MemoryYuvVideoFrame(int width, int height, YuvPlaneLayout layout, IReadOnlyList<VideoPlane> planes)
        : base(width, height)
    {
        Layout = layout;
        Planes = planes;
    }

    public YuvPlaneLayout Layout { get; }

    /// <summary>Luma first, then the chroma plane of NV12, or U and V of I420.</summary>
    public IReadOnlyList<VideoPlane> Planes { get; }
}

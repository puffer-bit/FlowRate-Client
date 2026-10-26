using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Client.Core.Media;
using SkiaSharp;

namespace Client.Views.Sessions.Hybrid.Video;

/// <summary>
/// Draws the frames of one stream on the render thread of the compositor, at every refresh of the
/// monitor, whatever the UI thread is busy with.
/// </summary>
/// <remarks>
/// A frame that is due is brought into textures of the OpenGL context the user interface renders
/// with: DMA-BUF planes through EGL images, CUDA frames copied on the GPU, Direct3D 11 frames copied
/// inside ANGLE, frames in memory uploaded. A shader turns it into an RGB texture of its own
/// framebuffer, which Skia then draws like any image. Skia is told afterwards that its OpenGL state
/// was changed under it.
/// </remarks>
internal sealed unsafe class GlVideoRenderer : CompositionCustomVisualHandler
{
    public sealed record StretchMessage(StretchMode Stretch);
    public sealed record ClearMessage;
    public sealed record StartMessage;
    public sealed record StopMessage;

    // Fourcc of the single-plane formats the planes of NV12 and P010 are imported as
    private const uint FourccR8 = 0x20203852;
    private const uint FourccGr88 = 0x38385247;
    private const uint FourccR16 = 0x20363152;
    private const uint FourccGr1616 = 0x32335247;
    private const uint Rgba8 = 0x8058;

    // Every context of the compositor shares one group, so what a stopped renderer leaves behind is
    // deleted by the next one that renders
    private static readonly ConcurrentQueue<(uint Texture, uint Framebuffer)> Orphans = new();

    private readonly VideoFrameQueue _queue;
    private readonly Action _onBroken;

    private GlFunctions? _gl;
    private GlInterface? _glInterface;
    private bool _isBroken;
    private bool _isRunning;
    private StretchMode _stretch = StretchMode.Uniform;

    private uint _vertexArray;
    private uint _yuvProgram;
    private uint _rgbProgram;
    private int _yuvMatrix;
    private int _yuvOffset;
    private int _yuvPlanar;
    private int _rgbSwap;

    // Inputs: a BGRA frame in memory, YUV planes copied or uploaded (V apart for I420), imported
    // planes, an imported Direct3D 11 frame
    private uint _rgbTexture;
    private PixelSize _rgbSize;
    private uint _lumaTexture;
    private uint _chromaTexture;
    private uint _chromaVTexture;
    private PixelSize _yuvSize;
    private bool _isYuvPlanar;
    private uint _importedLuma;
    private uint _importedChroma;
    private uint _importedRgb;
    private IntPtr _eglDisplay;
    private IntPtr _lumaImage;
    private IntPtr _chromaImage;
    private AngleD3D11Import? _angle;

    // The RGB picture that is drawn, rendered once per frame
    private uint _outputTexture;
    private uint _outputFramebuffer;
    private PixelSize _outputSize;
    private bool _hasPicture;

    // On screen until the next one replaces it, so its buffer is not reused while it is read
    private PendingFrame? _current;

    /// <param name="onBroken">Called on the render thread once OpenGL turned out not to be usable.</param>
    public GlVideoRenderer(VideoFrameQueue queue, Action onBroken)
    {
        _queue = queue;
        _onBroken = onBroken;
    }

    public override void OnMessage(object message)
    {
        switch (message)
        {
            case StretchMessage stretch:
                _stretch = stretch.Stretch;
                Invalidate();
                break;
            case ClearMessage:
                ReleaseCurrent();
                _hasPicture = false;
                Invalidate();
                break;
            case StartMessage when !_isRunning:
                _isRunning = true;
                RegisterForNextAnimationFrameUpdate();
                break;
            case StopMessage:
                _isRunning = false;
                ReleaseCurrent();
                LeaveResources();
                break;
        }
    }

    // On every refresh: a frame that waits may be due now
    public override void OnAnimationFrameUpdate()
    {
        if (!_isRunning)
            return;

        if (_queue.HasPending)
            Invalidate();

        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (_isBroken || context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature feature)
            return;

        using var lease = feature.Lease();

        if (lease.GrContext is not { } grContext)
        {
            Break();
            return;
        }

        var drewSomething = false;

        using (var api = lease.TryLeasePlatformGraphicsApi())
        {
            if (api?.Context is not IGlContext glContext)
            {
                Break();
                return;
            }

            if (_gl == null && !Initialize(glContext.GlInterface))
                return;

            DeleteOrphans();

            if (_queue.Pick() is { } next)
            {
                // What Skia still holds queued goes out before the state changes under it
                grContext.Flush();
                _gl!.ResetDrawState();

                if (Bring(next))
                    drewSomething = RenderPicture();
                else
                    next.Release();

                // Skia finds its own state again on the next draw
                grContext.ResetContext();
            }
        }

        if (_hasPicture || drewSomething)
            DrawPicture(lease, grContext);
    }

    private bool Initialize(GlInterface glInterface)
    {
        _glInterface = glInterface;
        _gl = GlFunctions.Load(glInterface);

        if (_gl == null)
        {
            Break();
            return false;
        }

        var version = glInterface.ContextInfo.Version;
        var (header, precision) = version.Type == GlProfileType.OpenGLES
            ? ("#version 300 es\n", "precision mediump float;\n")
            : ("#version 150\n", "");

        _yuvProgram = _gl.BuildProgram(header + VertexShader, header + precision + YuvShader, out _);
        _rgbProgram = _gl.BuildProgram(header + VertexShader, header + precision + RgbShader, out _);

        if (_yuvProgram == 0 || _rgbProgram == 0)
        {
            Break();
            return false;
        }

        _gl.UseProgram(_yuvProgram);
        _gl.Uniform1i(_gl.Uniform(_yuvProgram, "luma"), 0);
        _gl.Uniform1i(_gl.Uniform(_yuvProgram, "chroma"), 1);
        _gl.Uniform1i(_gl.Uniform(_yuvProgram, "chromaV"), 2);
        _yuvPlanar = _gl.Uniform(_yuvProgram, "planarChroma");
        _yuvMatrix = _gl.Uniform(_yuvProgram, "yuvToRgb");
        _yuvOffset = _gl.Uniform(_yuvProgram, "yuvOffset");
        _gl.UseProgram(_rgbProgram);
        _gl.Uniform1i(_gl.Uniform(_rgbProgram, "picture"), 0);
        _rgbSwap = _gl.Uniform(_rgbProgram, "swapRedBlue");
        _gl.UseProgram(0);

        uint vertexArray;
        _gl.GenVertexArrays(1, &vertexArray);
        _vertexArray = vertexArray;

        if (OperatingSystem.IsLinux() && _gl.EglImageTargetTexture2D != null)
            _eglDisplay = EglDmaBuf.GetImportingDisplay();

        if (OperatingSystem.IsWindows() && _gl.EglImageTargetTexture2D != null)
            _angle = AngleD3D11Import.Create(glInterface);

        return true;
    }

    private void Break()
    {
        if (_isBroken)
            return;

        _isBroken = true;
        _onBroken();
    }

    // Brings a frame into the input textures; the previous frame is let go
    private bool Bring(PendingFrame frame)
    {
        ReleaseCurrent();

        var isBrought = frame.Gpu switch
        {
            DmaBufVideoFrame dmaBuf => Import(dmaBuf),
            GlCopyVideoFrame copy => CopyYuv(copy),
            D3D11SharedVideoFrame shared => ImportD3D11(shared),
            MemoryYuvVideoFrame yuv => UploadYuv(yuv),
            null => UploadRgb(frame),
            _ => false
        };

        if (isBrought)
            _current = frame;

        return isBrought;
    }

    private bool UploadRgb(PendingFrame frame)
    {
        var gl = _gl!;

        if (frame.Pixels == null)
            return false;

        var size = new PixelSize(frame.Width, frame.Height);

        if (_rgbTexture == 0)
            _rgbTexture = gl.CreateTexture();

        gl.ActiveTexture(GlFunctions.Texture0);
        gl.BindTexture(GlFunctions.Texture2D, _rgbTexture);
        gl.PixelStorei(GlFunctions.UnpackAlignment, 4);

        fixed (byte* pixels = frame.Pixels)
        {
            // BGRA bytes go in as RGBA, the shader swaps the channels back
            if (size != _rgbSize)
            {
                gl.TexImage2D(GlFunctions.Texture2D, 0, GlFunctions.Rgba8, size.Width, size.Height, 0,
                    GlFunctions.Rgba, GlFunctions.UnsignedByte, pixels);
                _rgbSize = size;
            }
            else
            {
                gl.TexSubImage2D(GlFunctions.Texture2D, 0, 0, 0, size.Width, size.Height,
                    GlFunctions.Rgba, GlFunctions.UnsignedByte, pixels);
            }
        }

        return true;
    }

    private bool Import(DmaBufVideoFrame frame)
    {
        var gl = _gl!;

        if (_eglDisplay == IntPtr.Zero || gl.EglImageTargetTexture2D == null || frame.Planes.Count < 2)
        {
            VideoSurfaceSupport.ReportFailure(VideoSurfaceKinds.DmaBuf);
            return false;
        }

        var isTenBit = frame.Fourcc == DmaBufVideoFrame.FourccP010;
        var luma = frame.Planes[0];
        var chroma = frame.Planes[1];

        _lumaImage = EglDmaBuf.CreateImage(_eglDisplay, frame.Width, frame.Height, isTenBit ? FourccR16 : FourccR8,
            luma.Fd, luma.Offset, luma.Pitch, frame.Modifier);
        _chromaImage = EglDmaBuf.CreateImage(_eglDisplay, (frame.Width + 1) / 2, (frame.Height + 1) / 2,
            isTenBit ? FourccGr1616 : FourccGr88, chroma.Fd, chroma.Offset, chroma.Pitch, frame.Modifier);

        if (_lumaImage == IntPtr.Zero || _chromaImage == IntPtr.Zero)
        {
            DestroyImages();
            VideoSurfaceSupport.ReportFailure(VideoSurfaceKinds.DmaBuf);
            return false;
        }

        if (_importedLuma == 0)
            _importedLuma = gl.CreateTexture();

        if (_importedChroma == 0)
            _importedChroma = gl.CreateTexture();

        gl.ActiveTexture(GlFunctions.Texture0);
        gl.BindTexture(GlFunctions.Texture2D, _importedLuma);
        gl.EglImageTargetTexture2D(GlFunctions.Texture2D, _lumaImage);
        gl.BindTexture(GlFunctions.Texture2D, _importedChroma);
        gl.EglImageTargetTexture2D(GlFunctions.Texture2D, _chromaImage);

        return true;
    }

    private bool CopyYuv(GlCopyVideoFrame frame)
    {
        EnsureYuvTextures(new PixelSize(frame.Width, frame.Height), false);

        if (frame.CopyTo(_lumaTexture, _chromaTexture))
            return true;

        VideoSurfaceSupport.ReportFailure(VideoSurfaceKinds.CudaGl);
        return false;
    }

    private bool UploadYuv(MemoryYuvVideoFrame frame)
    {
        var gl = _gl!;
        var size = new PixelSize(frame.Width, frame.Height);
        var chromaSize = new PixelSize((size.Width + 1) / 2, (size.Height + 1) / 2);
        var isPlanar = frame.Layout == YuvPlaneLayout.I420;

        EnsureYuvTextures(size, isPlanar);
        gl.PixelStorei(GlFunctions.UnpackAlignment, 1);

        // The rows are read straight from the buffer of the decoder, padding and all
        Upload(_lumaTexture, frame.Planes[0], size, GlFunctions.Red, 1);

        if (isPlanar)
        {
            Upload(_chromaTexture, frame.Planes[1], chromaSize, GlFunctions.Red, 1);
            Upload(_chromaVTexture, frame.Planes[2], chromaSize, GlFunctions.Red, 1);
        }
        else
        {
            Upload(_chromaTexture, frame.Planes[1], chromaSize, GlFunctions.Rg, 2);
        }

        gl.PixelStorei(GlFunctions.UnpackRowLength, 0);
        gl.PixelStorei(GlFunctions.UnpackAlignment, 4);
        return true;

        void Upload(uint texture, VideoPlane plane, PixelSize planeSize, int format, int bytesPerPixel)
        {
            gl.ActiveTexture(GlFunctions.Texture0);
            gl.BindTexture(GlFunctions.Texture2D, texture);
            gl.PixelStorei(GlFunctions.UnpackRowLength, plane.Stride / bytesPerPixel);
            gl.TexSubImage2D(GlFunctions.Texture2D, 0, 0, 0, planeSize.Width, planeSize.Height, format,
                GlFunctions.UnsignedByte, (void*)plane.Data);
        }
    }

    // Luma, and chroma as RG8 or, for planar frames, U and V as R8 each
    private void EnsureYuvTextures(PixelSize size, bool isPlanar)
    {
        if (size == _yuvSize && isPlanar == _isYuvPlanar && _lumaTexture != 0)
            return;

        var gl = _gl!;
        var (width, height) = ((size.Width + 1) / 2, (size.Height + 1) / 2);

        DeleteYuvTextures();

        _lumaTexture = gl.CreateTexture();
        gl.TexImage2D(GlFunctions.Texture2D, 0, GlFunctions.R8, size.Width, size.Height, 0,
            GlFunctions.Red, GlFunctions.UnsignedByte, null);
        _chromaTexture = gl.CreateTexture();

        if (isPlanar)
        {
            gl.TexImage2D(GlFunctions.Texture2D, 0, GlFunctions.R8, width, height, 0,
                GlFunctions.Red, GlFunctions.UnsignedByte, null);
            _chromaVTexture = gl.CreateTexture();
            gl.TexImage2D(GlFunctions.Texture2D, 0, GlFunctions.R8, width, height, 0,
                GlFunctions.Red, GlFunctions.UnsignedByte, null);
        }
        else
        {
            gl.TexImage2D(GlFunctions.Texture2D, 0, GlFunctions.Rg8, width, height, 0,
                GlFunctions.Rg, GlFunctions.UnsignedByte, null);
        }

        _yuvSize = size;
        _isYuvPlanar = isPlanar;
    }

    private bool ImportD3D11(D3D11SharedVideoFrame frame)
    {
        var gl = _gl!;

        if (_angle == null || gl.EglImageTargetTexture2D == null)
        {
            VideoSurfaceProbe.Logger?.LogWarning("Direct3D 11 frames cannot be imported: {Reason}",
                gl.EglImageTargetTexture2D == null ? "no glEGLImageTargetTexture2DOES" : AngleD3D11Import.LastFailure);
            VideoSurfaceSupport.ReportFailure(VideoSurfaceKinds.D3D11Shared);
            return false;
        }

        var image = _angle.Import(frame.SharedHandle, frame.Width, frame.Height, out var isBroken);

        if (isBroken)
        {
            VideoSurfaceProbe.Logger?.LogWarning("Direct3D 11 frame import failed: {Reason}", AngleD3D11Import.LastFailure);
            VideoSurfaceSupport.ReportFailure(VideoSurfaceKinds.D3D11Shared);
            return false;
        }

        // Not released in time: this one is skipped, the writer takes it back
        if (image == IntPtr.Zero)
            return false;

        frame.MarkRead();

        if (_importedRgb == 0)
            _importedRgb = gl.CreateTexture();

        gl.ActiveTexture(GlFunctions.Texture0);
        gl.BindTexture(GlFunctions.Texture2D, _importedRgb);
        gl.EglImageTargetTexture2D(GlFunctions.Texture2D, image);

        return true;
    }

    // Renders the input of the current frame into the RGB texture that is drawn
    private bool RenderPicture()
    {
        var gl = _gl!;
        var frame = _current!;
        var size = new PixelSize(frame.Width, frame.Height);

        if (size != _outputSize || _outputTexture == 0)
        {
            gl.DeleteFramebuffer(_outputFramebuffer);
            gl.DeleteTexture(_outputTexture);

            _outputTexture = gl.CreateTexture();
            gl.TexImage2D(GlFunctions.Texture2D, 0, GlFunctions.Rgba8, size.Width, size.Height, 0,
                GlFunctions.Rgba, GlFunctions.UnsignedByte, null);
            _outputFramebuffer = gl.GenFramebuffer();
            gl.BindFramebuffer(GlFunctions.Framebuffer, _outputFramebuffer);
            gl.FramebufferTexture2D(GlFunctions.Framebuffer, GlFunctions.ColorAttachment0, GlFunctions.Texture2D,
                _outputTexture, 0);

            if (gl.CheckFramebufferStatus(GlFunctions.Framebuffer) != GlFunctions.FramebufferComplete)
            {
                Break();
                return false;
            }

            _outputSize = size;
        }

        gl.BindFramebuffer(GlFunctions.Framebuffer, _outputFramebuffer);
        gl.Viewport(0, 0, size.Width, size.Height);

        switch (frame.Gpu)
        {
            case DmaBufVideoFrame:
                DrawYuv(frame.Gpu, _importedLuma, _importedChroma);
                break;
            case GlCopyVideoFrame:
                DrawYuv(frame.Gpu, _lumaTexture, _chromaTexture);
                break;
            case MemoryYuvVideoFrame:
                DrawYuv(frame.Gpu, _lumaTexture, _chromaTexture, _isYuvPlanar ? _chromaVTexture : 0);
                break;
            case D3D11SharedVideoFrame:
                DrawRgb(_importedRgb, false);
                break;
            default:
                DrawRgb(_rgbTexture, true);
                break;
        }

        gl.BindFramebuffer(GlFunctions.Framebuffer, 0);
        _hasPicture = true;
        return true;
    }

    /// <param name="chromaV">The V plane when U and V come apart, otherwise 0.</param>
    private void DrawYuv(GpuVideoFrame frame, uint luma, uint chroma, uint chromaV = 0)
    {
        var gl = _gl!;

        gl.UseProgram(_yuvProgram);
        SetColorConversion(frame.Matrix, frame.IsFullRange);
        gl.Uniform1i(_yuvPlanar, chromaV != 0 ? 1 : 0);
        gl.ActiveTexture(GlFunctions.Texture0);
        gl.BindTexture(GlFunctions.Texture2D, luma);
        gl.ActiveTexture(GlFunctions.Texture0 + 1);
        gl.BindTexture(GlFunctions.Texture2D, chroma);

        if (chromaV != 0)
        {
            gl.ActiveTexture(GlFunctions.Texture0 + 2);
            gl.BindTexture(GlFunctions.Texture2D, chromaV);
        }

        DrawQuad();
    }

    private void DrawRgb(uint texture, bool swapRedBlue)
    {
        var gl = _gl!;

        gl.UseProgram(_rgbProgram);
        gl.Uniform1i(_rgbSwap, swapRedBlue ? 1 : 0);
        gl.ActiveTexture(GlFunctions.Texture0);
        gl.BindTexture(GlFunctions.Texture2D, texture);
        DrawQuad();
    }

    private void DrawQuad()
    {
        var gl = _gl!;

        gl.BindVertexArray(_vertexArray);
        gl.DrawArrays(GlFunctions.TriangleStrip, 0, 4);
        gl.BindVertexArray(0);
        gl.ActiveTexture(GlFunctions.Texture0);
        gl.UseProgram(0);
    }

    // BT.709 or BT.601, with the scale of the limited range folded into the matrix
    private void SetColorConversion(VideoColorMatrix matrix, bool isFullRange)
    {
        var gl = _gl!;
        var (luma, chroma) = isFullRange ? (1f, 1f) : (255f / 219f, 255f / 224f);
        var (rv, gu, gv, bu) = matrix == VideoColorMatrix.Bt601
            ? (1.402f, -0.344136f, -0.714136f, 1.772f)
            : (1.5748f, -0.1873f, -0.4681f, 1.8556f);

        // Column by column: what Y, U and V each add to red, green and blue
        var values = stackalloc float[]
        {
            luma, luma, luma,
            0, gu * chroma, bu * chroma,
            rv * chroma, gv * chroma, 0
        };

        gl.UniformMatrix3fv(_yuvMatrix, 1, 0, values);
        gl.Uniform3f(_yuvOffset, isFullRange ? 0 : 16f / 255f, 128f / 255f, 128f / 255f);
    }

    private void DrawPicture(ISkiaSharpApiLease lease, GRContext grContext)
    {
        if (_outputTexture == 0)
            return;

        var size = _outputSize;
        using var texture = new GRBackendTexture(size.Width, size.Height, false,
            new GRGlTextureInfo(GlFunctions.Texture2D, _outputTexture, Rgba8));

        // The framebuffer holds the first row of the picture at its bottom
        using var image = SKImage.FromTexture(grContext, texture, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);

        if (image == null)
            return;

        var place = VideoLayout.Fit(new Rect(0, 0, EffectiveSize.X, EffectiveSize.Y), size.Width, size.Height, _stretch);

        lease.SkCanvas.DrawImage(image, new SKRect(0, 0, size.Width, size.Height),
            new SKRect((float)place.X, (float)place.Y, (float)place.Right, (float)place.Bottom),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
    }

    private void ReleaseCurrent()
    {
        DestroyImages();
        _current?.Release();
        _current = null;
    }

    private void DestroyImages()
    {
        EglDmaBuf.DestroyImage(_eglDisplay, _lumaImage);
        EglDmaBuf.DestroyImage(_eglDisplay, _chromaImage);
        _lumaImage = _chromaImage = IntPtr.Zero;
    }

    private void DeleteYuvTextures()
    {
        foreach (var texture in (ReadOnlySpan<uint>)[_lumaTexture, _chromaTexture, _chromaVTexture])
        {
            if (texture == 0)
                continue;

            GlCopyVideoFrame.NotifyTextureDeleting(texture);
            _gl?.DeleteTexture(texture);
        }

        _lumaTexture = _chromaTexture = _chromaVTexture = 0;
        _yuvSize = default;
    }

    // Without a context to delete them in, the objects go to the next renderer that has one
    private void LeaveResources()
    {
        foreach (var texture in (ReadOnlySpan<uint>)[_rgbTexture, _importedLuma, _importedChroma, _importedRgb, _outputTexture])
        {
            if (texture != 0)
                Orphans.Enqueue((texture, 0));
        }

        foreach (var texture in (ReadOnlySpan<uint>)[_lumaTexture, _chromaTexture, _chromaVTexture])
        {
            if (texture == 0)
                continue;

            GlCopyVideoFrame.NotifyTextureDeleting(texture);
            Orphans.Enqueue((texture, 0));
        }

        if (_outputFramebuffer != 0)
            Orphans.Enqueue((0, _outputFramebuffer));

        if (_yuvProgram != 0)
            Orphans.Enqueue((uint.MaxValue, _yuvProgram));

        if (_rgbProgram != 0)
            Orphans.Enqueue((uint.MaxValue, _rgbProgram));

        _angle?.Dispose();
        _angle = null;
        _rgbTexture = _importedLuma = _importedChroma = _importedRgb = _outputTexture = _outputFramebuffer = 0;
        _lumaTexture = _chromaTexture = _chromaVTexture = _yuvProgram = _rgbProgram = 0;
        _rgbSize = _yuvSize = _outputSize = default;
        _hasPicture = false;
        _gl = null;
    }

    private void DeleteOrphans()
    {
        var gl = _gl!;

        while (Orphans.TryDequeue(out var orphan))
        {
            if (orphan.Texture == uint.MaxValue)
                gl.DeleteProgram(orphan.Framebuffer);
            else if (orphan.Texture != 0)
                gl.DeleteTexture(orphan.Texture);
            else
                gl.DeleteFramebuffer(orphan.Framebuffer);
        }
    }

    // A strip over the whole viewport, without vertex data; the first row of the picture is at the top
    private const string VertexShader = """
        out vec2 uv;
        void main()
        {
            vec2 corner = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1));
            uv = vec2(corner.x, 1.0 - corner.y);
            gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string YuvShader = """
        uniform sampler2D luma;
        uniform sampler2D chroma;
        uniform sampler2D chromaV;
        uniform int planarChroma;
        uniform mat3 yuvToRgb;
        uniform vec3 yuvOffset;
        in vec2 uv;
        out vec4 color;
        void main()
        {
            vec2 uvValues = planarChroma != 0
                ? vec2(texture(chroma, uv).r, texture(chromaV, uv).r)
                : texture(chroma, uv).rg;
            vec3 yuv = vec3(texture(luma, uv).r, uvValues) - yuvOffset;
            color = vec4(clamp(yuvToRgb * yuv, 0.0, 1.0), 1.0);
        }
        """;

    private const string RgbShader = """
        uniform sampler2D picture;
        uniform int swapRedBlue;
        in vec2 uv;
        out vec4 color;
        void main()
        {
            vec4 texel = texture(picture, uv);
            color = vec4(swapRedBlue != 0 ? texel.bgr : texel.rgb, 1.0);
        }
        """;
}

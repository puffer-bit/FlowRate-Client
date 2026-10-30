using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Client.Core.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Client.Avalonia.Video;

/// <summary>
/// Finds out once, when the main window opens, how this system can show frames: which presenter
/// draws them and what may reach it without being copied through memory.
/// </summary>
public static partial class VideoSurfaceProbe
{
    private static bool _isDone;

    /// <summary>Frames are drawn with OpenGL, see <see cref="GlVideoPresenter"/>.</summary>
    public static bool UsesGl { get; private set; }

    /// <summary>Where presenters report why a fast path failed.</summary>
    public static ILogger Logger { get; private set; } = NullLogger.Instance;

    public static async Task RunAsync(Visual window, ILogger? logger)
    {
        if (_isDone || ElementComposition.GetElementVisual(window)?.Compositor is not { } compositor)
            return;

        _isDone = true;
        Logger = logger ?? NullLogger.Instance;
        var kinds = VideoSurfaceKinds.None;

        try
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                kinds = await ProbeGlAsync(compositor, Logger);
        }
        catch (Exception exception)
        {
            LogProbeFailed(Logger, exception);
            UsesGl = false;
            kinds = VideoSurfaceKinds.None;
        }

        // Any OpenGL presenter uploads YUV planes and converts them itself
        if (UsesGl)
            kinds |= VideoSurfaceKinds.YuvMemory;

        VideoSurfaceSupport.Set(kinds);
        LogProbed(Logger, UsesGl ? "OpenGL" : "bitmap", kinds);
    }

    private static async Task<VideoSurfaceKinds> ProbeGlAsync(Compositor compositor, ILogger logger)
    {
        var feature = await compositor.TryGetRenderInterfaceFeature(typeof(IOpenGlTextureSharingRenderInterfaceContextFeature));

        if (feature is not IOpenGlTextureSharingRenderInterfaceContextFeature sharing)
        {
            LogNoSharing(logger, feature?.GetType().FullName ?? "none");
            return VideoSurfaceKinds.None;
        }

        if (!sharing.CanCreateSharedContext || sharing.CreateSharedContext() is not { } context)
        {
            LogNoSharedContext(logger, sharing.CanCreateSharedContext);

            // ANGLE refuses shared contexts, but the presenter draws in the renderer's own context, so it
            // still uploads YUV and RGB frames, and lets ANGLE open D3D11 frames by their share
            // handles. Whether ANGLE can is only found out there, with the first frame: a missing
            // extension or a texture it cannot open is reported as a failure, and the stream falls back
            // to memory. (Copying into ANGLE's device behind its back instead showed black pictures and
            // crashed the AMD driver.)
            if (!OperatingSystem.IsWindows())
                return VideoSurfaceKinds.None;

            UsesGl = true;
            return VideoSurfaceKinds.D3D11Shared;
        }

        var kinds = VideoSurfaceKinds.None;

        using (context)
        using (context.MakeCurrent())
        {
            UsesGl = true;
            var canImport = context.GlInterface.GetProcAddress("glEGLImageTargetTexture2DOES") != IntPtr.Zero;
            LogSharedContext(logger, context.Version, canImport);

            if (OperatingSystem.IsLinux() && canImport && EglDmaBuf.GetImportingDisplay() != IntPtr.Zero)
                kinds |= VideoSurfaceKinds.DmaBuf;

            // ANGLE on Direct3D 11, which Avalonia renders with on Windows
            if (OperatingSystem.IsWindows() && AngleD3D11Import.IsSupported(context.GlInterface))
                kinds |= VideoSurfaceKinds.D3D11Shared;
        }

        // The NVIDIA driver brings the library; whether the copy works shows with the first frame
        if (OperatingSystem.IsLinux() && NativeLibrary.TryLoad("libcuda.so.1", out var cuda))
        {
            NativeLibrary.Free(cuda);
            kinds |= VideoSurfaceKinds.CudaGl;
        }

        return kinds;
    }
}

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

namespace Client.Views.Sessions.Hybrid.Video;

/// <summary>
/// Finds out once, when the main window opens, how this system can show frames: which presenter
/// draws them and what may reach it without being copied through memory.
/// </summary>
internal static class VideoSurfaceProbe
{
    private static bool _isDone;

    /// <summary>Frames are drawn with OpenGL, see <see cref="GlVideoPresenter"/>.</summary>
    public static bool UsesGl { get; private set; }

    /// <summary>Where presenters report why a fast path failed.</summary>
    public static ILogger? Logger { get; private set; }

    public static async Task RunAsync(Visual window, ILogger? logger)
    {
        if (_isDone || ElementComposition.GetElementVisual(window)?.Compositor is not { } compositor)
            return;

        _isDone = true;
        Logger = logger;
        var kinds = VideoSurfaceKinds.None;

        try
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                kinds = await ProbeGlAsync(compositor, logger);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Failed to find out how video can reach the screen, frames are copied");
            UsesGl = false;
            kinds = VideoSurfaceKinds.None;
        }

        // Any OpenGL presenter uploads YUV planes and converts them itself
        if (UsesGl)
            kinds |= VideoSurfaceKinds.YuvMemory;

        VideoSurfaceSupport.Set(kinds);
        logger?.LogInformation("Video presenter: {Presenter}, frames without copying: {Kinds}",
            UsesGl ? "OpenGL" : "bitmap", kinds);
    }

    private static async Task<VideoSurfaceKinds> ProbeGlAsync(Compositor compositor, ILogger? logger)
    {
        var feature = await compositor.TryGetRenderInterfaceFeature(typeof(IOpenGlTextureSharingRenderInterfaceContextFeature));

        if (feature is not IOpenGlTextureSharingRenderInterfaceContextFeature sharing)
        {
            logger?.LogInformation("The renderer shares no OpenGL context: {Feature}", feature?.GetType().FullName ?? "none");
            return VideoSurfaceKinds.None;
        }

        if (!sharing.CanCreateSharedContext || sharing.CreateSharedContext() is not { } context)
        {
            logger?.LogInformation("The renderer cannot create a shared OpenGL context ({CanCreate})",
                sharing.CanCreateSharedContext);

            // ANGLE refuses shared contexts, but the presenter draws in the renderer's own context, so it
            // still uploads YUV and RGB frames. D3D11 frames are not taken there: copying into ANGLE's
            // device behind its back showed black pictures and crashed the AMD driver, not reported as
            // a failed import that would fall back
            if (!OperatingSystem.IsWindows())
                return VideoSurfaceKinds.None;

            UsesGl = true;
            return VideoSurfaceKinds.None;
        }

        var kinds = VideoSurfaceKinds.None;

        using (context)
        using (context.MakeCurrent())
        {
            UsesGl = true;
            var canImport = context.GlInterface.GetProcAddress("glEGLImageTargetTexture2DOES") != IntPtr.Zero;
            logger?.LogInformation("Shared OpenGL context: {Version}, image import {CanImport}",
                context.Version, canImport);

            if (OperatingSystem.IsLinux() && canImport && EglDmaBuf.GetImportingDisplay() != IntPtr.Zero)
                kinds |= VideoSurfaceKinds.DmaBuf;

            // ANGLE on Direct3D 11, which Avalonia renders with on Windows
            if (OperatingSystem.IsWindows() && canImport && AngleD3D11Import.IsSupported(context.GlInterface))
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

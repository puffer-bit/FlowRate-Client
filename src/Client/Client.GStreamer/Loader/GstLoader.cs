using System.Runtime.InteropServices;
using Client.Tools.Environment;
using Client.Tools.AppInfo;
using Microsoft.Extensions.Logging;
using Client.GStreamer.Interop;

namespace Client.GStreamer.Loader;

public partial class GstLoader
{
    public static bool LoadGstreamer(ILogger logger)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            return false;

        // Without a runtime the first call below fails with DllNotFoundException, so say why up front
        if (GstNativeLibraryResolver.LibraryDirectory == null)
        {
            if (GstNativeLibraryResolver.UsesInstalledRuntime)
                LogInstalledRuntimeNotFound(logger);
            else
                LogRuntimeNotFound(logger, GstNativeLibraryResolver.ExpectedBundledRoot);
            return false;
        }

        try
        {
            INativeEnvironment.Set("NICE_DISABLE_IPV6", "1");
            INativeEnvironment.Set("NICE_DISABLE_UPNP", "1");

            // The installed runtime keeps its own plugins and registry
            if (GstNativeLibraryResolver.BundledRoot is { } bundledRoot)
                IsolateFromSystem(bundledRoot, logger);

            // The binding looks for the libraries itself, so it is pointed at the runtime the
            // resolver found. A module that is never initialized hands its objects out as plain
            // elements and boxed values
            var options = new Gst.GstSharpOptions { NativeSearchPath = GstNativeLibraryResolver.LibraryDirectory };
            GstSharp.Initialize(options);
            Gst.App.GstApp.Initialize(options);
            Gst.Sdp.GstSdp.Initialize(options);
            Gst.WebRTC.GstWebRTC.Initialize(options);
            GstLogFilter.Install(logger);

            LogInitialized(logger, Gst.Global.VersionString(), GstNativeLibraryResolver.LibraryDirectory);

            return GstElementCheck.Run(logger, GstNativeLibraryResolver.IsBundled);
        }
        catch (Exception exception)
        {
            LogInitializationFailed(logger, exception);
            return false;
        }
    }

    /// <summary>
    /// Points GStreamer at the shipped plugins only, so an installed GStreamer of another version can
    /// neither add its plugins nor share the registry. Must run before initialization; on Linux the
    /// variables go through setenv, since GLib reads the native environment.
    /// </summary>
    private static void IsolateFromSystem(string bundledRoot, ILogger logger)
    {
        var cacheDirectory = Path.Combine(AppDirectories.Data, "gstreamer");

        // Loading no GIO modules at all: the system ones are built against the system GLib
        var emptyGioModules = Path.Combine(cacheDirectory, "gio-modules");
        Directory.CreateDirectory(emptyGioModules);

        var scanner = Path.Combine(bundledRoot, "libexec", "gstreamer-1.0",
            OperatingSystem.IsWindows() ? "gst-plugin-scanner.exe" : "gst-plugin-scanner");

        var pluginPath = Path.Combine(bundledRoot, "lib", "gstreamer-1.0");
        if (OperatingSystem.IsLinux())
        {
            pluginPath += Path.PathSeparator + Path.Combine(bundledRoot, "lib", SelectVaPluginFolder(logger));
            pluginPath += Path.PathSeparator + Path.Combine(bundledRoot, "lib", SelectPipeWirePluginFolder(logger));
        }

        INativeEnvironment.Set("GST_PLUGIN_SYSTEM_PATH_1_0", pluginPath);
        INativeEnvironment.Set("GST_PLUGIN_PATH_1_0", "");
        INativeEnvironment.Set("GST_PLUGIN_SCANNER_1_0", scanner);
        INativeEnvironment.Set("GST_REGISTRY_1_0", Path.Combine(cacheDirectory, "registry.x86_64.bin"));
        INativeEnvironment.Set("GIO_MODULE_DIR", emptyGioModules);
    }

    /// <summary>
    /// The bundle has the VA-API plugin in two builds, and only one of them may be on the plugin path.
    /// The base one runs with any libva and already encodes AV1. The modern one also asks the driver
    /// for the encoder surface alignment, which AV1 on AMD needs, but calls vaMapBuffer2 from libva 2.21+.
    /// Without a libva at all the base one is taken and simply registers no elements.
    /// </summary>
    private static string SelectVaPluginFolder(ILogger logger)
    {
        const string baseFolder = "gstreamer-1.0-va";
        const string modernFolder = "gstreamer-1.0-va-modern";

        if (!NativeLibrary.TryLoad("libva.so.2", out var libva))
            return baseFolder;

        try
        {
            var folder = NativeLibrary.TryGetExport(libva, "vaMapBuffer2", out _) ? modernFolder : baseFolder;
            LogVaPluginBuild(logger, folder);
            return folder;
        }
        finally
        {
            NativeLibrary.Free(libva);
        }
    }

    /// <summary>
    /// The PipeWire plugin also comes in two builds. The base one runs with libpipewire 0.3.48 and hands
    /// DMA-BUF frames over without their modifier, which the VA postprocessor cannot take. The modern one
    /// negotiates modifiers (DMA_DRM caps) but calls functions that only libpipewire 1.2+ has.
    /// </summary>
    private static unsafe string SelectPipeWirePluginFolder(ILogger logger)
    {
        const string baseFolder = "gstreamer-1.0-pipewire";
        const string modernFolder = "gstreamer-1.0-pipewire-modern";

        if (!NativeLibrary.TryLoad("libpipewire-0.3.so.0", out var library))
            return baseFolder;

        try
        {
            if (!NativeLibrary.TryGetExport(library, "pw_get_library_version", out var export))
                return baseFolder;

            var text = Marshal.PtrToStringUTF8(((delegate* unmanaged<IntPtr>)export)());
            var folder = Version.TryParse(text, out var version) && version >= new Version(1, 2) ? modernFolder : baseFolder;
            LogPipeWirePluginBuild(logger, folder, text ?? "");
            return folder;
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace Client.GStreamer.Loader;

/// <summary>
/// Finds the GStreamer runtime and makes its libraries loadable for the native calls of this
/// project, which import them by their Windows names. The GstSharp.Net binding loads the libraries
/// itself and is pointed at <see cref="LibraryDirectory"/>. Must be registered before the first
/// GStreamer call.
/// The application ships its own runtime in the 'gstreamer' folder next to it, with the same layout on
/// both systems: libraries in bin (Windows) or lib (Linux), plugins in lib/gstreamer-1.0 and the plugin
/// scanner in libexec/gstreamer-1.0. Every configuration, Debug included, uses only this runtime and
/// never an installed one. Only the test bot asks for the installed runtime instead.
/// </summary>
public static partial class GstNativeLibraryResolver
{
    public const string BundledFolderName = "gstreamer";

    private const string WindowsCoreLibrary = "gstreamer-1.0-0.dll";
    private const string LinuxCoreLibrary = "libgstreamer-1.0.so.0";

    // Set by the official MSVC runtime installer
    private const string WindowsRootVariable = "GSTREAMER_1_0_ROOT_MSVC_X86_64";
    private const string WindowsDefaultRoot = @"C:\Program Files\gstreamer\1.0\msvc_x86_64";
    private const string LinuxSystemLibraryDirectory = "/usr/lib/x86_64-linux-gnu";

    private static int _registered;

    /// <summary>
    /// The directory the GStreamer libraries are loaded from, or null when no runtime was found.
    /// </summary>
    public static string? LibraryDirectory { get; private set; }

    /// <summary>
    /// The root of the runtime shipped with the application, or null when it is missing or the
    /// installed runtime is used.
    /// </summary>
    public static string? BundledRoot { get; private set; }

    public static bool IsBundled => BundledRoot != null;

    /// <summary>
    /// The GStreamer installed on the system was asked for instead of the shipped one.
    /// </summary>
    public static bool UsesInstalledRuntime { get; private set; }

    public static string ExpectedBundledRoot => Path.Combine(AppContext.BaseDirectory, BundledFolderName);

    /// <param name="useInstalledRuntime">Load the GStreamer installed on the system instead of the shipped one.</param>
    public static void Register(bool useInstalledRuntime = false)
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            return;

        UsesInstalledRuntime = useInstalledRuntime;
        if (useInstalledRuntime)
            LibraryDirectory = OperatingSystem.IsWindows() ? FindInstalledWindowsRuntime() : FindSystemLinuxRuntime();
        else
            FindBundledRuntime();

        if (LibraryDirectory == null)
            return;

        if (OperatingSystem.IsWindows())
        {
            // Dependencies of the libraries and of the plugins loaded later are found through PATH
            var path = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", $"{LibraryDirectory};{path}");
        }

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, libraryName) => Resolve(libraryName);
    }

    private static void FindBundledRuntime()
    {
        var bundledRoot = ExpectedBundledRoot;
        var bundledLibraries = Path.Combine(bundledRoot, OperatingSystem.IsWindows() ? "bin" : "lib");

        if (File.Exists(Path.Combine(bundledLibraries, CoreLibrary)))
        {
            BundledRoot = bundledRoot;
            LibraryDirectory = bundledLibraries;
        }
    }

    private static string CoreLibrary => OperatingSystem.IsWindows() ? WindowsCoreLibrary : LinuxCoreLibrary;

    private static string? FindInstalledWindowsRuntime()
    {
        string?[] roots = [Environment.GetEnvironmentVariable(WindowsRootVariable), WindowsDefaultRoot];

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var bin = Path.Combine(root, "bin");
            if (File.Exists(Path.Combine(bin, WindowsCoreLibrary)))
                return bin;
        }

        return null;
    }

    private static string? FindSystemLinuxRuntime() =>
        File.Exists(Path.Combine(LinuxSystemLibraryDirectory, LinuxCoreLibrary)) ? LinuxSystemLibraryDirectory : null;

    private static IntPtr Resolve(string libraryName)
    {
        var fileName = OperatingSystem.IsWindows() ? libraryName : ToLinuxName(libraryName);
        if (fileName == null || LibraryDirectory == null)
            return IntPtr.Zero;

        var fullPath = Path.Combine(LibraryDirectory, fileName);
        return File.Exists(fullPath) ? NativeLibrary.Load(fullPath) : IntPtr.Zero;
    }

    /// <summary>
    /// Maps a Windows name like gstwebrtc-1.0-0.dll to its shared object libgstwebrtc-1.0.so.0.
    /// </summary>
    private static string? ToLinuxName(string libraryName)
    {
        // libnice is versioned on its own
        if (libraryName == "nice-10.dll")
            return "libnice.so.10";

        var match = WindowsLibraryName().Match(libraryName);
        return match.Success ? $"lib{match.Groups["name"].Value}-{match.Groups["api"].Value}.so.0" : null;
    }

    [GeneratedRegex(@"^(?<name>[a-z0-9]+)-(?<api>\d+\.\d+)-0\.dll$")]
    private static partial Regex WindowsLibraryName();
}

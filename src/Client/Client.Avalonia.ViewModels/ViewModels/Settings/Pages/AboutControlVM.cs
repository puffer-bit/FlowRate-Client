using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Client.Application.Settings.Search;
using Client.Avalonia.Localization;
using Client.Services;
using Client.Tools.AppInfo;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

/// <summary>
/// The tab about the application itself: its version, the system it runs on, where its code is and
/// what it is built from. It has nothing to load, save or reset.
/// </summary>
public class AboutControlVM : ReactiveObject, ISettingVM
{
    public string Name => Strings.Get("settings.about.name");
    public SettingTabType Type => SettingTabType.About;
    public bool IsEnabled => true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("about",
        ("application", ["version", "platform"]),
        ("project", ["repository", "support"]));

    public string Version { get; }

    /// <summary>
    /// The system and the architecture of the process, "Linux x64".
    /// </summary>
    public string PlatformName { get; } =
        $"{CurrentPlatform.Name} {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Which release of the system it is, as the system names itself.
    /// </summary>
    public string PlatformDetails { get; }

    public string? PlatformIconKey => CurrentPlatform.IconKey;

    public Uri RepositoryUrl { get; } = new(AppInformation.RepositoryUrl);

    /// <summary>
    /// The address of the repository as it is shown, without the scheme.
    /// </summary>
    public string RepositoryName => RepositoryUrl.Host + RepositoryUrl.AbsolutePath;

    /// <summary>
    /// The main libraries this build of the application has in it, so the list differs between platforms.
    /// </summary>
    public IReadOnlyList<AboutLibrary> Libraries { get; }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    /// <param name="version">Version of the application, which each platform keeps in its own place.</param>
    /// <param name="systemDescription">
    /// Release of the operating system, where the platform names it better than the runtime does.
    /// </param>
    public AboutControlVM(string version, string? systemDescription = null)
    {
        Version = version;
        PlatformDetails = systemDescription ?? RuntimeInformation.OSDescription;
        Libraries = FindLibraries();

        LoadCommand = ReactiveCommand.CreateFromTask(() => Task.CompletedTask);
    }

    private static IReadOnlyList<AboutLibrary> FindLibraries()
    {
        var libraries = new List<AboutLibrary>
        {
            new(".NET", Environment.Version.ToString(), "MIT", Strings.Observe("settings.about.library.dotnet"))
        };

        Add("Avalonia", "Avalonia.Base", "MIT", Strings.Observe("settings.about.library.avalonia"));
        Add("ReactiveUI", "ReactiveUI", "MIT", Strings.Observe("settings.about.library.reactiveui"));
        Add("GstSharp.Net", "GstSharp.Net", "LGPL-2.1", Strings.Observe("settings.about.library.gstreamer"));
        Add("Silk.NET.OpenAL", "Silk.NET.OpenAL", "MIT", Strings.Observe("settings.about.library.openal"));
        Add("Microsoft.Data.Sqlite", "Microsoft.Data.Sqlite", "MIT", Strings.Observe("settings.about.library.sqlite"));
        Add("SQLite3 Multiple Ciphers", "SQLite3MC.PCLRaw.provider", "MIT", Strings.Observe("settings.about.library.sqlite3mc"));
        Add("Serilog", "Serilog", "Apache-2.0", Strings.Observe("settings.about.library.serilog"));
        Add("MessageFormat", "Jeffijoe.MessageFormat", "MIT", Strings.Observe("settings.about.library.messageformat"));

        return libraries;

        // A library the build does not carry is left out: Android has no GStreamer or OpenAL
        void Add(string name, string assemblyName, string license, IObservable<string> purpose)
        {
            if (VersionOf(assemblyName) is { } version)
                libraries.Add(new AboutLibrary(name, version, license, purpose));
        }
    }

    private static string? VersionOf(string assemblyName)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (Exception e) when (e is IOException or BadImageFormatException)
        {
            return null;
        }

        // The version of the package; the assembly version often stays at the major one
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString();
        if (version == null)
            return null;

        // Only the numbers: what follows them names the commit and the branch, "4.4.0-main-497f80f", "12.1.2+0a1b"
        var end = 0;
        while (end < version.Length && (char.IsAsciiDigit(version[end]) || version[end] == '.'))
            end++;

        return end > 0 ? version[..end] : version;
    }

    public Task ResetAllAsync() => Task.CompletedTask;
}

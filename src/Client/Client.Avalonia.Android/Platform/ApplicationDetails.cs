using System;
using Android.Content.PM;
using Android.OS;
using Client.Tools.AppInfo;

namespace Client.Droid.Platform;

/// <summary>
/// What Android knows about the application and about itself, for the page about the application.
/// </summary>
internal static class ApplicationDetails
{
    /// <summary>
    /// The version name of the installed package, which is ApplicationDisplayVersion of the project,
    /// with the number of the build and its commit: "0.3.2-build2610130042(c6f00e6)".
    /// </summary>
    public static string Version =>
        AppInformation.WithBuildNumber(PackageVersion, typeof(ApplicationDetails).Assembly);

    private static string PackageVersion
    {
        get
        {
            var context = global::Android.App.Application.Context;
            if (context.PackageManager is not { } packages || context.PackageName is not { } name)
                return "0.0.0";

            try
            {
                var package = OperatingSystem.IsAndroidVersionAtLeast(33)
                    ? packages.GetPackageInfo(name, PackageManager.PackageInfoFlags.Of(0L))
                    : packages.GetPackageInfo(name, 0);

                return package?.VersionName ?? "0.0.0";
            }
            catch (PackageManager.NameNotFoundException)
            {
                return "0.0.0";
            }
        }
    }

    /// <summary>
    /// The release of Android and its API level, "Android 14 (API 34)".
    /// </summary>
    public static string SystemDescription => $"Android {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt})";
}

using System;
using System.Reactive.Linq;

namespace Client.ViewModels.Settings.Pages;

/// <summary>
/// A library the application is built from, as the tab about the application lists it.
/// </summary>
/// <param name="Name">Name of the package.</param>
/// <param name="Version">Version of the package that is in this build.</param>
/// <param name="License">SPDX identifier of the license.</param>
/// <param name="Purpose">What the application uses the library for, in the language of the interface.</param>
public sealed record AboutLibrary(string Name, string Version, string License, IObservable<string> Purpose)
{
    /// <summary>
    /// The purpose and the license on one line, under the name.
    /// </summary>
    public IObservable<string> Description { get; } = Purpose.Select(purpose => $"{purpose} · {License}");
}

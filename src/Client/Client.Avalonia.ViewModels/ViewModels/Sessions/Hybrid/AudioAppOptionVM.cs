namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// An entry of the list of applications whose sound goes along with a shared screen.
/// </summary>
/// <param name="Key">The application, or null for the entry that shares no sound.</param>
/// <param name="Name">What the entry shows.</param>
public sealed record AudioAppOptionVM(string? Key, string Name);

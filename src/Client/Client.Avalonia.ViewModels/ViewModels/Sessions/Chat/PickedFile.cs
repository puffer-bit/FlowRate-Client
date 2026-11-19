using System;
using System.IO;
using System.Threading.Tasks;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// A file picked in the file dialog, as the view hands it over.
/// </summary>
/// <param name="Key">
/// What tells the file from another one, shown as a hint: its path, or the address the platform knows it by.
/// </param>
/// <param name="Open">Opens the content from its start; called every time the file is read.</param>
public record PickedFile(string Name, string? Key, long Size, Func<Task<Stream>> Open);

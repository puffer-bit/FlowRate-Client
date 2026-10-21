using System;

namespace Client.ViewModels.Settings.Pages;

/// <summary>
/// A choice of the interface language.
/// </summary>
/// <param name="Code">Language code, or null for the language of the operating system.</param>
/// <param name="Name">
/// What the choice is called. A language is named in itself, so that it can be found by someone who
/// does not read the current one; the name of the system choice follows the language.
/// </param>
/// <param name="IsMachineTranslated">Whether the texts of the language come from AI and were not checked by a speaker.</param>
public sealed record LanguageOption(string? Code, IObservable<string> Name, bool IsMachineTranslated = false);

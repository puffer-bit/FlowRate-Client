namespace Client.Application.Settings.Search;

/// <summary>
/// The texts of a setting that search looks through, in one language.
/// </summary>
/// <param name="Title">Name of the setting.</param>
/// <param name="Description">What is written under the name, if anything.</param>
/// <param name="Section">Heading of the group the setting is in.</param>
public sealed record SettingSearchText(string Title, string? Description, string Section);

/// <summary>
/// A setting that can be found by search.
/// </summary>
/// <param name="Key">Identifies the setting within the settings window, e.g. "audio.inputVolume"; the view scrolls to the control marked with it.</param>
/// <param name="Text">The texts in the language of the interface; these are the ones shown in the results.</param>
/// <param name="InvariantText">The English texts when the interface is in another language, so that English queries still match.</param>
public sealed record SettingSearchEntry(string Key, SettingSearchText Text, SettingSearchText? InvariantText = null);

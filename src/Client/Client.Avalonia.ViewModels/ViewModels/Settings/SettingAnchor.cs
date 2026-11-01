namespace Client.ViewModels.Settings;

/// <summary>
/// A section of a settings page that can be jumped to from the list of tabs.
/// </summary>
/// <param name="SectionKey">The key of the header of the section, "tab.section".</param>
/// <param name="TextKey">The key of the text of the sub-item in <c>Strings</c>.</param>
public sealed record SettingAnchor(string SectionKey, string TextKey);

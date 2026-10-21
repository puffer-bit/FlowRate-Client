using Client.Application.Settings.Search;

namespace Client.ViewModels.Settings;

/// <summary>
/// A setting found by the settings search, with the tab it is on.
/// </summary>
public class SettingSearchResultVM(ISettingVM tab, SettingSearchEntry entry)
{
    public ISettingVM Tab { get; } = tab;
    public SettingSearchEntry Entry { get; } = entry;

    public string Title => Entry.Text.Title;

    /// <summary>Where the setting is: the tab and the section, e.g. "Voice and audio › Microphone"</summary>
    public string Path => $"{Tab.Name} › {Entry.Text.Section}";
}

using System.Collections.Generic;
using Client.Avalonia.Localization;
using ReactiveUI;

namespace Client.ViewModels.Settings;

/// <summary>
/// A line of the list of tabs: a tab, or one of its <see cref="ISettingVM.Anchors"/>, which come out
/// under the tab while it is open and lead to their sections of its page.
/// </summary>
public class SettingNavItemVM : ReactiveObject
{
    public ISettingVM Setting { get; }

    /// <summary>The section a sub-item leads to; null on the line of a tab.</summary>
    public SettingAnchor? Anchor { get; }

    /// <summary>The line of the tab a sub-item is under.</summary>
    public SettingNavItemVM? Parent { get; }

    public bool IsSubItem => Parent != null;

    public bool HasSubItems { get; private set; }

    public string Name => Anchor is { } anchor ? Strings.Get(anchor.TextKey) : Setting.Name;

    /// <summary>Whether the tab is open, and so its sub-items are shown under it.</summary>
    public bool IsExpanded
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            foreach (var subItem in _subItems)
                subItem.RaisePropertyChanged(nameof(IsShown));
        }
    }

    public bool IsShown => Parent?.IsExpanded ?? true;

    private readonly List<SettingNavItemVM> _subItems = [];

    private SettingNavItemVM(ISettingVM setting, SettingAnchor? anchor, SettingNavItemVM? parent)
    {
        Setting = setting;
        Anchor = anchor;
        Parent = parent;
    }

    /// <summary>
    /// The lines of the tabs in their order, each with its sub-items under it.
    /// </summary>
    public static IReadOnlyList<SettingNavItemVM> Build(IEnumerable<ISettingVM> settings)
    {
        var items = new List<SettingNavItemVM>();
        foreach (var setting in settings)
        {
            var tab = new SettingNavItemVM(setting, null, null);
            items.Add(tab);

            foreach (var anchor in setting.Anchors)
            {
                var subItem = new SettingNavItemVM(setting, anchor, tab);
                tab._subItems.Add(subItem);
                items.Add(subItem);
            }

            tab.HasSubItems = tab._subItems.Count > 0;
        }

        return items;
    }
}

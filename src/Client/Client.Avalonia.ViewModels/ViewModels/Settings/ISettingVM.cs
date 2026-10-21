using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Application.Settings.Search;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace Client.ViewModels.Settings;

public interface ISettingVM : IReactiveObject
{
    string Name { get; }
    SettingTabType Type { get; }
    bool IsEnabled { get; }

    /// <summary>
    /// The icon of the tab spins and trembles in the list of tabs.
    /// </summary>
    bool IsIconAgitated => false;

    /// <summary>
    /// The settings of the tab that the settings search can find, in the order the tab shows them.
    /// </summary>
    IReadOnlyList<SettingSearchEntry> SearchEntries => [];

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    
    /// <summary>
    /// Restores the defaults of this tab. Saved servers, profiles and the remembered auto
    /// connection are data, not settings, and are kept.
    /// </summary>
    Task ResetAllAsync();
}
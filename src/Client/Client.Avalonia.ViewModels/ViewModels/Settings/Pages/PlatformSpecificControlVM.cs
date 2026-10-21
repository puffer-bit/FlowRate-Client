using Client.Avalonia.Localization;
using System.Threading.Tasks;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.Application.Settings;

namespace Client.ViewModels.Settings.Pages;

public class PlatformSpecificControlVM : ReactiveObject, ISettingVM
{
    public string Name => Strings.Get("settings.platform.name");
    public SettingTabType Type { get; } = SettingTabType.PlatformSpecific;
    public bool IsEnabled { get; } = false;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }
    
    private readonly IPlatformSpecificService _platformSpecificService;
    
    public PlatformSpecificControlVM(IPlatformSpecificService platformSpecificService)
    {
        _platformSpecificService = platformSpecificService;
        
        LoadCommand = ReactiveCommand
            .CreateFromTask(LoadAsync);
    }

    private async Task LoadAsync()
    {
        
    }

    // Nothing to reset until the tab has settings
    public Task ResetAllAsync() => Task.CompletedTask;
}
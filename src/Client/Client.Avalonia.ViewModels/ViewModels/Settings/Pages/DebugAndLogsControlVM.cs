using Client.Avalonia.Localization;
using System.Threading.Tasks;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.Application.Settings;

namespace Client.ViewModels.Settings.Pages;

public class DebugAndLogsControlVM : ReactiveObject, ISettingVM
{
    public string Name => Strings.Get("settings.debug.name");
    public SettingTabType Type => SettingTabType.DebugAndLogs;
    public bool IsEnabled => false;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }
    
    private readonly IDebugAndLogService _debugAndLogService;
    
    public DebugAndLogsControlVM(IDebugAndLogService debugAndLogService)
    {
        _debugAndLogService = debugAndLogService;
        
        LoadCommand = ReactiveCommand
            .CreateFromTask(LoadAsync);
    }

    private async Task LoadAsync()
    {
        
    }

    // Nothing to reset until the tab has settings
    public Task ResetAllAsync() => Task.CompletedTask;
}
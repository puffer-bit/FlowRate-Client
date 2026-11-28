using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Avalonia.Localization;
using Client.Tools.AppInfo;
using Client.ViewModels.Dialogs;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Main;

/// <summary>
/// The modes this launch runs in, which the status bar marks next to the dev build: each tells on hover what it means
/// and explains it in a message box on a click.
/// </summary>
public partial class MainWindowVM
{
    public bool IsSafeMode => LaunchOptions.Current.SafeMode;

    // Safe mode turns the media off too and says so itself
    public bool IsMediaDisabled => !IsSafeMode && !Client.Core.Media.MediaRuntime.IsEnabled;
    public bool IsSoftwareRendering => !HardwareAcceleration.IsEnabled;

    public string SafeModeText => Strings.Get("settings.mode.safe.description");
    public string MediaDisabledText => Strings.Get("media.disabled.startup");

    /// <summary>Forced by the command line for this launch, or turned off in the video settings.</summary>
    public string SoftwareRenderingText => Strings.Get(HardwareAcceleration.IsForcedOff
        ? "settings.mode.software.description"
        : "settings.mode.software.saved");

    public ReactiveCommand<RxVoid, RxVoid> ExplainSafeModeCommand { get; private set; } = null!;
    public ReactiveCommand<RxVoid, RxVoid> ExplainMediaDisabledCommand { get; private set; } = null!;
    public ReactiveCommand<RxVoid, RxVoid> ExplainSoftwareRenderingCommand { get; private set; } = null!;

    private void CreateLaunchModeCommands()
    {
        ExplainSafeModeCommand = ReactiveCommand.CreateFromTask(() =>
            ExplainLaunchModeAsync("settings.mode.safe.title", SafeModeText));
        ExplainMediaDisabledCommand = ReactiveCommand.CreateFromTask(() =>
            ExplainLaunchModeAsync("media.disabled.title", MediaDisabledText));
        ExplainSoftwareRenderingCommand = ReactiveCommand.CreateFromTask(() =>
            ExplainLaunchModeAsync("settings.mode.software.title", SoftwareRenderingText));
    }

    private async Task ExplainLaunchModeAsync(string titleKey, string text) =>
        await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok, text, Strings.Get(titleKey)));
}

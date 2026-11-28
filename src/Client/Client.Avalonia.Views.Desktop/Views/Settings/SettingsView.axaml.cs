using System;
using System.Linq;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.Avalonia.Controls.Settings;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.Views.Dialogs;
using Client.Views.Common;
using Client.ViewModels.Settings;

namespace Client.Views.Settings;

internal partial class SettingsView : ViewControl<SettingsWindowVM>
{
    public SettingsView()
    {
        InitializeComponent();
        
        if (!Design.IsDesignMode)
        {
            this.WhenActivated(disposables =>
            {
                ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
                
                ViewModel!.ShowSetting.RegisterHandler(interaction =>
                {
                    var key = interaction.Input;

                    // The view of the tab that has just been selected is created and laid out later in this
                    // turn of the dispatcher; the setting can only be scrolled to after that
                    Dispatcher.UIThread.Post(
                        () => SettingContent.GetVisualDescendants().OfType<SettingsPage>().FirstOrDefault()?.ScrollTo(key),
                        DispatcherPriority.Loaded);

                    interaction.SetOutput(RxVoid.Default);
                }).DisposeWith(disposables);

                ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);
            });
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        // The dialog is gone, however it was closed: stop the microphone test and device watching
        ViewModel?.Dispose();
    }
}
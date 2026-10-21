using System;
using System.Reactive.Disposables;
using Client.Avalonia.Styles.Animation;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.Views.Common;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Views.Sessions.Hybrid;

internal partial class HybridSessionControl : ViewControl<ActiveHybridSessionVM>
{
    public HybridSessionControl()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            // The glows wander without an end, so they are not stopped by a zero duration like the rest
            Motion.Changed += UpdateBackdropMotion;
            Disposable.Create(() => Motion.Changed -= UpdateBackdropMotion).DisposeWith(disposables);
            UpdateBackdropMotion();
        });
    }

    private void UpdateBackdropMotion() => Backdrop.Classes.Set("moving", Motion.IsEnabled);
}

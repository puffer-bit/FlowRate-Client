using System;
using System.Reactive.Disposables;
using Avalonia.Media;
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
            // Each glow wanders along its own closed path and comes back to where it started, so the
            // loop shows no jump. Different paths and durations keep the picture from repeating
            new BackdropGlows(Backdrop,
                new BackdropGlows.Glow((TranslateTransform)FarGlow.RenderTransform!, TimeSpan.FromSeconds(46),
                    (0, 0, 0), (0.35, 220, 90), (0.7, 80, -70), (1, 0, 0)),
                new BackdropGlows.Glow((TranslateTransform)MiddleGlow.RenderTransform!, TimeSpan.FromSeconds(37),
                    (0, 0, 0), (0.3, -180, -110), (0.65, -260, 60), (1, 0, 0)),
                new BackdropGlows.Glow((TranslateTransform)NearGlow.RenderTransform!, TimeSpan.FromSeconds(29),
                    (0, 0, 0), (0.4, 140, -130), (0.75, -110, -60), (1, 0, 0))
            ).DisposeWith(disposables);
        });
    }
}

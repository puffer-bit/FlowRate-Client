using System;
using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.Media;
using Client.Services;
using ReactiveUI.Primitives;
using Client.Views.Common;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Views.Sessions.Hybrid;

internal partial class HybridSessionControl : ViewControl<ActiveHybridSessionVM>
{
    private const double PeersMinWidth = 200, PeersMaxWidth = 480;

    /// <summary>The width the splitter gave the participants panel, kept while it is hidden.</summary>
    private static double _peersWidth = 260;

    public HybridSessionControl()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            // The column holds the panel's width in pixels, so the panel keeps it as the window is
            // resized and widens with the splitter. Hidden, the column takes no room
            this.WhenViewModelChanged((vm, subscriptions) =>
                System.ObservableExtensions.Subscribe(
                        vm.WatchProperty(nameof(vm.IsUserListVisible), model => model.IsUserListVisible), ShowPeers)
                    .DisposeWith(subscriptions))
                .DisposeWith(disposables);

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

    private void ShowPeers(bool isVisible)
    {
        var peersColumn = MainArea.ColumnDefinitions[2];
        if (peersColumn.Width.IsAbsolute && peersColumn.Width.Value >= PeersMinWidth)
            _peersWidth = peersColumn.Width.Value;
        peersColumn.MinWidth = isVisible ? PeersMinWidth : 0;
        peersColumn.MaxWidth = isVisible ? PeersMaxWidth : double.PositiveInfinity;
        peersColumn.Width = new GridLength(isVisible ? _peersWidth : 0);
    }
}

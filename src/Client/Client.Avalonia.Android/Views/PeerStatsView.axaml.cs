using System;
using Avalonia;
using Avalonia.Controls;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Droid.Views;

/// <summary>
/// How the streams of one user reach us, with graphs of their loss and jitter.
/// </summary>
public partial class PeerStatsView : UserControl
{
    private bool _isAttached;
    private PeerStatsVM? _shownStats;

    public PeerStatsView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        ShowStats(DataContext as PeerStatsVM);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        ShowStats(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ShowStats(_isAttached ? DataContext as PeerStatsVM : null);
    }

    private void ShowStats(PeerStatsVM? stats)
    {
        if (ReferenceEquals(_shownStats, stats))
            return;

        _shownStats?.HideHistory();
        _shownStats = stats;
        _shownStats?.ShowHistory();
    }
}

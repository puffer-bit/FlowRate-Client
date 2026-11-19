using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// Keeps a flyout alive for its leave animation. The presenter uses the same Presence
/// animations as other controls; commands still run before the flyout is closed.
/// </summary>
public static class FlyoutMotion
{
    private static readonly ConditionalWeakTable<Flyout, Lifetime> Lifetimes = new();
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        FlyoutBase.IsOpenProperty.Changed.AddClassHandler<Flyout>((flyout, change) =>
        {
            if (change.NewValue is true)
                Lifetimes.GetValue(flyout, static owner => new Lifetime(owner));
        });
    }

    private sealed class Lifetime
    {
        private readonly Flyout _flyout;
        private IDisposable? _closeTimer;
        private Control? _leaving;
        private bool _completingClose;

        public Lifetime(Flyout flyout)
        {
            _flyout = flyout;
            flyout.Closing += OnClosing;
            flyout.Popup.Closed += OnClosed;
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            // A second close request must not restart the fade or bypass it.
            if (_completingClose || e.Cancel)
                return;

            if (_leaving != null)
            {
                e.Cancel = true;
                return;
            }

            if (_flyout.Popup.Child is not FlyoutPresenter presenter ||
                TopLevel.GetTopLevel(presenter) == null ||
                Presence.GetLeave(presenter) == LeaveEffect.None ||
                Presence.GetLeaveTime(presenter) <= TimeSpan.Zero)
                return;

            e.Cancel = true;
            _leaving = presenter;
            Presence.SetIsLeaving(presenter, true);
            _closeTimer = DispatcherTimer.RunOnce(CompleteClose, Presence.GetLeaveTime(presenter));
        }

        private void CompleteClose()
        {
            _closeTimer = null;
            _completingClose = true;
            try
            {
                _flyout.Hide();
            }
            finally
            {
                _completingClose = false;
                Reset();
            }
        }

        private void OnClosed(object? sender, EventArgs e) => Reset();

        private void Reset()
        {
            _closeTimer?.Dispose();
            _closeTimer = null;
            if (_leaving is { } presenter)
            {
                _leaving = null;
                Presence.SetIsLeaving(presenter, false);
            }
        }
    }
}

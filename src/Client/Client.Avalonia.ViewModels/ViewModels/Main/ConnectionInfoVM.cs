using System;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using ReactiveUI;

namespace Client.ViewModels.Main;

/// <summary>
/// The connection to the server for the flyout of the status bar: where it goes, since when and
/// how the server was trusted.
/// </summary>
public sealed class ConnectionInfoVM : ReactiveObject
{
    private readonly DispatcherTimer _clock;

    private DateTime? _connectedAt;

    public ConnectionInfoVM()
    {
        // Only the duration and the time move; ticks while connected
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RaiseTimes());
    }

    /// <summary>
    /// The measured round trip, or null when there is none yet.
    /// </summary>
    public TimeSpan? RoundTrip
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(RoundTripText));
        }
    }

    public string RoundTripText => RoundTrip is { } value ? $"{Math.Ceiling(value.TotalMilliseconds):0} ms" : "—";

    public string Address
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "—";

    /// <summary>
    /// The system verified the certificate; otherwise the user trusted it by hand.
    /// </summary>
    public bool IsCertificateVerified
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The time of the day the connection was made, with the date when it was not today.
    /// </summary>
    public string ConnectedAtText => _connectedAt is { } connectedAt
        ? connectedAt.Date == DateTime.Today ? connectedAt.ToString("HH:mm") : Strings.FormatDateTime(connectedAt)
        : "—";

    public string DurationText
    {
        get
        {
            if (_connectedAt is not { } connectedAt)
                return "—";

            var duration = DateTime.Now - connectedAt;
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}:{duration:mm\\:ss}"
                : duration.ToString("m\\:ss");
        }
    }

    /// <summary>
    /// Starts counting a connection that has just been made, a restored one too.
    /// </summary>
    public void Attach(string? address, bool isCertificateVerified)
    {
        Address = address ?? "—";
        IsCertificateVerified = isCertificateVerified;
        _connectedAt = DateTime.Now;
        RaiseTimes();
        _clock.Start();
    }

    public void Detach()
    {
        _clock.Stop();
        _connectedAt = null;
        RaiseTimes();
    }

    private void RaiseTimes()
    {
        this.RaisePropertyChanged(nameof(ConnectedAtText));
        this.RaisePropertyChanged(nameof(DurationText));
    }
}

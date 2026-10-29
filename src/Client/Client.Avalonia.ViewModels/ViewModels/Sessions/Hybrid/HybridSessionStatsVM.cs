using System;
using System.Collections.Generic;
using System.Linq;
using Client.Avalonia.Localization;
using Client.Core.Sessions.Hybrid;
using ReactiveUI;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// The quality of the connection of a hybrid session and its recent history, for the graphs of the
/// statistics flyout.
/// </summary>
public class HybridSessionStatsVM : ReactiveObject
{
    /// <summary>
    /// How many readings the graphs show: two minutes of them.
    /// </summary>
    public const int HistoryLength = 60;

    private readonly Queue<TransportStatsSample> _history = new();

    public ConnectionQuality Quality
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(QualityText));
            this.RaisePropertyChanged(nameof(SignalLevel));
        }
    }

    /// <summary>
    /// The quality as the lit arcs of the signal indicator.
    /// </summary>
    public int SignalLevel => Quality switch
    {
        ConnectionQuality.Good => 3,
        ConnectionQuality.Fair => 2,
        ConnectionQuality.Poor => 1,
        _ => 0
    };

    public string QualityText => Quality switch
    {
        ConnectionQuality.Good => Strings.Get("hybrid.stats.quality.good"),
        ConnectionQuality.Fair => Strings.Get("hybrid.stats.quality.fair"),
        ConnectionQuality.Poor => Strings.Get("hybrid.stats.quality.poor"),
        _ => Strings.Get("hybrid.stats.quality.unknown")
    };

    public string RoundTripText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "—";

    /// <summary>
    /// Whether the media goes over a TCP candidate pair; null while that is not known: before
    /// the first reading, while ICE has not picked a pair, and without a connection.
    /// </summary>
    public bool? IsOverTcp
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(TransportText));
            this.RaisePropertyChanged(nameof(TransportTip));
        }
    }

    public string TransportText => IsOverTcp switch
    {
        true => "TCP",
        false => "UDP",
        null => "?"
    };

    public string TransportTip => IsOverTcp switch
    {
        true => Strings.Get("hybrid.stats.tcp"),
        false => Strings.Get("hybrid.stats.udp"),
        null => Strings.Get("hybrid.stats.transportUnknown")
    };

    public string InboundLossText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "—";

    public string OutboundLossText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "—";

    /// <summary>
    /// The round trips of the readings, oldest first; null where the SFU reported none.
    /// </summary>
    public IReadOnlyList<double?> RoundTripHistory
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    public IReadOnlyList<double?> InboundLossHistory
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    public IReadOnlyList<double?> OutboundLossHistory
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    /// <summary>
    /// Takes a new reading. Called on the UI thread.
    /// </summary>
    public void Add(TransportStatsSample sample)
    {
        _history.Enqueue(sample);

        while (_history.Count > HistoryLength)
            _history.Dequeue();

        Quality = sample.Quality;
        // The last known round trip stays shown through readings without one
        RoundTripText = _history.LastOrDefault(reading => reading.RoundTripMs != null).RoundTripMs is { } roundTrip
            ? $"{Math.Ceiling(roundTrip):0} ms"
            : "—";
        IsOverTcp = sample.IsOverTcp;
        InboundLossText = $"{sample.InboundLossPercent:0.#} %";
        OutboundLossText = $"{sample.OutboundLossPercent:0.#} %";

        // New lists, so that the graphs see a change
        RoundTripHistory = _history.Select(reading => reading.RoundTripMs).ToArray();
        InboundLossHistory = _history.Select(reading => (double?)reading.InboundLossPercent).ToArray();
        OutboundLossHistory = _history.Select(reading => (double?)reading.OutboundLossPercent).ToArray();
    }

    /// <summary>
    /// Forgets the quality and the protocol while there is no connection to measure; the history stays.
    /// </summary>
    public void ResetQuality()
    {
        Quality = ConnectionQuality.Unknown;
        IsOverTcp = null;
    }
}

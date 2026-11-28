using System;
using System.Collections.Generic;
using System.Linq;
using Client.Avalonia.Localization;
using Client.Core.Sessions.Hybrid;
using ReactiveUI;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// How the streams of one user of the hybrid session we are in reach us, and its recent history: the
/// loss on their way to the SFU and the jitter of their packets.
/// </summary>
public class PeerStatsVM : ReactiveObject
{
    // The thresholds of StreamHealthAnalyzer, so a badge on the screen and this rating agree
    private const double GoodLossPercent = 2;
    private const double FairLossPercent = 8;
    private const double GoodJitterMs = 30;
    private const double FairJitterMs = 80;

    private readonly Queue<(double? LossPercent, double? JitterMs)> _history = new();
    private int _historyViewers;
    private bool _historyChanged;

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
        _ => Strings.Get("hybrid.stats.peer.silent")
    };

    public string LossText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "—";

    public string JitterText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "—";

    /// <summary>
    /// The loss of the readings, oldest first; null where the user sent nothing.
    /// </summary>
    public IReadOnlyList<double?> LossHistory
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    public IReadOnlyList<double?> JitterHistory
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    /// <summary>
    /// Takes a new reading of the session. Called on the UI thread.
    /// </summary>
    public void Add(double? loss, double? jitter)
    {
        _history.Enqueue((loss, jitter));

        while (_history.Count > HybridSessionStatsVM.HistoryLength)
            _history.Dequeue();

        Quality = Rate(loss, jitter);
        LossText = loss is { } percent ? $"{percent:0.#} %" : "—";
        JitterText = jitter is { } ms ? $"{Math.Ceiling(ms):0} ms" : "—";

        _historyChanged = true;
        if (_historyViewers > 0)
            UpdateHistory();
    }

    /// <summary>Starts updating graph snapshots while a view shows them. Called on the UI thread.</summary>
    public void ShowHistory()
    {
        if (++_historyViewers == 1 && _historyChanged)
            UpdateHistory();
    }

    /// <summary>Stops updating graph snapshots once the last view closes.</summary>
    public void HideHistory()
    {
        if (_historyViewers > 0)
            _historyViewers--;
    }

    private void UpdateHistory()
    {
        _historyChanged = false;
        LossHistory = _history.Select(reading => reading.LossPercent).ToArray();
        JitterHistory = _history.Select(reading => reading.JitterMs).ToArray();
    }

    private static ConnectionQuality Rate(double? loss, double? jitter)
    {
        if (loss is not { } percent)
            return ConnectionQuality.Unknown;

        var ms = jitter ?? 0;

        if (percent < GoodLossPercent && ms < GoodJitterMs)
            return ConnectionQuality.Good;

        if (percent < FairLossPercent && ms < FairJitterMs)
            return ConnectionQuality.Fair;

        return ConnectionQuality.Poor;
    }
}

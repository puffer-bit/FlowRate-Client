using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using Client.Core.Media;
using Client.Core.Settings;

namespace Client.Avalonia.Controls.Media;

/// <summary>
/// The statistics of one screen drawn over its picture: a line of the main values, which a click
/// expands into the network, the transport, the encoder or decoder, the drawing and the sound, with
/// graphs of the last half minute. Only what this device measured is shown.
/// </summary>
/// <remarks>
/// Whether the statistics are expanded is one choice for every screen, which starts as the setting
/// says. Drawn by the control itself: a few dozen text blocks laid out anew every reading would cost
/// more than the picture under them.
/// </remarks>
public sealed class StreamPerfOverlay : Control
{
    private const double FontSize = 11;
    private const double Padding = 6;
    private const double ColumnGap = 10;
    private const double SectionGap = 4;
    private const double GraphHeight = 24;
    private const double GraphWidth = 220;
    private static readonly TimeSpan GraphSpan = TimeSpan.FromSeconds(30);

    // Thresholds of the colours, close to the ones of the health badges of a screen
    private const double RoundTripWarning = 150;
    private const double RoundTripBad = 300;
    private const double LossWarning = 2;
    private const double LossBad = 8;
    private const double BufferWarning = 100;
    private const double BufferBad = 250;
    private const double PacerWarning = 30;
    private const double PacerBad = 100;
    private const double ConcealedWarning = 1;
    private const double ConcealedBad = 5;
    private const int QueueWarning = 5;
    private const int QueueBad = 10;
    private const double LateWarning = 60;

    public static readonly StyledProperty<bool> IsExpandedTileProperty =
        AvaloniaProperty.Register<StreamPerfOverlay, bool>(nameof(IsExpandedTile));

    private static bool _isExpanded = DebugSettings.Current.IsMediaPerfExpanded;
    private static bool _isExpandedSetting = DebugSettings.Current.IsMediaPerfExpanded;
    private static event Action? ExpandedChanged;

    private readonly DispatcherTimer _timer;
    // The lines being built, and the ones drawn: formatted and measured, swapped in whole, so a render
    // that comes before the next measure still finds every line ready
    private readonly List<Line> _lines = [];
    private IReadOnlyList<Line> _shown = [];
    private readonly Queue<GraphPoint> _history = new();
    private readonly Typeface _typeface = new(FontFamily.Default);
    private readonly Typeface _boldTypeface = new(FontFamily.Default, weight: FontWeight.SemiBold);

    private volatile Reading? _reading;
    private Reading? _historyReading;
    private Size _size;

    static StreamPerfOverlay()
    {
        AffectsMeasure<StreamPerfOverlay>(IsExpandedTileProperty);
        IsExpandedTileProperty.Changed.AddClassHandler<StreamPerfOverlay>((overlay, _) => overlay.Refresh());

        DebugSettings.Changed += () =>
        {
            // The setting decides again once it changes, whatever was clicked since
            if (DebugSettings.Current.IsMediaPerfExpanded == _isExpandedSetting)
                return;

            _isExpandedSetting = DebugSettings.Current.IsMediaPerfExpanded;
            _isExpanded = _isExpandedSetting;
            Dispatcher.UIThread.Post(() => ExpandedChanged?.Invoke());
        };
    }

    public StreamPerfOverlay()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = DebugSettings.Current.MediaPerfInterval };
        _timer.Tick += (_, _) => Refresh();
        Cursor = new Cursor(StandardCursorType.Hand);
        IsVisible = false;
    }

    /// <summary>The screen fills the view, for <see cref="MediaPerfOverlayMode.ExpandedOnly"/>.</summary>
    public bool IsExpandedTile
    {
        get => GetValue(IsExpandedTileProperty);
        set => SetValue(IsExpandedTileProperty, value);
    }

    /// <summary>Reads the counters of the frames as they are drawn now, or null while nothing draws them.</summary>
    public Func<FrameStats?>? ReadFrames { get; set; }

    /// <summary>Tells how the frames reach the screen, for the drawing part; null where it is not known.</summary>
    public Func<string?>? ReadRoute { get; set; }

    /// <summary>
    /// Takes the latest window of the statistics of the screen; null while there is none, as before
    /// the first one. Any thread.
    /// </summary>
    public void SetPerf(StreamPerf? stream, NetworkPerf? network) => _reading = new Reading(stream, network);

    /// <summary>Forgets the statistics and the graphs, for another screen.</summary>
    public void Reset()
    {
        _reading = null;
        _historyReading = null;
        _history.Clear();
        Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ExpandedChanged += Refresh;
        DebugSettings.Changed += OnSettingsChanged;
        _timer.Start();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ExpandedChanged -= Refresh;
        DebugSettings.Changed -= OnSettingsChanged;
        _timer.Stop();
    }

    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        _timer.Interval = DebugSettings.Current.MediaPerfInterval;
        Refresh();
    });

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Pointer.Type == PointerType.Mouse)
            return;

        _isExpanded = !_isExpanded;
        ExpandedChanged?.Invoke();
        e.Handled = true;
    }

    // A double click here is two clicks on the statistics, not one that expands the screen
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
    }

    private void Refresh()
    {
        var settings = DebugSettings.Current;
        var isShown = settings.MediaPerfOverlay == MediaPerfOverlayMode.AllStreams ||
                      (settings.MediaPerfOverlay == MediaPerfOverlayMode.ExpandedOnly && IsExpandedTile);

        IsVisible = isShown;

        if (!isShown)
            return;

        var reading = _reading;
        var frames = ReadFrames?.Invoke();

        // A point a reading, and a reading only once
        if (reading != null && !ReferenceEquals(reading, _historyReading))
        {
            _historyReading = reading;
            AddHistory(reading, frames);
        }

        Build(reading, frames, settings);
        Format();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void AddHistory(Reading reading, FrameStats? frames)
    {
        var now = DateTime.UtcNow;
        _history.Enqueue(new GraphPoint(now, Delay(reading.Stream), CodecMs(reading.Stream), Loss(reading),
            Bitrate(reading.Stream), frames?.ShownPerSecond));

        while (_history.Count > 0 && now - _history.Peek().Time > GraphSpan)
            _history.Dequeue();
    }

    // ---- What is shown ----

    private void Build(Reading? reading, FrameStats? frames, DebugSettings settings)
    {
        _lines.Clear();

        var stream = reading?.Stream;
        var network = reading?.Network;

        _lines.Add(Line.Summary((_isExpanded ? "▾ " : "▸ ") + Summary(stream, network, frames), SummaryLevel(reading, frames)));

        if (!_isExpanded)
            return;

        if (reading == null)
        {
            _lines.Add(Line.Text(Strings.Get("hybrid.screen.perf.waiting"), Level.Secondary));
        }

        var sections = settings.MediaPerfSections;

        if (sections.HasFlag(MediaPerfSections.Network) && network != null)
            AddNetwork(network, stream);

        if (sections.HasFlag(MediaPerfSections.Transport))
        {
            if (stream?.Receive is { } receive)
                AddReceive(receive);

            if (stream?.Send is { } send)
                AddSend(send);
        }

        if (sections.HasFlag(MediaPerfSections.Codec))
        {
            if (stream?.Encoder is { } encoder)
                AddEncoder(encoder);

            if (stream?.Decoder is { } decoder)
                AddDecoder(decoder);
            else if (stream?.Receive is { DecoderImplementation: { } implementation } peerDecoded)
                AddPeerDecoder(implementation, peerDecoded);
        }

        if (sections.HasFlag(MediaPerfSections.Render) && frames is { } shown)
            AddRender(shown);

        if (sections.HasFlag(MediaPerfSections.Audio) && stream?.Audio is { } audio)
            AddAudio(audio);

        if (settings.ShowMediaPerfGraphs && _history.Count > 1)
            AddGraphs();
    }

    private static string Summary(StreamPerf? stream, NetworkPerf? network, FrameStats? frames)
    {
        var parts = new List<string>();

        if (frames is { } shown)
            parts.Add(Invariant($"{shown.ShownPerSecond:F0}/{shown.ReceivedPerSecond:F0} fps"));
        else if (stream?.Encoder is { } encoder)
            parts.Add(Invariant($"{encoder.OutputPerSecond:F0} fps"));

        if (Delay(stream) is { } delay)
            parts.Add(Invariant($"≈{delay:F0} ms"));

        if (LossOf(stream) is { } loss)
            parts.Add(Invariant($"{loss:F1}%"));

        if (Bitrate(stream) is { } kbps)
            parts.Add(FormatKbps(kbps));

        if (parts.Count == 0 && network?.RoundTripMs is { } roundTrip)
            parts.Add(Invariant($"RTT {roundTrip:F0} ms"));

        return parts.Count == 0 ? Strings.Get("hybrid.screen.perf.waiting") : string.Join(" · ", parts);
    }

    /// <summary>
    /// What a picture spends on this device: from its first packet to it being decoded for a watched
    /// screen, from its capture to it leaving for our own.
    /// </summary>
    private static double? Delay(StreamPerf? stream)
    {
        if (stream?.Receive is { } receive)
            return (receive.WaitMs is { IsEmpty: false } wait ? wait.AverageMs : receive.ProcessingMs) +
                   (stream.Decoder is { DecodeMs.IsEmpty: false } decoder ? decoder.DecodeMs.P50Ms : receive.DecodeMs ?? 0);

        if (stream?.Encoder is { EncodeMs.IsEmpty: false } encoder)
            return encoder.EncodeMs.P50Ms + (stream.Send?.QueueMs?.AverageMs ?? 0) + (stream.Send?.PacerMs ?? 0);

        return stream?.Decoder is { DecodeMs.IsEmpty: false } only ? only.DecodeMs.P50Ms : null;
    }

    private static double? CodecMs(StreamPerf? stream) =>
        stream?.Encoder is { EncodeMs.IsEmpty: false } encoder ? encoder.EncodeMs.P95Ms
        : stream?.Decoder is { DecodeMs.IsEmpty: false } decoder ? decoder.DecodeMs.P95Ms
        : stream?.Receive?.DecodeMs;

    private static double? LossOf(StreamPerf? stream) => stream?.Receive?.LossPercent ?? stream?.Send?.LossPercent;

    private static double? Loss(Reading reading) => LossOf(reading.Stream);

    private static double? Bitrate(StreamPerf? stream) =>
        stream?.Receive?.BitrateKbps ?? stream?.Send?.BitrateKbps ?? stream?.Encoder?.BitrateKbps;

    private static Level SummaryLevel(Reading? reading, FrameStats? frames)
    {
        var stream = reading?.Stream;
        var level = Level.Normal;

        level = Worse(level, LevelOf(LossOf(stream), LossWarning, LossBad));
        level = Worse(level, LevelOf(reading?.Network?.RoundTripMs, RoundTripWarning, RoundTripBad));

        if (stream?.Receive is { Freezes: > 0 })
            level = Level.Bad;

        if (stream?.Decoder is { } decoder)
            level = Worse(level, LevelOf(decoder.MaxQueued, QueueWarning, QueueBad));

        if (stream?.Encoder is { InputPerSecond: > 0 } encoder && !encoder.EncodeMs.IsEmpty)
            level = Worse(level, LevelOf(encoder.EncodeMs.P95Ms, 700 / encoder.InputPerSecond, 1000 / encoder.InputPerSecond));

        if (frames is { LateMs: > LateWarning })
            level = Worse(level, Level.Warning);

        return level;
    }

    private void AddNetwork(NetworkPerf network, StreamPerf? stream)
    {
        Header("hybrid.screen.perf.network");
        Pair("rtt", Ms(network.RoundTripMs), LevelOf(network.RoundTripMs, RoundTripWarning, RoundTripBad));

        if (LossOf(stream) is { } loss)
            Pair("loss", Invariant($"{loss:F1}%"), LevelOf(loss, LossWarning, LossBad));

        if (network.AvailableOutgoingKbps is { } estimate)
            Pair("estimate", FormatKbps(estimate));

        if (network is { SentKbps: { } sent, ReceivedKbps: { } received })
            Pair("traffic", $"↑ {FormatKbps(sent)}  ↓ {FormatKbps(received)}");

        var protocol = network.IsOverTcp switch
        {
            true => "TCP",
            false => "UDP",
            null => null
        };

        if (protocol != null || network.LocalCandidate != null)
            Pair("route", string.Join(" ", new[]
            {
                protocol,
                network.LocalCandidate != null ? $"{network.LocalCandidate} → {network.RemoteCandidate ?? "?"}" : null
            }.OfType<string>()), network.IsOverTcp == true ? Level.Warning : Level.Normal);
    }

    private void AddReceive(VideoReceivePerf receive)
    {
        Header("hybrid.screen.perf.transport");
        Pair("bitrate", receive.Width > 0
            ? $"{FormatKbps(receive.BitrateKbps)} · {receive.Width}×{receive.Height}"
            : FormatKbps(receive.BitrateKbps));
        Pair("loss", Invariant($"{receive.LossPercent:F1}% · {receive.Packets} pkt"),
            LevelOf(receive.LossPercent, LossWarning, LossBad));
        Pair("jitter", Ms(receive.JitterMs));
        Pair("jitterBuffer", receive.JitterBufferTargetMs is { } target
                ? Invariant($"{receive.JitterBufferMs:F0} / {target:F0} ms")
                : Ms(receive.JitterBufferMs),
            LevelOf(receive.JitterBufferMs, BufferWarning, BufferBad));
        Pair("assembly", Invariant($"{receive.AssemblyMs:F1} ms"));
        Pair("processing", Ms(receive.ProcessingMs));

        if (receive.WaitMs is { IsEmpty: false } wait)
            Pair("inLibWebRTC", Timing(wait), LevelOf(wait.AverageMs, BufferWarning, BufferBad));

        if (receive.HandoffMs is { IsEmpty: false } handoff)
            Pair("handoff", Timing(handoff), LevelOf(handoff.MaxMs, 5, 20));

        Pair("frames", Invariant($"{receive.ReceivedPerSecond:F0} → {receive.DecodedPerSecond:F0} fps") +
                       (receive.Dropped > 0 ? Invariant($" · −{receive.Dropped}") : ""),
            receive.Dropped > 0 ? Level.Warning : Level.Normal);
        Pair("freezes", Invariant($"{receive.Freezes} · {receive.FreezeMs:F0} ms"),
            receive.Freezes > 0 ? Level.Bad : Level.Normal);
        Pair("requests", Invariant($"{receive.Nacks} / {receive.Plis} / {receive.Firs}"),
            receive.Plis + receive.Firs > 0 ? Level.Warning : Level.Normal);
        Pair("keyFrames", receive.KeyFrames.ToString(CultureInfo.InvariantCulture));
    }

    private void AddSend(VideoSendPerf send)
    {
        Header("hybrid.screen.perf.transport");
        Pair("bitrate", send.TargetKbps is { } target
            ? $"{FormatKbps(send.BitrateKbps)} / {FormatKbps(target)}"
            : FormatKbps(send.BitrateKbps));
        Pair("frames", Invariant($"{send.SentPerSecond:F0} fps"));

        if (send.LossPercent is { } loss)
            Pair("loss", Invariant($"{loss:F1}%"), LevelOf(loss, LossWarning, LossBad));

        if (send.RoundTripMs is { } roundTrip)
            Pair("rtt", Ms(roundTrip), LevelOf(roundTrip, RoundTripWarning, RoundTripBad));

        Pair("pacer", Invariant($"{send.PacerMs:F1} ms"), LevelOf(send.PacerMs, PacerWarning, PacerBad));

        if (send.QueueMs is { IsEmpty: false } queue)
            Pair("queue", Timing(queue), LevelOf(queue.AverageMs, 10, 50));

        if (send.Skipped is { } skipped)
            Pair("skipped", skipped.ToString(CultureInfo.InvariantCulture), skipped > 0 ? Level.Warning : Level.Normal);

        Pair("resent", send.Retransmitted.ToString(CultureInfo.InvariantCulture));
        Pair("requests", Invariant($"{send.Nacks} / {send.Plis} / {send.Firs}"),
            send.Plis + send.Firs > 0 ? Level.Warning : Level.Normal);

        if (send.HugeFrames > 0)
            Pair("huge", send.HugeFrames.ToString(CultureInfo.InvariantCulture), Level.Warning);

        if (send.QualityLimitation is { } limitation)
            Pair("limited", limitation, limitation == "none" ? Level.Normal : Level.Warning);
    }

    private void AddEncoder(EncoderPerf encoder)
    {
        Header("hybrid.screen.perf.encoder");
        _lines.Add(Line.Text($"{encoder.Element} · {encoder.Scaler}", Level.Secondary));
        Pair("capture", Invariant($"{encoder.CapturedPerSecond:F0} fps"));
        Pair("inOut", Invariant($"{encoder.InputPerSecond:F0} → {encoder.OutputPerSecond:F0} fps"),
            encoder.InputPerSecond > 0 && encoder.OutputPerSecond < encoder.InputPerSecond * 0.8 ? Level.Bad : Level.Normal);
        Pair("encode", Latency(encoder.EncodeMs), encoder.InputPerSecond > 0 && !encoder.EncodeMs.IsEmpty
            ? LevelOf(encoder.EncodeMs.P95Ms, 700 / encoder.InputPerSecond, 1000 / encoder.InputPerSecond)
            : Level.Normal);
        Pair("bitrate", $"{FormatKbps(encoder.BitrateKbps)} / {FormatKbps(encoder.TargetKbps)}");
        Pair("keyFrames", encoder.KeyFrames > 0
            ? Invariant($"{encoder.KeyFrames} · {encoder.MaxKeyFrameBytes / 1024.0:F0} KB")
            : "0");
        Pair("frameBytes", Invariant($"{encoder.AverageFrameBytes / 1024.0:F1} KB"));
    }

    private void AddDecoder(DecoderPerf decoder)
    {
        Header("hybrid.screen.perf.decoder");
        _lines.Add(Line.Text($"{decoder.Element} · {(decoder.IsHardware ? "GPU" : "CPU")}", Level.Secondary));
        Pair("inOut", Invariant($"{decoder.InputPerSecond:F0} → {decoder.OutputPerSecond:F0} fps"),
            decoder.InputPerSecond > 0 && decoder.OutputPerSecond < decoder.InputPerSecond * 0.8 ? Level.Bad : Level.Normal);
        Pair("decode", Latency(decoder.DecodeMs), decoder.InputPerSecond > 0 && !decoder.DecodeMs.IsEmpty
            ? LevelOf(decoder.DecodeMs.P95Ms, 700 / decoder.InputPerSecond, 1000 / decoder.InputPerSecond)
            : Level.Normal);
        Pair("queue", Invariant($"{decoder.Queued} · max {decoder.MaxQueued}"),
            LevelOf(decoder.MaxQueued, QueueWarning, QueueBad));
        Pair("dropped", decoder.Dropped.ToString(CultureInfo.InvariantCulture),
            decoder.Dropped > 0 ? Level.Bad : Level.Normal);
    }

    // libwebrtc decodes itself, as on Android
    private void AddPeerDecoder(string implementation, VideoReceivePerf receive)
    {
        Header("hybrid.screen.perf.decoder");
        Pair("decodedBy", implementation);

        if (receive.DecodeMs is { } decode)
            Pair("decode", Invariant($"{decode:F1} ms"),
                receive.DecodedPerSecond > 0 ? LevelOf(decode, 700 / receive.DecodedPerSecond, 1000 / receive.DecodedPerSecond) : Level.Normal);
    }

    private void AddRender(FrameStats frames)
    {
        Header("hybrid.screen.perf.render");
        Pair("shown", Invariant($"{frames.ShownPerSecond:F0} / {frames.ReceivedPerSecond:F0} fps"));
        Pair("dropped", frames.Dropped.ToString(CultureInfo.InvariantCulture));
        Pair("late", Ms(frames.LateMs), frames.LateMs > LateWarning ? Level.Warning : Level.Normal);

        if (ReadRoute?.Invoke() is { } route)
            _lines.Add(Line.Text(route, Level.Secondary));
    }

    private void AddAudio(AudioReceivePerf audio)
    {
        Header("hybrid.screen.perf.audio");
        Pair("bitrate", FormatKbps(audio.BitrateKbps));
        Pair("loss", Invariant($"{audio.LossPercent:F1}%"), LevelOf(audio.LossPercent, LossWarning, LossBad));
        Pair("jitterBuffer", audio.JitterBufferTargetMs is { } target
                ? Invariant($"{audio.JitterBufferMs:F0} / {target:F0} ms")
                : Ms(audio.JitterBufferMs),
            LevelOf(audio.JitterBufferMs, BufferWarning, BufferBad));
        Pair("concealed", Invariant($"{audio.ConcealedPercent:F1}%"),
            LevelOf(audio.ConcealedPercent, ConcealedWarning, ConcealedBad));
        Pair("stretch", Invariant($"{audio.AcceleratedPercent:F1}% / {audio.DeceleratedPercent:F1}%"));
        Pair("fec", audio.FecPackets.ToString(CultureInfo.InvariantCulture));
    }

    private void AddGraphs()
    {
        Header("hybrid.screen.perf.graphs");
        Graph("≈ ms", point => point.Delay, Level.Normal);
        Graph(Strings.Get("hybrid.screen.perf.decode") + " / " + Strings.Get("hybrid.screen.perf.encode") + " p95, ms",
            point => point.CodecMs, Level.Normal);
        Graph(Strings.Get("hybrid.screen.perf.loss") + ", %", point => point.Loss, Level.Warning);
        Graph(Strings.Get("hybrid.screen.perf.bitrate") + ", kbit/s", point => point.Kbps, Level.Good);
        Graph(Strings.Get("hybrid.screen.perf.shown") + ", fps", point => point.Fps, Level.Secondary);
    }

    private void Header(string key) => _lines.Add(Line.Header(Strings.Get(key)));

    private void Pair(string key, string value, Level level = Level.Normal) =>
        _lines.Add(Line.Pair(Strings.Get($"hybrid.screen.perf.{key}"), value, level));

    private void Graph(string label, Func<GraphPoint, double?> select, Level level)
    {
        var values = _history.Select(select).ToList();

        if (values.All(value => value == null))
            return;

        _lines.Add(Line.Graph(label, values, level));
    }

    private static Level LevelOf(double? value, double warning, double bad) => value switch
    {
        null => Level.Normal,
        _ when value >= bad => Level.Bad,
        _ when value >= warning => Level.Warning,
        _ => Level.Normal
    };

    private static Level Worse(Level a, Level b) => (Level)Math.Max((int)a, (int)b);

    private static string Ms(double? value) => value is { } ms ? Invariant($"{ms:F0} ms") : "—";

    private static string Latency(LatencyStats stats) => stats.IsEmpty
        ? "—"
        : Invariant($"{stats.P50Ms:F1} / {stats.P95Ms:F1} / {stats.MaxMs:F1} ms");

    private static string Timing(TimingStats stats) => Invariant($"{stats.AverageMs:F1} / {stats.MaxMs:F1} ms");

    private static string FormatKbps(double kbps) => kbps >= 1000
        ? Invariant($"{kbps / 1000:F1} Mbit/s")
        : Invariant($"{kbps:F0} kbit/s");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // ---- Layout and drawing ----

    private void Format()
    {
        var labelWidth = 0.0;
        var width = 0.0;
        var height = 0.0;

        foreach (var line in _lines)
            line.Format(this);

        foreach (var line in _lines.Where(line => line.Kind == LineKind.Pair))
            labelWidth = Math.Max(labelWidth, line.LabelText!.Width);

        foreach (var line in _lines)
        {
            width = Math.Max(width, line.Kind switch
            {
                LineKind.Pair => labelWidth + ColumnGap + line.ValueText!.Width,
                LineKind.Graph => Math.Max(GraphWidth, line.LabelText!.Width),
                _ => line.LabelText!.Width
            });
            height += line.Height + (line.Kind == LineKind.Header ? SectionGap : 0);
        }

        _labelWidth = labelWidth;
        _size = new Size(width + Padding * 2, height + Padding * 2);
        _shown = _lines.ToList();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        _shown.Count == 0 ? default : new Size(Math.Min(_size.Width, availableSize.Width), _size.Height);

    private double _labelWidth;

    public override void Render(DrawingContext context)
    {
        var lines = _shown;

        if (lines.Count == 0)
            return;

        var background = Brush("GeneralBrush") is ISolidColorBrush solid
            ? new SolidColorBrush(solid.Color, 0.86)
            : (IBrush)Brushes.Black;
        context.DrawRectangle(background, null, new Rect(Bounds.Size), 4, 4);

        using var clip = context.PushClip(new Rect(Bounds.Size));
        var y = Padding;

        foreach (var line in lines)
        {
            if (line.Kind == LineKind.Header)
                y += SectionGap;

            switch (line.Kind)
            {
                case LineKind.Pair:
                    line.LabelText!.SetForegroundBrush(BrushOf(Level.Secondary));
                    line.ValueText!.SetForegroundBrush(BrushOf(line.Level));
                    context.DrawText(line.LabelText, new Point(Padding, y));
                    context.DrawText(line.ValueText, new Point(Padding + _labelWidth + ColumnGap, y));
                    break;

                case LineKind.Graph:
                    line.LabelText!.SetForegroundBrush(BrushOf(Level.Secondary));
                    context.DrawText(line.LabelText, new Point(Padding, y));
                    DrawGraph(context, line, new Rect(Padding, y + line.LabelText.Height,
                        Math.Max(GraphWidth, Bounds.Width - Padding * 2), GraphHeight));
                    break;

                default:
                    line.LabelText!.SetForegroundBrush(line.Kind == LineKind.Header ? BrushOf(Level.Normal) : BrushOf(line.Level));
                    context.DrawText(line.LabelText, new Point(Padding, y));
                    break;
            }

            y += line.Height;
        }
    }

    private void DrawGraph(DrawingContext context, Line line, Rect area)
    {
        var values = line.Values!;
        var max = values.OfType<double>().DefaultIfEmpty(0).Max();
        var top = max <= 0 ? 1 : max * 1.15;
        var step = values.Count > 1 ? area.Width / (values.Count - 1) : area.Width;
        var stroke = new Pen(BrushOf(line.Level == Level.Normal ? Level.Primary : line.Level), 1.2);
        var grid = new Pen(Brush("SecondaryTextBrush") ?? Brushes.Gray, 0.5) { DashStyle = DashStyle.Dash };

        context.DrawLine(grid, area.BottomLeft, area.BottomRight);

        var geometry = new StreamGeometry();

        using (var figure = geometry.Open())
        {
            var isOpen = false;

            for (var index = 0; index < values.Count; index++)
            {
                if (values[index] is not { } value)
                {
                    if (isOpen)
                        figure.EndFigure(false);

                    isOpen = false;
                    continue;
                }

                var point = new Point(area.Left + index * step, area.Bottom - value / top * area.Height);

                if (isOpen)
                {
                    figure.LineTo(point);
                }
                else
                {
                    figure.BeginFigure(point, false);
                    isOpen = true;
                }
            }

            if (isOpen)
                figure.EndFigure(false);
        }

        context.DrawGeometry(null, stroke, geometry);

        // The scale and the latest reading, at the right
        var last = values.LastOrDefault(value => value != null);
        var text = new FormattedText(
            Invariant($"{last ?? 0:0.#} / max {max:0.#}"), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            _typeface, FontSize - 1, BrushOf(Level.Secondary));
        context.DrawText(text, new Point(area.Right - text.Width, area.Top));
    }

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

    private IBrush BrushOf(Level level) => level switch
    {
        Level.Bad => Brush("ErrorBrush"),
        Level.Warning => Brush("WarningBrush"),
        Level.Good => Brush("SuccessBrush"),
        Level.Secondary => Brush("SecondaryTextBrush"),
        Level.Primary => Brush("LinkTextBrush") ?? Brush("PrimaryTextBrush"),
        _ => Brush("PrimaryTextBrush")
    } ?? Brushes.White;

    private sealed record Reading(StreamPerf? Stream, NetworkPerf? Network);

    private readonly record struct GraphPoint(DateTime Time, double? Delay, double? CodecMs, double? Loss, double? Kbps,
        double? Fps);

    private enum Level
    {
        Normal,
        Primary,
        Secondary,
        Good,
        Warning,
        Bad
    }

    private enum LineKind
    {
        Summary,
        Header,
        Text,
        Pair,
        Graph
    }

    private sealed class Line
    {
        public LineKind Kind { get; private init; }
        public string Label { get; private init; } = "";
        public string? Value { get; private init; }
        public Level Level { get; private init; }
        public IReadOnlyList<double?>? Values { get; private init; }

        public FormattedText? LabelText { get; private set; }
        public FormattedText? ValueText { get; private set; }

        public double Height => Kind == LineKind.Graph
            ? LabelText!.Height + GraphHeight + 2
            : Math.Max(LabelText!.Height, ValueText?.Height ?? 0);

        public static Line Summary(string text, Level level) => new() { Kind = LineKind.Summary, Label = text, Level = level };

        public static Line Header(string text) => new() { Kind = LineKind.Header, Label = text };

        public static Line Text(string text, Level level) => new() { Kind = LineKind.Text, Label = text, Level = level };

        public static Line Pair(string label, string value, Level level) =>
            new() { Kind = LineKind.Pair, Label = label, Value = value, Level = level };

        public static Line Graph(string label, IReadOnlyList<double?> values, Level level) =>
            new() { Kind = LineKind.Graph, Label = label, Values = values, Level = level };

        public void Format(StreamPerfOverlay overlay)
        {
            var typeface = Kind is LineKind.Header or LineKind.Summary ? overlay._boldTypeface : overlay._typeface;
            var size = Kind == LineKind.Graph ? FontSize - 1 : FontSize;

            LabelText = new FormattedText(Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size,
                null);
            ValueText = Value == null
                ? null
                : new FormattedText(Value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, overlay._typeface,
                    FontSize, null);
        }
    }
}

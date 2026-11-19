using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Client.Core.Settings;

namespace Client.Avalonia.Controls.Indicators;

/// <summary>A microphone level and its configured gate threshold on the same decibel scale.</summary>
public sealed class VoiceGateMeter : Control
{
    public static readonly StyledProperty<double> LevelProperty =
        AvaloniaProperty.Register<VoiceGateMeter, double>(nameof(Level));
    public static readonly StyledProperty<double> ThresholdDbProperty =
        AvaloniaProperty.Register<VoiceGateMeter, double>(nameof(ThresholdDb), -32);
    public static readonly StyledProperty<bool> ShowThresholdProperty =
        AvaloniaProperty.Register<VoiceGateMeter, bool>(nameof(ShowThreshold), true);
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<VoiceGateMeter, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> LevelBrushProperty =
        AvaloniaProperty.Register<VoiceGateMeter, IBrush?>(nameof(LevelBrush));
    public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
        AvaloniaProperty.Register<VoiceGateMeter, IBrush?>(nameof(MarkerBrush));

    public double Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public double ThresholdDb { get => GetValue(ThresholdDbProperty); set => SetValue(ThresholdDbProperty, value); }
    public bool ShowThreshold { get => GetValue(ShowThresholdProperty); set => SetValue(ShowThresholdProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? LevelBrush { get => GetValue(LevelBrushProperty); set => SetValue(LevelBrushProperty, value); }
    public IBrush? MarkerBrush { get => GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }

    static VoiceGateMeter() => AffectsRender<VoiceGateMeter>(LevelProperty, ThresholdDbProperty,
        ShowThresholdProperty, TrackBrushProperty, LevelBrushProperty, MarkerBrushProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Match the inset of the slider thumb beneath the meter.
        const double inset = 9;
        var width = Math.Max(0, Bounds.Width - 2 * inset);
        if (width == 0 || Bounds.Height < 16)
            return;

        var track = new Rect(inset, 6, width, Bounds.Height - 12);
        context.DrawRectangle(TrackBrush, null, track, 4, 4);
        var level = double.IsFinite(Level) ? Math.Clamp(Level, 0, 1) : 0;
        if (level > 0)
            context.DrawRectangle(LevelBrush, null, new Rect(track.TopLeft, new Size(width * level, track.Height)), 4, 4);

        if (!ShowThreshold || MarkerBrush == null)
            return;

        var threshold = double.IsFinite(ThresholdDb) ? ThresholdDb : AudioSettings.MinVoiceGateThresholdDb;
        var fraction = Math.Clamp((threshold - AudioSettings.MinVoiceGateThresholdDb) /
            (AudioSettings.MaxVoiceGateThresholdDb - AudioSettings.MinVoiceGateThresholdDb), 0, 1);
        var x = inset + fraction * width;
        context.DrawLine(new Pen(MarkerBrush, 2), new Point(x, 0), new Point(x, Bounds.Height));
        context.DrawEllipse(MarkerBrush, null, new Point(x, 3), 3, 3);
    }
}

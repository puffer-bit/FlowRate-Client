using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using Client.Core.Sessions.Hybrid;

namespace Client.Views.Sessions.Hybrid.Video;

/// <summary>
/// Badges over a watched screen, one per area with trouble, coloured by the worst issue there; the
/// tooltip of a badge lists the issues with their figures. A badge comes quickly when its area gets
/// an issue, and fades out slowly once the issues there are gone.
/// </summary>
public sealed class StreamIssuesOverlay : StackPanel
{
    public static readonly StyledProperty<IReadOnlyList<StreamIssue>?> IssuesProperty =
        AvaloniaProperty.Register<StreamIssuesOverlay, IReadOnlyList<StreamIssue>?>(nameof(Issues));

    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(2500);

    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#FFC107"));
    private static readonly IBrush CriticalBrush = new SolidColorBrush(Color.Parse("#FF5252"));
    private static readonly IBrush BadgeBackground = new SolidColorBrush(Color.Parse("#B0101014"));

    // Drawn in a 24 × 24 box with a stroke of 2
    private static readonly Geometry NetworkIcon = Geometry.Parse(
        "M4,20 L4,16 M9,20 L9,12 M14,20 L14,8 M19,20 L19,4");
    private static readonly Geometry SourceIcon = Geometry.Parse(
        "M3,5 L21,5 L21,16 L3,16 Z M8,20 L16,20 M12,16 L12,20 M4,2 L20,19");
    private static readonly Geometry AudioIcon = Geometry.Parse(
        "M3,9 L7,9 L12,5 L12,19 L7,15 L3,15 Z M16,9.5 L21,14.5 M21,9.5 L16,14.5");
    private static readonly Geometry PerformanceIcon = Geometry.Parse(
        "M5.6,19.4 A9,9 0 1 1 18.4,19.4 M12,4 L12,6 M3,13 L5,13 M21,13 L19,13 " +
        "M13.1,11.9 L16.5,8.5 M13.5,13 A1.5,1.5 0 1 1 10.5,13 A1.5,1.5 0 1 1 13.5,13");

    // The transport has no picture of its own, the name of the protocol says more
    private const string TransportLabel = "TCP";

    private readonly Dictionary<StreamIssueArea, Badge> _badges = new();

    public IReadOnlyList<StreamIssue>? Issues
    {
        get => GetValue(IssuesProperty);
        set => SetValue(IssuesProperty, value);
    }

    public StreamIssuesOverlay()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 6;

        // Always in the same order, so a badge does not jump when another one comes or goes
        foreach (var area in Enum.GetValues<StreamIssueArea>())
        {
            var badge = new Badge(area);
            _badges[area] = badge;
            Children.Add(badge.Control);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IssuesProperty)
            Apply(Issues ?? []);
    }

    private void Apply(IReadOnlyList<StreamIssue> issues)
    {
        foreach (var (area, badge) in _badges)
        {
            var found = issues.Where(issue => issue.Area == area).ToList();

            if (found.Count > 0)
                badge.Show(found);
            else
                badge.Hide();
        }
    }

    private static string AreaTitle(StreamIssueArea area) => area switch
    {
        StreamIssueArea.Network => Strings.Get("hybrid.screen.issues.network"),
        StreamIssueArea.Transport => Strings.Get("hybrid.screen.issues.transport"),
        StreamIssueArea.Source => Strings.Get("hybrid.screen.issues.source"),
        StreamIssueArea.Audio => Strings.Get("hybrid.screen.issues.audio"),
        _ => Strings.Get("hybrid.screen.issues.performance")
    };

    private static string Describe(StreamIssue issue)
    {
        var value = Figure(issue.Value);

        return issue.Kind switch
        {
            StreamIssueKind.PacketLoss => Strings.Format("hybrid.screen.issues.packetLoss", ("percent", value)),
            StreamIssueKind.HighJitter => Strings.Format("hybrid.screen.issues.highJitter", ("ms", value)),
            StreamIssueKind.WaitingForKeyframe =>
                Strings.Format("hybrid.screen.issues.waitingForKeyframe", ("seconds", value)),
            StreamIssueKind.TcpTransport => Strings.Get("hybrid.screen.issues.tcpTransport"),
            StreamIssueKind.NoSourceFrames => Strings.Format("hybrid.screen.issues.noSourceFrames", ("seconds", value)),
            StreamIssueKind.NoAudio => Strings.Format("hybrid.screen.issues.noAudio", ("seconds", value)),
            StreamIssueKind.DecoderStalled => Strings.Format("hybrid.screen.issues.decoderStalled", ("seconds", value)),
            _ => Strings.Format("hybrid.screen.issues.rendererBehind", ("dropped", value),
                ("received", Figure(issue.Total)))
        };
    }

    // Whole numbers read best; a small share of a percent keeps one decimal
    private static string Figure(double value) =>
        value.ToString(value < 10 && value % 1 != 0 ? "0.#" : "0", CultureInfo.InvariantCulture);

    private sealed class Badge
    {
        private readonly StreamIssueArea _area;
        private readonly Path? _icon;
        private readonly TextBlock? _label;
        private readonly DoubleTransition _fade;

        // Counts the shows and hides, so a hide finishing late leaves a badge shown again alone
        private int _version;

        public Border Control { get; }

        public Badge(StreamIssueArea area)
        {
            _area = area;
            _fade = new DoubleTransition { Property = Visual.OpacityProperty };

            Control = new Border
            {
                MinWidth = 34,
                Height = 34,
                Padding = new Thickness(7, 0),
                CornerRadius = new CornerRadius(8),
                Background = BadgeBackground,
                Opacity = 0,
                IsVisible = false,
                Transitions = [_fade]
            };

            if (area == StreamIssueArea.Transport)
            {
                _label = new TextBlock
                {
                    Text = TransportLabel,
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Control.Child = _label;
                return;
            }

            _icon = new Path
            {
                Data = area switch
                {
                    StreamIssueArea.Network => NetworkIcon,
                    StreamIssueArea.Source => SourceIcon,
                    StreamIssueArea.Audio => AudioIcon,
                    _ => PerformanceIcon
                },
                StrokeThickness = area == StreamIssueArea.Network ? 3 : 2,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Width = 24,
                Height = 24
            };
            Control.Child = new Viewbox
            {
                Width = 20,
                Height = 20,
                Child = new Canvas { Width = 24, Height = 24, Children = { _icon } }
            };
        }

        public void Show(IReadOnlyList<StreamIssue> issues)
        {
            _version++;

            var brush = issues.Any(issue => issue.Severity == StreamIssueSeverity.Critical) ? CriticalBrush : WarningBrush;

            if (_icon != null)
                _icon.Stroke = brush;

            if (_label != null)
                _label.Foreground = brush;

            ToolTip.SetTip(Control, CreateTip(issues));

            _fade.Duration = FadeInDuration;
            _fade.Easing = new QuadraticEaseOut();
            Control.IsVisible = true;
            Control.Opacity = 1;
        }

        public void Hide()
        {
            if (!Control.IsVisible)
                return;

            var version = ++_version;
            _fade.Duration = FadeOutDuration;
            _fade.Easing = new SineEaseInOut();
            Control.Opacity = 0;

            // Takes the faded badge out of the row once it is no longer seen
            DispatcherTimer.RunOnce(() =>
            {
                if (version == _version)
                    Control.IsVisible = false;
            }, FadeOutDuration);
        }

        private Control CreateTip(IReadOnlyList<StreamIssue> issues)
        {
            var tip = new StackPanel { Spacing = 4, MaxWidth = 320 };
            tip.Children.Add(new TextBlock { Text = AreaTitle(_area), FontWeight = FontWeight.SemiBold });

            foreach (var issue in issues)
            {
                tip.Children.Add(new TextBlock
                {
                    Text = Describe(issue),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = issue.Severity == StreamIssueSeverity.Critical ? CriticalBrush : WarningBrush
                });
            }

            return tip;
        }
    }
}

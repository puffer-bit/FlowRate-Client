using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Client.Avalonia.Video.Preview.Analyzers;

namespace Client.Avalonia.Video.Preview;

/// <summary>
/// The video surface of the app fed with a test picture, with the issue badges over it switching
/// to the next scenario every few seconds.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly TimeSpan ScenarioDuration = TimeSpan.FromSeconds(6);

    private readonly Grid _videoHost = new();
    private readonly StreamIssuesOverlay _overlay = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(8)
    };
    private readonly TextBlock _scenarioText = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _countdownText = new() { Opacity = 0.7 };
    private readonly TextBlock _issuesText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _pauseButton = new() { Content = "Pause" };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private VideoView? _video;
    private PatternFeed? _feed;
    private int _scenario = -1;
    private DateTime _scenarioEnds;
    private bool _isPaused;

    public MainWindow()
    {
        Title = "FlowRate — video analyzers test";
        Width = 1280;
        Height = 760;

        var nextButton = new Button { Content = "Next" };
        nextButton.Click += (_, _) => NextScenario();
        _pauseButton.Click += (_, _) => TogglePause();

        var panel = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(16),
            Children =
            {
                _scenarioText,
                _countdownText,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { nextButton, _pauseButton } },
                new Separator(),
                _issuesText
            }
        };

        _videoHost.Background = Brushes.Black;
        _videoHost.Children.Add(_overlay);

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,300") };
        layout.Children.Add(_videoHost);
        Grid.SetColumn(panel, 1);
        layout.Children.Add(panel);
        Content = layout;

        _timer.Tick += (_, _) => Tick();
        Opened += async (_, _) => await StartAsync();
    }

    private async System.Threading.Tasks.Task StartAsync()
    {
        // The surface picks its presenter from the probe, so it is created once the probe is done
        await VideoSurfaceProbe.RunAsync(this, new ConsoleLogger());
        DebugSettings.Apply(DebugSettings.Current with { ShowFrameStats = true });

        _video = new VideoView();
        _videoHost.Children.Insert(0, _video);
        _feed = new PatternFeed(_video.PushFrame, IssueScenarios.NormalFramerate);

        NextScenario();
        _timer.Start();
    }

    private void Tick()
    {
        if (_isPaused)
            return;

        var left = _scenarioEnds - DateTime.UtcNow;

        if (left <= TimeSpan.Zero)
            NextScenario();
        else
            _countdownText.Text = $"Next in {left.TotalSeconds:0.0} s";
    }

    private void NextScenario()
    {
        _scenario = (_scenario + 1) % IssueScenarios.All.Count;
        _scenarioEnds = DateTime.UtcNow + ScenarioDuration;

        var scenario = IssueScenarios.All[_scenario];
        _scenarioText.Text = $"{_scenario + 1}/{IssueScenarios.All.Count}. {scenario.Name}";
        _issuesText.Text = scenario.Issues.Count == 0
            ? "No badges"
            : string.Join("\n\n", scenario.Issues.Select(issue =>
                $"[{issue.Area}, {issue.Severity}]\n{Describe(issue)}"));

        _overlay.Issues = scenario.Issues;

        if (_feed != null)
            _feed.Framerate = scenario.Framerate;

        if (scenario.ClearsPicture)
            _video?.Clear();
    }

    private static string Describe(StreamIssue issue) =>
        issue.Total > 0 ? $"{issue.Kind}: {issue.Value} of {issue.Total}"
        : issue.Value > 0 ? $"{issue.Kind}: {issue.Value}"
        : issue.Kind.ToString();

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        _pauseButton.Content = _isPaused ? "Resume" : "Pause";

        if (_isPaused)
            _countdownText.Text = "Paused";
        else
            _scenarioEnds = DateTime.UtcNow + ScenarioDuration;
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _feed?.Dispose();
        base.OnClosed(e);
    }
}

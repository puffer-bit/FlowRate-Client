using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports;
using Client.GStreamer.Loader;
using Client.Services.ScreenCast.Linux;
using Microsoft.Extensions.Logging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Avalonia.Controls.Media;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Shared.Models.Media;
using Client.Avalonia.Video.Preview.Analyzers;

namespace Client.Avalonia.Video.Preview;

/// <summary>
/// The production video surface with test scenarios and an interactive PipeWire capture.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly TimeSpan ScenarioDuration = TimeSpan.FromSeconds(6);

    private readonly ConsoleLogger _logger = new();
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
    private readonly StackPanel _scenarioControls = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Button _pauseButton = new() { Content = "Pause" };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private readonly Button _captureButton = new() { Content = "Start PipeWire", IsEnabled = false };
    private readonly CheckBox _copyBuffers = new() { Content = "Copy PipeWire buffers", IsChecked = false };
    private readonly CheckBox _dmaBufCapture = new() { Content = "DMA-BUF (GPU capture)", IsChecked = true };
    private readonly CheckBox _captureOnly = new() { Content = "Capture only (no rendering)", IsChecked = false };
    private readonly TextBlock _captureText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CancellationTokenSource _lifetime = new();
    private XdgDesktopPortalClient? _portal;
    private PipeWireFeed? _capture;
    private PipeWireVideoInput? _input;
    private bool _selecting;
    private bool _closed;
    private VideoView? _video;
    private StreamPerfOverlay? _perf;
    private DateTime _perfSampled;
    private PatternFeed? _feed;
    private int _scenario = -1;
    private DateTime _scenarioEnds;
    private bool _isPaused;

    public MainWindow()
    {
        Title = "FlowRate — video analyzers test";
        Width = 1280;
        Height = 760;

        _captureButton.IsVisible = OperatingSystem.IsLinux();
        _captureButton.Click += async (_, _) => await ToggleCaptureAsync();
        var nextButton = new Button { Content = "Next" };
        nextButton.Click += (_, _) => NextScenario();
        _pauseButton.Click += (_, _) => TogglePause();
        _scenarioControls.Children.Add(nextButton);
        _scenarioControls.Children.Add(_pauseButton);

        var panel = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(16),
            Children =
            {
                _captureButton,
                _copyBuffers,
                _captureOnly,
                _dmaBufCapture,
                _captureText,
                _scenarioText,
                _countdownText,
                _scenarioControls,
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
        Opened += async (_, _) =>
        {
            try { await StartAsync(); }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Video preview initialization failed");
                _captureText.Text = exception.Message;
            }
        };
    }

    private async System.Threading.Tasks.Task StartAsync()
    {
        // The surface picks its presenter from the probe, so it is created once the probe is done
        await VideoSurfaceProbe.RunAsync(this, _logger);
        if (_closed) return;
        DebugSettings.Apply(DebugSettings.Current with
        {
            MediaPerfOverlay = MediaPerfOverlayMode.AllStreams,
            IsMediaPerfExpanded = true
        });

        _video = new VideoView();
        _videoHost.Children.Insert(0, _video);
        _videoHost.Children.Insert(1, _perf = new StreamPerfOverlay
        {
            ReadFrames = () => _video.Stats,
            ReadRoute = () => _video.Route,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6)
        });
        _feed = new PatternFeed(_video.PushFrame, IssueScenarios.NormalFramerate);

        NextScenario();
        _captureButton.IsEnabled = true;
        _timer.Start();
    }

    private void Tick()
    {
        if (_capture != null)
        {
            if (DateTime.UtcNow - _perfSampled >= TimeSpan.FromSeconds(1))
            {
                _perfSampled = DateTime.UtcNow;
                _perf?.SetPerf(null, null);
            }
            return;
        }
        if (_isPaused || _selecting) return;

        var left = _scenarioEnds - DateTime.UtcNow;

        if (left <= TimeSpan.Zero)
            NextScenario();
        else
            _countdownText.Text = $"Next in {left.TotalSeconds:0.0} s";

        if (DateTime.UtcNow - _perfSampled >= TimeSpan.FromSeconds(1))
        {
            _perfSampled = DateTime.UtcNow;
            _perf?.SetPerf(SamplePerf(), new NetworkPerf(18 + Random.Shared.Next(10), 6200, 90, 4100, false, "host", "host"));
        }
    }

    // Made-up statistics of a watched screen, so every part of the overlay can be seen
    private static StreamPerf SamplePerf()
    {
        var wobble = Random.Shared.NextDouble();
        var decode = new LatencyStats(1.2 + wobble, 2.5 + wobble * 3, 6 + wobble * 10, 30);

        return new StreamPerf(2, MediaSourceType.Screen, false,
            new VideoReceivePerf(4000 + wobble * 300, 380, wobble * 3, 2, 20 + wobble * 30, 30, 0.4, 3, 30, 30, 0,
                wobble > 0.9 ? 1 : 0, wobble > 0.9 ? 300 : 0, 2, 0, 0, 0, 0, 0,
                WaitMs: new TimingStats(2 + wobble * 5, 12, 30), HandoffMs: new TimingStats(0.1, 0.4, 30)),
            new DecoderPerf("vah264dec", true, 30, 30, decode, 0, wobble > 0.7 ? 3 : 1, 0),
            Audio: new AudioReceivePerf(96, wobble, 2, 45, 40, wobble * 2, 0.2, 0.1, 0));
    }

    private void NextScenario()
    {
        if (_capture != null || _selecting) return;
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

    private async Task ToggleCaptureAsync()
    {
        _captureButton.IsEnabled = false;
        _copyBuffers.IsEnabled = false;
        _captureOnly.IsEnabled = false;
        _dmaBufCapture.IsEnabled = false;
        _selecting = true;
        try
        {
            if (_capture != null)
            {
                StopCapture();
                if (_portal != null) await _portal.CloseAsync();
                StartPattern();
                return;
            }

            _selecting = true;
            _scenarioControls.IsVisible = false;
            _overlay.Issues = [];
            _perf?.Reset();
            _scenarioText.Text = "PipeWire";
            _countdownText.Text = "";
            _issuesText.Text = "";
            _captureText.Text = "Select a screen or window in the system dialog…";
            if (GstLoader.Options == null && !GstLoader.LoadGstreamer(_logger))
                throw new InvalidOperationException("GStreamer runtime is unavailable; see console output.");
            if (_portal == null)
            {
                _portal = new XdgDesktopPortalClient(new ConsoleLogger<XdgDesktopPortalClient>());
                _portal.Closed += () => Dispatcher.UIThread.Post(() =>
                {
                    if (_closed) return;
                    StopCapture();
                    StartPattern();
                    _captureText.Text = "Sharing stopped by the desktop.";
                });
            }
            var selected = await _portal.SelectSourceAsync(_lifetime.Token);
            if (selected is not PipeWireVideoInput input)
            {
                StartPattern();
                _captureText.Text = "Selection cancelled.";
                return;
            }
            if (_closed) { input.Remote.Dispose(); return; }
            _input = input;
            _feed?.Dispose();
            _feed = null;
            _video?.Clear();
            _perf?.Reset();
            _overlay.Issues = [];
            _scenarioText.Text = "PipeWire — live preview";
            _countdownText.Text = "Capture limit: 144 FPS";
            _issuesText.Text = _dmaBufCapture.IsChecked == true
                ? "PipeWire → DMA-BUF → OpenGL" : "PipeWire → BGRA → VideoView";
            _capture = new PipeWireFeed(input, 144, _video!.PushFrame,
                (rate, caps) => Dispatcher.UIThread.Post(() =>
                {
                    if (_capture != null) _issuesText.Text = $"Capture: {rate:0.0} FPS\n{caps}";
                }),
                rate => Dispatcher.UIThread.Post(() =>
                {
                    if (_capture != null) _countdownText.Text = $"Source: {rate:0.0} FPS · display limit: 144";
                }),
                error =>
                {
                    _logger.LogError(error, "PipeWire capture failed");
                    Dispatcher.UIThread.Post(async () =>
                    {
                        if (_closed) return;
                        StopCapture();
                        if (_portal != null) await _portal.CloseAsync();
                        StartPattern();
                        _captureText.Text = error.Message;
                    });
                }, _logger, _copyBuffers.IsChecked == true, _captureOnly.IsChecked == true, _dmaBufCapture.IsChecked == true);
            _perf?.SetPerf(null, null);
            _captureButton.Content = "Stop PipeWire";
            _captureText.Text = $"PipeWire node {input.NodeId} · {input.Width}×{input.Height}";
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to start or stop PipeWire preview");
            StopCapture();
            if (_portal != null && !_closed) await _portal.CloseAsync();
            if (!_closed) StartPattern();
            _captureText.Text = exception.Message;
        }
        finally
        {
            _selecting = false;
            _captureButton.IsEnabled = !_closed;
            _copyBuffers.IsEnabled = _capture == null;
            _captureOnly.IsEnabled = _capture == null;
            _dmaBufCapture.IsEnabled = _capture == null;
        }
    }

    private void StopCapture()
    {
        _capture?.Dispose();
        _capture = null;
        _input?.Remote.Dispose();
        _input = null;
        _perf?.Reset();
        _video?.Clear();
        _captureButton.Content = "Start PipeWire";
        _copyBuffers.IsEnabled = true;
        _captureOnly.IsEnabled = true;
        _dmaBufCapture.IsEnabled = true;
    }

    private void StartPattern()
    {
        if (_closed || _video == null) return;
        _selecting = false;
        _scenarioControls.IsVisible = true;
        _feed ??= new PatternFeed(_video.PushFrame, IssueScenarios.NormalFramerate);
        NextScenario();
    }

    private static string Describe(StreamIssue issue) =>
        issue.Total > 0 ? $"{issue.Kind}: {issue.Value} of {issue.Total}"
        : issue.Value > 0 ? $"{issue.Kind}: {issue.Value}"
        : issue.Kind.ToString();

    private void TogglePause()
    {
        if (_selecting || _capture != null) return;
        _isPaused = !_isPaused;
        _pauseButton.Content = _isPaused ? "Resume" : "Pause";

        if (_isPaused)
            _countdownText.Text = "Paused";
        else
            _scenarioEnds = DateTime.UtcNow + ScenarioDuration;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _lifetime.Cancel();
        _timer.Stop();
        StopCapture();
        _portal?.Dispose();
        _feed?.Dispose();
        base.OnClosed(e);
    }
}

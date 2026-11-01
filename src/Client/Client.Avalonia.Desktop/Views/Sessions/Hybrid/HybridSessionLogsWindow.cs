using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using Client.Tools.Logging;
using Client.ViewModels.Sessions.Hybrid;
using Serilog.Events;

namespace Client.Views.Sessions.Hybrid;

/// <summary>A bounded, live view of events explicitly attributed to one session.</summary>
internal sealed class HybridSessionLogsWindow : Window
{
    private static readonly Dictionary<(Window Owner, int SessionId), HybridSessionLogsWindow> Windows = new();
    private readonly ObservableCollection<LogEntry> _entries = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly ListBox _list;
    private readonly TextBlock _empty;
    private readonly CheckBox _follow;
    private readonly int _sessionId;
    private long _sequence;

    public static void Open(Window owner, HybridSessionStatusBarVM session)
    {
        var key = (owner, session.SessionId);
        if (Windows.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return;
        }

        var window = new HybridSessionLogsWindow(session);
        Windows.Add(key, window);
        window.Closed += (_, _) => Windows.Remove(key);
        window.Show(owner);
    }

    private HybridSessionLogsWindow(HybridSessionStatusBarVM session)
    {
        _sessionId = session.SessionId;
        Title = $"{Strings.Get("hybrid.logs.open")} — {session.SessionName}";
        Width = 1000;
        Height = 600;
        MinWidth = 560;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _follow = new CheckBox { Content = Strings.Get("hybrid.logs.follow"), IsChecked = true };
        _empty = new TextBlock
        {
            Text = Strings.Get("hybrid.logs.empty"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _list = new ListBox
        {
            ItemsSource = _entries,
            ItemTemplate = new FuncDataTemplate<LogEntry>((entry, _) => new SelectableTextBlock
            {
                Text = entry == null ? "" :
                    $"{entry.Timestamp:HH:mm:ss.fff}  {entry.Level,-11}  {entry.ShortCategory}\n{entry.Message}"
                    + (entry.Exception == null ? "" : $"\n{entry.Exception}"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4),
                Foreground = entry?.Level switch
                {
                    LogEventLevel.Error or LogEventLevel.Fatal => Brushes.IndianRed,
                    LogEventLevel.Warning => Brushes.DarkOrange,
                    _ => Foreground
                }
            })
        };
        var journal = new Grid();
        journal.Children.Add(_list);
        journal.Children.Add(_empty);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(12), RowSpacing = 8 };
        layout.Children.Add(_follow);
        Grid.SetRow(journal, 1);
        layout.Children.Add(journal);
        Content = layout;

        _timer.Tick += (_, _) => Refresh();
        Opened += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var snapshot = ClientLogging.Memory.Snapshot();
        // A clear removes retained history too, even if the next event is for another session.
        var oldest = snapshot.Count == 0 ? long.MaxValue : snapshot[0].Sequence;
        while (_entries.Count > 0 && _entries[0].Sequence < oldest)
            _entries.RemoveAt(0);

        var added = false;
        foreach (var entry in snapshot.Where(entry => entry.Sequence > _sequence && entry.SessionId == _sessionId))
        {
            _entries.Add(entry);
            added = true;
        }
        if (snapshot.Count > 0)
            _sequence = snapshot[^1].Sequence;
        _empty.IsVisible = _entries.Count == 0;
        if (added && _follow.IsChecked == true)
            _list.ScrollIntoView(_entries[^1]);
    }
}

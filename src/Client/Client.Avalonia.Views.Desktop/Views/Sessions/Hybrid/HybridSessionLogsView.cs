using System;
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
using Client.Views.Dialogs;
using Client.ViewModels.Sessions.Hybrid;
using Serilog.Events;

namespace Client.Views.Sessions.Hybrid;

/// <summary>A bounded, live view of events explicitly attributed to one session, shown as a dialog.</summary>
internal sealed class HybridSessionLogsView : UserControl
{
    private readonly ObservableCollection<LogEntry> _entries = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly ListBox _list;
    private readonly TextBlock _empty;
    private readonly CheckBox _follow;
    private readonly int _sessionId;
    private long _sequence;

    /// <summary>Shows the journal of <paramref name="session"/> over the window that hosts <paramref name="owner"/>.</summary>
    public static void Open(Visual owner, HybridSessionStatusBarVM session)
    {
        if (InWindowDialog.CanShow(owner))
            _ = InWindowDialog.ShowAsync<object?>(new HybridSessionLogsView(session), owner, null, fillWindow: true);
    }

    private HybridSessionLogsView(HybridSessionStatusBarVM session)
    {
        _sessionId = session.SessionId;
        MaxWidth = 1000;
        MaxHeight = 600;

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
        var close = new Button { Content = Strings.Get("common.close"), IsCancel = true };
        close.Click += (_, _) => InWindowDialog.Close(this);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        header.Children.Add(new TextBlock
        {
            Text = $"{Strings.Get("hybrid.logs.open")} — {session.SessionName}", FontSize = 16,
            FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(20, 16), RowSpacing = 8 };
        layout.Children.Add(header);
        Grid.SetRow(_follow, 1);
        layout.Children.Add(_follow);
        Grid.SetRow(journal, 2);
        layout.Children.Add(journal);
        Content = layout;

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
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

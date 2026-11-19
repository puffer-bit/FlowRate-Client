using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Client.Avalonia.Icons;
using Client.Avalonia.Localization;
using Client.Core.Chat;

namespace Client.Avalonia.Controls.Emoji;

public sealed class EmojiPickedEventArgs(EmojiEntry entry) : EventArgs
{
    public EmojiEntry Entry { get; } = entry;
    public bool Accepted { get; set; }
    public string? RejectionReason { get; set; }
}

public sealed class EmojiCell(EmojiEntry entry, bool russian) : INotifyPropertyChanged
{
    public EmojiEntry Entry { get; } = entry;
    public string Name { get; } = entry.Name(russian);
    private bool _selected;
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record EmojiRow(IReadOnlyList<EmojiCell> Cells);

/// <summary>A reusable offline picker. Its host inserts text and persists the shared recent list.</summary>
public partial class EmojiPickerView : UserControl
{
    private static readonly string[] CategoryIcons = ["◷", "😀", "👋", "🐻", "🍎", "🚗", "⚽", "💡", "❤", "🏳"];
    private RecentEmoji _recent = new();
    private EmojiCell[] _cells = [];
    private EmojiRow[] _rows = [];
    private int _category = -1;
    private int _columns = 8;
    private int _selected;
    private bool _ready;
    private bool _gridNavigation;

    public event EventHandler<EmojiPickedEventArgs>? EmojiPicked;
    public event EventHandler? CloseRequested;
    public event EventHandler? RecentChanged;

    public EmojiPickerView()
    {
        InitializeComponent();
        _ready = true;
        SearchInput.PointerPressed += (_, _) => _gridNavigation = false;
        AddHandler(KeyDownEvent, OnPickerKeyDown, RoutingStrategies.Tunnel);
        SizeChanged += (_, _) =>
        {
            int columns = Math.Max(1, (int)((Bounds.Width - 28) / 36));
            if (columns == _columns) return;
            _columns = columns;
            // SizeChanged runs during layout. Replacing virtualized items there can leave an
            // unowned row container on screen, so reflow after the current layout has finished.
            Dispatcher.UIThread.Post(BuildRows);
        };
        AttachedToVisualTree += (_, _) => Strings.LanguageChanged += OnLanguageChanged;
        DetachedFromVisualTree += (_, _) => Strings.LanguageChanged -= OnLanguageChanged;
    }

    public void Open(RecentEmoji recent)
    {
        _recent = recent;
        _category = recent.Entries.Count > 0 ? -1 : 0;
        SearchInput.Text = "";
        BuildCategories();
        Refresh();
        SearchInput.Focus();
    }

    private void OnLanguageChanged()
    {
        BuildCategories();
        Refresh();
    }

    private void BuildCategories()
    {
        Categories.Children.Clear();
        for (int category = -1; category < 9; category++)
        {
            int value = category;
            string name = CategoryName(value);
            var button = new Button
            {
                Width = 30, Height = 32,
                Content = new TextBlock { Text = CategoryIcons[category + 1], FontSize = 18,
                    FontFamily = category == -1 ? FontFamily : EmojiFont.Family }
            };
            button.Classes.Add("emoji-category");
            button.Classes.Set("selected", value == _category && string.IsNullOrWhiteSpace(SearchInput.Text));
            ToolTip.SetTip(button, name);
            global::Avalonia.Automation.AutomationProperties.SetName(button, name);
            button.Click += (_, _) =>
            {
                _category = value;
                SearchInput.Text = "";
                Refresh();
                SearchInput.Focus();
            };
            Categories.Children.Add(button);
        }
    }

    private static string CategoryName(int category) => Strings.Get(category < 0
        ? "chat.emoji.recent" : $"chat.emoji.category.{category}");

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _gridNavigation = false;
        if (_ready) Refresh();
    }

    private void Refresh()
    {
        SelectionNotice.IsVisible = false;
        bool searching = !string.IsNullOrWhiteSpace(SearchInput.Text);
        var entries = !searching && _category == -1 ? _recent.Entries
            : EmojiCatalog.Search(SearchInput.Text, _category < 0 ? null : _category);
        bool russian = Strings.Current.Code.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        _cells = entries.Select(entry => new EmojiCell(entry, russian)).ToArray();
        _selected = 0;
        if (_cells.Length > 0) _cells[0].IsSelected = true;
        CategoryTitle.Text = searching ? Strings.Get("chat.emoji.results") : CategoryName(_category);
        EmptyMessage.IsVisible = _cells.Length == 0;
        foreach (var button in Categories.Children.OfType<Button>().Select((button, index) => (button, index)))
            button.button.Classes.Set("selected", !searching && button.index - 1 == _category);
        BuildRows();
        if (_rows.Length > 0) EmojiRows.ScrollIntoView(_rows[0]);
    }

    private void BuildRows()
    {
        _rows = _cells.Chunk(_columns).Select(cells => new EmojiRow(cells)).ToArray();
        EmojiRows.ItemsSource = _rows;
        if (_rows.Length > 0) EmojiRows.ScrollIntoView(_rows[Math.Min(_selected / _columns, _rows.Length - 1)]);
    }

    private void OnEmojiClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: EmojiCell cell }) Pick(cell.Entry);
    }

    private void OnEmojiFocused(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: EmojiCell cell }) return;
        int index = Array.IndexOf(_cells, cell);
        if (index < 0) return;
        _cells[_selected].IsSelected = false;
        _selected = index;
        cell.IsSelected = true;
        _gridNavigation = true;
    }

    private void Pick(EmojiEntry entry)
    {
        var args = new EmojiPickedEventArgs(entry);
        EmojiPicked?.Invoke(this, args);
        if (args.Accepted)
        {
            _recent.Record(entry);
            RecentChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (args.RejectionReason != null)
        {
            SelectionNotice.Text = args.RejectionReason;
            SelectionNotice.IsVisible = true;
        }
    }

    private void OnPickerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        if (_cells.Length == 0 || e.KeyModifiers != KeyModifiers.None) return;
        // Left/right remain text editing keys in search. Tab continues normal focus navigation.
        int delta = e.Key switch
        {
            Key.Down => _columns,
            Key.Up => -_columns,
            Key.Right when _gridNavigation || !SearchInput.IsKeyboardFocusWithin => 1,
            Key.Left when _gridNavigation || !SearchInput.IsKeyboardFocusWithin => -1,
            _ => 0
        };
        if (delta != 0)
        {
            _cells[_selected].IsSelected = false;
            _selected = _gridNavigation ? Math.Clamp(_selected + delta, 0, _cells.Length - 1) : 0;
            _gridNavigation = true;
            _cells[_selected].IsSelected = true;
            EmojiRows.ScrollIntoView(_rows[_selected / _columns]);
            SearchInput.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (SearchInput.IsKeyboardFocusWithin || EmojiRows.IsKeyboardFocusWithin))
        {
            Pick(_cells[_selected].Entry);
            e.Handled = true;
        }
    }
}

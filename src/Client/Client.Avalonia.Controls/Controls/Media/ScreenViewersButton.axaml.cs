using Avalonia;
using Avalonia.Controls;
using System.Collections.ObjectModel;

namespace Client.Avalonia.Controls.Media;

public partial class ScreenViewersButton : UserControl
{
    private const int PreviewLimit = 5;

    public static readonly StyledProperty<IReadOnlyList<ScreenViewerItem>> ViewersProperty =
        AvaloniaProperty.Register<ScreenViewersButton, IReadOnlyList<ScreenViewerItem>>(nameof(Viewers), []);

    public static readonly DirectProperty<ScreenViewersButton, IReadOnlyList<ScreenViewerItem>> PreviewProperty =
        AvaloniaProperty.RegisterDirect<ScreenViewersButton, IReadOnlyList<ScreenViewerItem>>(nameof(Preview), o => o.Preview);

    public static readonly DirectProperty<ScreenViewersButton, IReadOnlyList<ScreenViewerItem>> AudienceProperty =
        AvaloniaProperty.RegisterDirect<ScreenViewersButton, IReadOnlyList<ScreenViewerItem>>(nameof(Audience), o => o.Audience);

    public static readonly DirectProperty<ScreenViewersButton, string> OverflowTextProperty =
        AvaloniaProperty.RegisterDirect<ScreenViewersButton, string>(nameof(OverflowText), o => o.OverflowText);

    public static readonly DirectProperty<ScreenViewersButton, bool> HasViewersProperty =
        AvaloniaProperty.RegisterDirect<ScreenViewersButton, bool>(nameof(HasViewers), o => o.HasViewers);

    private readonly ObservableCollection<ScreenViewerItem> _preview = [];
    private readonly ObservableCollection<ScreenViewerItem> _audience = [];
    private string _overflowText = "";
    private bool _hasViewers;

    public IReadOnlyList<ScreenViewerItem> Viewers
    {
        get => GetValue(ViewersProperty);
        set => SetValue(ViewersProperty, value);
    }

    public IReadOnlyList<ScreenViewerItem> Preview => _preview;
    public IReadOnlyList<ScreenViewerItem> Audience => _audience;
    public string OverflowText => _overflowText;
    public bool HasViewers => _hasViewers;

    public ScreenViewersButton() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ViewersProperty)
            return;

        // Keep existing avatar controls alive when another viewer joins or leaves, so their
        // pictures are not reloaded and briefly replaced by initials.
        Synchronize(_audience, Viewers);
        Synchronize(_preview, Viewers.Take(PreviewLimit).ToArray());
        SetAndRaise(OverflowTextProperty, ref _overflowText,
            Viewers.Count > PreviewLimit ? $"+{Viewers.Count - PreviewLimit}" : "");
        SetAndRaise(HasViewersProperty, ref _hasViewers, Viewers.Count > 0);
    }

    private static void Synchronize(ObservableCollection<ScreenViewerItem> current,
        IReadOnlyList<ScreenViewerItem> updated)
    {
        var ids = updated.Select(viewer => viewer.UserId).ToHashSet();
        for (var index = current.Count - 1; index >= 0; index--)
            if (!ids.Contains(current[index].UserId))
                current.RemoveAt(index);

        for (var index = 0; index < updated.Count; index++)
        {
            var viewer = updated[index];
            var existing = -1;
            for (var candidate = index; candidate < current.Count; candidate++)
                if (current[candidate].UserId == viewer.UserId)
                {
                    existing = candidate;
                    break;
                }

            if (existing < 0)
                current.Insert(index, viewer);
            else
            {
                if (existing != index)
                    current.Move(existing, index);
                if (current[index] != viewer)
                    current[index] = viewer;
            }
        }
    }
}

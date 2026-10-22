using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Client.Avalonia.Controls.Icons;
using Client.Avalonia.Controls.Images;

namespace Client.Avalonia.Controls.Avatars;

/// <summary>
/// The avatar of a user: a picture cropped to a circle, or the first letter of the name in a filled
/// circle when there is none. The picture is fetched by <see cref="AvatarUrl"/>, or given as <see cref="Source"/>. Scales with its size, so the same look fits a 20px list row and a
/// 32px profile slot. A <see cref="Status"/> puts its badge at the lower right corner. A deleted user
/// gets a ghost in place of the initial.
/// </summary>
[PseudoClasses(SpeakingPseudoClass, WaitingPseudoClass, DeletedPseudoClass)]
public class UserAvatar : TemplatedControl
{
    private const string SpeakingPseudoClass = ":speaking";
    private const string WaitingPseudoClass = ":waiting";
    private const string DeletedPseudoClass = ":deleted";

    public static readonly StyledProperty<bool> IsDeletedProperty =
        AvaloniaProperty.Register<UserAvatar, bool>(nameof(IsDeleted));

    public static readonly StyledProperty<bool> IsSpeakingProperty =
        AvaloniaProperty.Register<UserAvatar, bool>(nameof(IsSpeaking));

    public static readonly StyledProperty<bool> IsWaitingProperty =
        AvaloniaProperty.Register<UserAvatar, bool>(nameof(IsWaiting));

    public static readonly StyledProperty<string?> UserNameProperty =
        AvaloniaProperty.Register<UserAvatar, string?>(nameof(UserName));

    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<UserAvatar, bool>(nameof(IsAnimated));

    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<UserAvatar, IImage?>(nameof(Source));

    public static readonly StyledProperty<string?> AvatarUrlProperty =
        AvaloniaProperty.Register<UserAvatar, string?>(nameof(AvatarUrl));

    public static readonly StyledProperty<string?> CoordinatorProperty =
        AvaloniaProperty.Register<UserAvatar, string?>(nameof(Coordinator));

    public static readonly StyledProperty<Enum?> StatusProperty =
        AvaloniaProperty.Register<UserAvatar, Enum?>(nameof(Status));

    public static readonly StyledProperty<IBrush?> StatusBackgroundProperty =
        AvaloniaProperty.Register<UserAvatar, IBrush?>(nameof(StatusBackground));

    public static readonly DirectProperty<UserAvatar, IImage?> StatusIconProperty =
        AvaloniaProperty.RegisterDirect<UserAvatar, IImage?>(nameof(StatusIcon), avatar => avatar.StatusIcon);

    public static readonly DirectProperty<UserAvatar, string> InitialProperty =
        AvaloniaProperty.RegisterDirect<UserAvatar, string>(nameof(Initial), avatar => avatar.Initial);

    public static readonly DirectProperty<UserAvatar, IImage?> PictureProperty =
        AvaloniaProperty.RegisterDirect<UserAvatar, IImage?>(nameof(Picture), avatar => avatar.Picture);

    /// <summary>
    /// Fetches the picture at an address a coordinator gave; null when there is none to show. Set once
    /// by the application, which knows where avatars come from. The first argument is
    /// <see cref="Coordinator"/>.
    /// </summary>
    public static Func<string?, string, Task<IImage?>>? Loader { get; set; }

    private string _initial = "?";
    private IImage? _picture;
    private IImage? _statusIcon;
    private IImage? _loaded;

    // Tells the answer to the last request from the answers to the ones before it
    private int _loadVersion;

    static UserAvatar()
    {
        // The speaking ring is drawn around the avatar, past its bounds; a templated control clips
        // to them by default, which left only the corners of the ring
        ClipToBoundsProperty.OverrideDefaultValue<UserAvatar>(false);
    }

    public string? UserName
    {
        get => GetValue(UserNameProperty);
        set => SetValue(UserNameProperty, value);
    }

    /// <summary>
    /// The address of the avatar as the coordinator gives it for a user.
    /// </summary>
    public string? AvatarUrl
    {
        get => GetValue(AvatarUrlProperty);
        set => SetValue(AvatarUrlProperty, value);
    }

    /// <summary>
    /// The id of the coordinator the user belongs to. Not set where it is the one we are connected to.
    /// </summary>
    public string? Coordinator
    {
        get => GetValue(CoordinatorProperty);
        set => SetValue(CoordinatorProperty, value);
    }

    /// <summary>
    /// A picture to show instead of the one at <see cref="AvatarUrl"/>.
    /// </summary>
    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// The user was deleted: a ghost is drawn instead of the initial.
    /// </summary>
    public bool IsDeleted
    {
        get => GetValue(IsDeletedProperty);
        set => SetValue(IsDeletedProperty, value);
    }

    /// <summary>
    /// Whether the user speaks right now; lights the ring around the avatar.
    /// </summary>
    public bool IsSpeaking
    {
        get => GetValue(IsSpeakingProperty);
        set => SetValue(IsSpeakingProperty, value);
    }

    /// <summary>
    /// Whether an animated picture moves; otherwise it holds still on its first frame, so a screen full
    /// of avatars does not flicker.
    /// </summary>
    public bool IsAnimated
    {
        get => GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    /// <summary>
    /// Whether the user waits for the host to let them in; keeps the ring lit in the warning color.
    /// </summary>
    public bool IsWaiting
    {
        get => GetValue(IsWaitingProperty);
        set => SetValue(IsWaitingProperty, value);
    }

    /// <summary>
    /// The status of the user, shown as the icon "Status{name}Icon" of the assets, so a value of any enum
    /// with the names of the icons will do. No badge when null.
    /// </summary>
    public Enum? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>
    /// The ring around the status badge; the color behind the avatar, so the badge looks cut out of it.
    /// </summary>
    public IBrush? StatusBackground
    {
        get => GetValue(StatusBackgroundProperty);
        set => SetValue(StatusBackgroundProperty, value);
    }

    /// <summary>
    /// The badge of <see cref="Status"/>, or null when there is none.
    /// </summary>
    public IImage? StatusIcon
    {
        get => _statusIcon;
        private set => SetAndRaise(StatusIconProperty, ref _statusIcon, value);
    }

    /// <summary>
    /// First letter of <see cref="UserName"/>, or "?" while the name is unknown.
    /// </summary>
    public string Initial
    {
        get => _initial;
        private set => SetAndRaise(InitialProperty, ref _initial, value);
    }

    /// <summary>
    /// The picture shown instead of the initial, or null when there is none.
    /// </summary>
    public IImage? Picture
    {
        get => _picture;
        private set => SetAndRaise(PictureProperty, ref _picture, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty || change.Property == IsAnimatedProperty)
            Picture = Shown(Source ?? _loaded);
        else if (change.Property == AvatarUrlProperty || change.Property == CoordinatorProperty)
            _ = LoadAsync();
        else if (change.Property == UserNameProperty)
            Initial = string.IsNullOrWhiteSpace(UserName) ? "?" : UserName.Trim()[..1].ToUpperInvariant();
        else if (change.Property == StatusProperty)
            StatusIcon = Status == null ? null : IconResources.Get($"Status{Status}Icon");
        else if (change.Property == IsSpeakingProperty)
            PseudoClasses.Set(SpeakingPseudoClass, IsSpeaking);
        else if (change.Property == IsWaitingProperty)
            PseudoClasses.Set(WaitingPseudoClass, IsWaiting);
        else if (change.Property == IsDeletedProperty)
            PseudoClasses.Set(DeletedPseudoClass, IsDeleted);
    }

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IImage? loaded = null;

        if (AvatarUrl is { Length: > 0 } avatarUrl && Loader is { } loader)
        {
            try
            {
                loaded = await loader(Coordinator, avatarUrl);
            }
            catch (Exception)
            {
                // An avatar that cannot be had is not worth more than the initial shown instead
            }

            if (version != _loadVersion)
                return;
        }

        _loaded = loaded;
        Picture = Shown(Source ?? loaded);
    }

    private IImage? Shown(IImage? picture) =>
        !IsAnimated && picture is AnimatedImage animated ? animated.FirstFrame : picture;
}

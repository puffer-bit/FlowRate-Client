namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// Marks a panel that scrolls itself but counts its offset in pixels rather than in items,
/// so <see cref="SmoothScrollContentPresenter"/> can animate the mouse wheel over it like over plain content.
/// </summary>
public interface IPixelScrollable;

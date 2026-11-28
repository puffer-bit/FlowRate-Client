using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Client.Avalonia.Styles.Animation;

namespace Client.Mobile.Views.Dialogs;

/// <summary>
/// The backdrop and the card a shared dialog is shown in on a phone. Created and removed by
/// <see cref="ScreenDialogPresenter"/>.
/// </summary>
public sealed class DialogFrame : UserControl
{
    private readonly Border _card;

    /// <summary>Raised when the dialog content closes it with a result.</summary>
    public event Action<object?>? Closed;

    /// <param name="fillWindow">Whether the card takes the whole height of the screen, for large dialogs.</param>
    public DialogFrame(Control content, bool fillWindow)
    {
        // The backdrop covers the whole screen and takes the pointer input, which makes the dialog modal
        Bind(BackgroundProperty, this.GetResourceObservable("DialogBackdropBrush"));
        Presence.SetAppear(this, AppearEffect.Fade);
        Presence.SetDuration(this, MotionDuration.Short);

        // As wide as the screen lets it, so a short text does not squeeze the buttons
        _card = new Border
        {
            Margin = new Thickness(20),
            MaxWidth = 480,
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = content
        };
        _card.Bind(Border.BackgroundProperty, _card.GetResourceObservable("GeneralBrush"));
        _card.Bind(Border.BorderBrushProperty, _card.GetResourceObservable("DialogBorderBrush"));
        _card.Bind(Border.BoxShadowProperty, _card.GetResourceObservable("DialogShadow"));
        Presence.SetAppear(_card, AppearEffect.Grow);
        Presence.SetDuration(_card, MotionDuration.Short);
        Content = _card;

        if (!fillWindow)
            return;

        _card.VerticalAlignment = VerticalAlignment.Stretch;
        if (double.IsFinite(content.MaxWidth))
            _card.MaxWidth = content.MaxWidth;
    }

    public void Close(object? result) => Closed?.Invoke(result);
}

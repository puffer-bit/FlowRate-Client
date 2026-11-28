using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Client.Avalonia.Controls.Icons;
using Client.Avalonia.Controls.Images;
using Client.Avalonia.Localization;
using Client.ViewModels.Sessions.Chat;
using Client.Views.Shared.Common;

namespace Client.Views.Shared.Chat;

/// <summary>Keeps the preview image alive while it is open above the message list.</summary>
internal static class LinkImageLightbox
{
    public static void Open(Control anchor, ChatLinkPreviewVM preview)
    {
        if (preview.Image is not { } picture || TopLevel.GetTopLevel(anchor) is not { } topLevel)
            return;

        preview.Show();
        var image = new ZoomableImage { Source = picture, Padding = new Thickness(24) };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Background = Brushes.Black };
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8), Spacing = 8
        };
        toolbar.Bind(Panel.BackgroundProperty, new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("GeneralBrush"));
        var rotate = IconButton("RotateImageIcon", "chat.image.rotate");
        var close = IconButton("CloseIcon", "common.close");
        toolbar.Children.Add(rotate);
        toolbar.Children.Add(close);
        rotate.Click += (_, _) => image.QuarterTurns = (image.QuarterTurns + 1) % 4;
        Grid.SetRow(image, 1);
        layout.Children.Add(image);
        layout.Children.Add(toolbar);

        Action dismiss;
        if (topLevel is Window owner)
        {
            var window = new Window
            {
                Title = preview.Title ?? preview.SiteName,
                Content = layout, Background = Brushes.Black
            };
            window.Closed += (_, _) => preview.Hide();
            dismiss = window.Close;
            FullscreenWindow.Show(window, owner);
        }
        else
        {
            var popup = new Popup
            {
                PlacementTarget = topLevel, Placement = PlacementMode.Center,
                IsLightDismissEnabled = true, Child = layout
            };
            void Resize()
            {
                layout.Width = topLevel.Bounds.Width;
                layout.Height = topLevel.Bounds.Height;
            }
            Resize();
            EventHandler<SizeChangedEventArgs> resized = (_, _) => Resize();
            topLevel.SizeChanged += resized;
            popup.Closed += (_, _) =>
            {
                topLevel.SizeChanged -= resized;
                preview.Hide();
            };
            dismiss = () => popup.IsOpen = false;
            popup.IsOpen = true;
        }
        close.Click += (_, _) => dismiss();
        layout.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            dismiss();
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        close.Focus();
    }

    private static Button IconButton(string icon, string label)
    {
        var button = new Button
        {
            Width = 36, Height = 36, Padding = new Thickness(8), Classes = { "ghost" },
            Content = new Image { Source = IconResources.Get(icon) }
        };
        button.Bind(ToolTip.TipProperty, Strings.Observe(label));
        button.Bind(global::Avalonia.Automation.AutomationProperties.NameProperty, Strings.Observe(label));
        return button;
    }
}

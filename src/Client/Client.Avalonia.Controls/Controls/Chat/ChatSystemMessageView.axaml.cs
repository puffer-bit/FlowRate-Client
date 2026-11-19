using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Client.Core.Chat;
using System.Windows.Input;
using Shared.Models.DataTransferObjects.ChatSession.Messages;

namespace Client.Avalonia.Controls.Chat;

/// <summary>The same compact event row on desktop and mobile.</summary>
public partial class ChatSystemMessageView : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<ChatSystemMessageView, string>(nameof(Text), string.Empty);
    public static readonly StyledProperty<DateTime> TimeProperty =
        AvaloniaProperty.Register<ChatSystemMessageView, DateTime>(nameof(Time));
    public static readonly StyledProperty<ChatSystemEventKind> KindProperty =
        AvaloniaProperty.Register<ChatSystemMessageView, ChatSystemEventKind>(nameof(Kind));

    public static readonly StyledProperty<IReadOnlyList<ChatSystemMessagePart>?> PartsProperty =
        AvaloniaProperty.Register<ChatSystemMessageView, IReadOnlyList<ChatSystemMessagePart>?>(nameof(Parts));
    public static readonly StyledProperty<ICommand?> OpenUserCommandProperty =
        AvaloniaProperty.Register<ChatSystemMessageView, ICommand?>(nameof(OpenUserCommand));

    public IReadOnlyList<ChatSystemMessagePart>? Parts { get => GetValue(PartsProperty); set => SetValue(PartsProperty, value); }
    public ICommand? OpenUserCommand { get => GetValue(OpenUserCommandProperty); set => SetValue(OpenUserCommandProperty, value); }
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public DateTime Time { get => GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
    public ChatSystemEventKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public ChatSystemMessageView()
    {
        InitializeComponent();
        EventText.SizeChanged += (_, _) => LimitNameWidths();
        RenderParts();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PartsProperty || change.Property == TextProperty || change.Property == OpenUserCommandProperty)
            RenderParts();
    }

    private void RenderParts()
    {
        if (EventText == null) return;
        EventText.Inlines!.Clear();
        foreach (var part in Parts ?? [new ChatSystemMessagePart(Text)])
        {
            if (part.UserId is { } id)
            {
                var button = new Button
                {
                    Content = new TextBlock { Text = part.Text, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                    Command = OpenUserCommand, CommandParameter = id
                };
                button.Classes.Add("event-user");
                EventText.Inlines.Add(new InlineUIContainer(button) { BaselineAlignment = global::Avalonia.Media.BaselineAlignment.TextBottom });
            }
            else EventText.Inlines.Add(new Run(part.Text));
        }
        LimitNameWidths();
    }

    private void LimitNameWidths()
    {
        if (EventText.Bounds.Width <= 0) return;
        foreach (var inline in EventText.Inlines!.OfType<InlineUIContainer>())
            if (inline.Child is Button button) button.MaxWidth = EventText.Bounds.Width;
    }
}

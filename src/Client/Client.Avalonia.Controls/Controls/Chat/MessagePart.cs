using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Client.Avalonia.Controls.Chat;

/// <summary>
/// A part of a message row that is built from its template only while <see cref="Content"/> has a value.
/// Unlike a content presenter, it never takes its content as its own data context: the bindings put on it
/// keep reading the message of the row, which changes whenever the list reuses the row for another message.
/// </summary>
public class MessagePart : Decorator
{
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<MessagePart, object?>(nameof(Content));

    public static readonly StyledProperty<IDataTemplate?> ContentTemplateProperty =
        AvaloniaProperty.Register<MessagePart, IDataTemplate?>(nameof(ContentTemplate));

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public IDataTemplate? ContentTemplate
    {
        get => GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    // The template the current child was built from; the child is kept while the template stays
    private IDataTemplate? _builtWith;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ContentProperty || change.Property == ContentTemplateProperty)
            UpdateChild();
    }

    private void UpdateChild()
    {
        var content = Content;
        var template = ContentTemplate;

        if (content == null || template == null)
        {
            Child = null;
            _builtWith = null;
            return;
        }

        // The view built for another message of the same kind shows this one
        if (Child == null || _builtWith != template)
        {
            Child = template.Build(content);
            _builtWith = template;
        }

        if (Child != null)
            Child.DataContext = content;
    }
}

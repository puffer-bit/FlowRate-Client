using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Client.Avalonia.Icons;
using Client.Core.Chat;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// Gives a <see cref="TextBlock"/> the text of a message with its web addresses shown as links:
/// each one is a <see cref="Run"/> with the class "link" (and "hover" under the pointer), and a
/// click or a tap opens it in the browser, after <see cref="LinkConfirmationProperty"/> agreed when
/// one is set. Set it instead of <see cref="TextBlock.Text"/>; the displayed text used by copying
/// and selection is read back with <see cref="GetText"/>.
/// <see cref="BodyProperty"/> renders chat Markdown together with mentions, each a <see cref="Run"/>
/// with the class "mention", and "self" when it calls the current user.
/// An emoji the text font would draw in black and white (❤️, ☀️) gets a run of its own in
/// <see cref="EmojiFont"/>.
/// </summary>
public static class ChatMessageText
{
    // A press that travels further than this is a selection or a scroll, not a click
    private const double ClickTolerance = 4;

    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(ChatMessageText));

    public static readonly AttachedProperty<ChatMessageBody?> BodyProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, ChatMessageBody?>("Body", typeof(ChatMessageText));

    /// <summary>
    /// Asked before a link opens; the link stays closed when it answers false.
    /// </summary>
    public static readonly AttachedProperty<Func<Uri, Task<bool>>?> LinkConfirmationProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, Func<Uri, Task<bool>>?>("LinkConfirmation", typeof(ChatMessageText));

    private static readonly AttachedProperty<LinkState?> StateProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, LinkState?>("State", typeof(ChatMessageText));

    public static string? GetText(TextBlock textBlock) =>
        textBlock.GetValue(StateProperty)?.Document.Text
        ?? textBlock.GetValue(BodyProperty)?.Text ?? textBlock.GetValue(TextProperty);

    public static ChatMessageBody? GetBody(TextBlock textBlock) => textBlock.GetValue(BodyProperty);
    public static void SetBody(TextBlock textBlock, ChatMessageBody? value) => textBlock.SetValue(BodyProperty, value);
    public static void SetText(TextBlock textBlock, string? value) => textBlock.SetValue(TextProperty, value);

    public static Func<Uri, Task<bool>>? GetLinkConfirmation(TextBlock textBlock) =>
        textBlock.GetValue(LinkConfirmationProperty);
    public static void SetLinkConfirmation(TextBlock textBlock, Func<Uri, Task<bool>>? value) =>
        textBlock.SetValue(LinkConfirmationProperty, value);

    /// <summary>
    /// The destination of the link at a point of the text block, or null when
    /// there is none there.
    /// </summary>
    public static string? GetLinkAt(TextBlock textBlock, Point position)
    {
        if (textBlock.GetValue(StateProperty) is not { } state)
            return null;

        int link = FindLink(textBlock, state, position);
        return link >= 0 ? state.Links[link].Uri.AbsoluteUri : null;
    }

    public static ChatMentionSpan? GetMentionAt(TextBlock textBlock, Point position)
    {
        if (textBlock.GetValue(StateProperty)?.Document is not { } body || body.Mentions.Count == 0)
            return null;

        int character = FindCharacter(textBlock, position);
        foreach (var mention in body.Mentions)
        {
            if (mention.UserId != null && character >= mention.Start && character < mention.End)
                return mention;
        }
        return null;
    }

    static ChatMessageText()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>((textBlock, e) => Apply(textBlock, e.GetNewValue<string?>(), []));
        BodyProperty.Changed.AddClassHandler<TextBlock>((textBlock, e) =>
        {
            var body = e.GetNewValue<ChatMessageBody?>();
            Apply(textBlock, body?.Text, body?.Mentions ?? []);
        });
    }

    private static void Apply(TextBlock textBlock, string? text, IReadOnlyList<ChatMentionSpan> mentions)
    {
        var document = GetBody(textBlock) is { } body
            ? ChatMarkdown.Render(body)
            : new ChatMarkdownDocument(text ?? "", mentions, ChatLinks.Find(text), []);
        var state = textBlock.GetValue(StateProperty);
        if (state == null)
        {
            state = new LinkState();
            textBlock.SetValue(StateProperty, state);
            Watch(textBlock);
        }
        var inlines = BuildInlines(document, out var runs);
        state.Reset(document, runs);
        textBlock.ClearValue(InputElement.CursorProperty);
        textBlock.Inlines!.Clear();
        textBlock.Inlines.AddRange(inlines);
    }

    private static List<Inline> BuildInlines(ChatMarkdownDocument document, out List<Run>[] linkRuns)
    {
        string text = document.Text;
        var emoji = ChatEmoji.FindPresentation(text);
        var boundaries = new SortedSet<int> { 0, text.Length };
        foreach (var span in document.Styles)
        {
            boundaries.Add(span.Start);
            boundaries.Add(span.End);
        }
        foreach (var span in document.Links)
        {
            boundaries.Add(span.Start);
            boundaries.Add(span.End);
        }
        foreach (var span in document.Mentions)
        {
            boundaries.Add(span.Start);
            boundaries.Add(span.End);
        }
        foreach (var span in emoji)
        {
            boundaries.Add(span.Start);
            boundaries.Add(span.End);
        }
        linkRuns = document.Links.Select(_ => new List<Run>()).ToArray();
        var inlines = new List<Inline>();
        int start = 0;
        foreach (int end in boundaries)
        {
            if (end <= start)
                continue;
            var run = new Run(text[start..end]);
            var style = document.Styles.FirstOrDefault(s => s.Start <= start && s.End > start).Style;
            if (style.HasFlag(ChatTextStyle.Bold))
                run.FontWeight = FontWeight.Bold;
            if (style.HasFlag(ChatTextStyle.Italic))
                run.FontStyle = FontStyle.Italic;
            if (style.HasFlag(ChatTextStyle.Strike))
                run.TextDecorations = TextDecorations.Strikethrough;
            if (style.HasFlag(ChatTextStyle.Code))
                run.Classes.Add("code");
            if (emoji.Any(span => span.Start <= start && span.End > start))
                run.FontFamily = EmojiFont.Family;
            foreach (var mention in document.Mentions.Where(m => m.Start <= start && m.End > start))
            {
                run.Classes.Add("mention");
                if (mention.IsSelf)
                    run.Classes.Add("self");
            }
            for (int i = 0; i < document.Links.Count; i++)
            {
                if (document.Links[i].Start <= start && document.Links[i].End > start)
                {
                    run.Classes.Add("link");
                    linkRuns[i].Add(run);
                }
            }
            inlines.Add(run);
            start = end;
        }
        return inlines;
    }

    // The selectable text of the desktop marks its pointer events handled, hence handledEventsToo
    private static void Watch(TextBlock textBlock)
    {
        textBlock.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
        textBlock.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, handledEventsToo: true);
        textBlock.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
        textBlock.AddHandler(InputElement.PointerExitedEvent, OnPointerExited, handledEventsToo: true);
        // The loss of the capture does not cancel a press: the selectable text lets go of the pointer
        // on every release, before this class hears of it. A scroll that takes the pointer away has
        // moved it past the tolerance, or the release never comes here
        // A long press opens the menu of the message; the finger going up after it is not a tap on the link
        textBlock.AddHandler(Control.ContextRequestedEvent, OnPressCancelled, handledEventsToo: true);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock textBlock || textBlock.GetValue(StateProperty) is not { } state)
            return;

        // Which link it is gets asked on the release, where the pointer is known to have stayed
        var point = e.GetCurrentPoint(textBlock);
        state.IsPressed = point.Properties.IsLeftButtonPressed;
        state.PressedAt = point.Position;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not TextBlock textBlock || textBlock.GetValue(StateProperty) is not { } state)
            return;

        var position = e.GetPosition(textBlock);

        if (state.IsPressed && !IsNear(position, state.PressedAt))
            state.IsPressed = false;

        // A finger has no hover: the link would stay underlined after the tap
        if (e.Pointer.Type != PointerType.Touch)
        {
            SetHovered(textBlock, state, FindLink(textBlock, state, position));
            if (GetMentionAt(textBlock, position) != null)
                textBlock.Cursor = new Cursor(StandardCursorType.Hand);
            else if (state.Hovered < 0)
                textBlock.ClearValue(InputElement.CursorProperty);
        }
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not TextBlock textBlock || textBlock.GetValue(StateProperty) is not { } state)
            return;

        bool isPressed = state.IsPressed;
        state.IsPressed = false;

        if (!isPressed || e.InitialPressMouseButton != MouseButton.Left)
            return;

        var position = e.GetPosition(textBlock);
        if (!IsNear(position, state.PressedAt))
            return;

        // A double click selects the word under it; the user is selecting, not following the link
        if (textBlock is SelectableTextBlock { SelectionStart: var start, SelectionEnd: var end } && start != end)
            return;

        if (GetMentionAt(textBlock, position) is { UserId: { } userId } mention
            && textBlock.DataContext is ChatMessageVM message)
        {
            string name = GetText(textBlock)!.Substring(mention.Start + 1, mention.Length - 1);
            ChatUserMenu.Open(textBlock, message, userId, name);
            e.Handled = true;
            return;
        }

        int link = FindLink(textBlock, state, position);
        if (link < 0)
            return;

        _ = OpenAsync(textBlock, state.Links[link].Uri);
    }

    private static async Task OpenAsync(TextBlock textBlock, Uri uri)
    {
        if (GetLinkConfirmation(textBlock) is { } confirm && !await confirm(uri))
            return;

        if (TopLevel.GetTopLevel(textBlock) is { } topLevel)
            await topLevel.Launcher.LaunchUriAsync(uri);
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is TextBlock textBlock && textBlock.GetValue(StateProperty) is { } state)
        {
            SetHovered(textBlock, state, -1);
            textBlock.ClearValue(InputElement.CursorProperty);
        }
    }

    private static void OnPressCancelled(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBlock textBlock && textBlock.GetValue(StateProperty) is { } state)
            state.IsPressed = false;
    }

    private static void SetHovered(TextBlock textBlock, LinkState state, int link)
    {
        if (state.Hovered == link)
            return;

        if (state.Hovered >= 0)
            foreach (var run in state.Runs[state.Hovered]) run.Classes.Remove("hover");
        if (link >= 0)
            foreach (var run in state.Runs[link]) run.Classes.Add("hover");
        state.Hovered = link;

        if (link >= 0)
            textBlock.Cursor = new Cursor(StandardCursorType.Hand);
        else
            textBlock.ClearValue(InputElement.CursorProperty);
    }

    // The index of the link under the point, or -1
    private static int FindLink(TextBlock textBlock, LinkState state, Point position)
    {
        if (state.Links.Count == 0)
            return -1;

        int character = FindCharacter(textBlock, position);

        for (int i = 0; i < state.Links.Count; i++)
        {
            if (character >= state.Links[i].Start && character < state.Links[i].End)
                return i;
        }

        return -1;
    }

    private static int FindCharacter(TextBlock textBlock, Point position)
    {
        // A selection change invalidates the layout before the pointer handler runs.
        if (!textBlock.IsMeasureValid)
            textBlock.UpdateLayout();

        var padding = textBlock.Padding;
        var hit = textBlock.TextLayout.HitTestPoint(position - new Point(padding.Left, padding.Top));
        return hit.IsInside ? hit.CharacterHit.FirstCharacterIndex : -1;
    }

    private static bool IsNear(Point a, Point b) =>
        Math.Abs(a.X - b.X) <= ClickTolerance && Math.Abs(a.Y - b.Y) <= ClickTolerance;

    // What the text block shows now. The views are recycled, so one state serves many messages
    private sealed class LinkState
    {
        public ChatMarkdownDocument Document { get; private set; } = new("", [], [], []);
        public IReadOnlyList<ChatLink> Links => Document.Links;
        public List<Run>[] Runs { get; private set; } = [];
        public bool IsPressed { get; set; }
        public Point PressedAt { get; set; }
        public int Hovered { get; set; } = -1;

        public void Reset(ChatMarkdownDocument document, List<Run>[] runs)
        {
            Document = document;
            Runs = runs;
            IsPressed = false;
            Hovered = -1;
        }
    }
}

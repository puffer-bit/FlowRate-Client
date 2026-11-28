using Shared.Models.DataTransferObjects.ChatSession.Messages.Links;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
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
    // A finger trembles more than a mouse, and covers more than the point it is said to touch
    private const double TapTolerance = 12;

    /// <summary>
    /// How far from a link or a mention a finger still hits it: the exact letters are hard to tap.
    /// </summary>
    public const double TouchReach = 12;

    public static readonly AttachedProperty<IBrush?> CodeBlockBackgroundProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IBrush?>("CodeBlockBackground", typeof(ChatMessageText));

    public static IBrush? GetCodeBlockBackground(TextBlock textBlock) => textBlock.GetValue(CodeBlockBackgroundProperty);
    public static void SetCodeBlockBackground(TextBlock textBlock, IBrush? value) => textBlock.SetValue(CodeBlockBackgroundProperty, value);

    internal static void RenderCodeBlocks(TextBlock textBlock, DrawingContext context)
    {
        var brush = GetCodeBlockBackground(textBlock);
        foreach (var (_, bounds) in GetCodeBlocks(textBlock))
            context.DrawRectangle(brush, brush is null ? null : new Pen(brush), bounds, 5, 5);
    }

    internal static IEnumerable<(string Text, Rect Bounds)> GetCodeBlocks(TextBlock textBlock)
    {
        // Asked before the text is measured anew, the layout would be built from the empty Text and kept
        if (textBlock.GetValue(StateProperty) is not { } state || !textBlock.IsMeasureValid)
            yield break;

        foreach (var span in state.Document.Styles.Where(span => span.Style.HasFlag(ChatTextStyle.CodeBlock)))
        {
            var rectangles = textBlock.TextLayout.HitTestTextRange(span.Start, span.Length).ToArray();
            if (rectangles.Length == 0)
                continue;

            double top = rectangles.Min(rect => rect.Top) + textBlock.Padding.Top - 3;
            double bottom = rectangles.Max(rect => rect.Bottom) + textBlock.Padding.Top + 3;
            var bounds = new Rect(0, top, textBlock.Bounds.Width, bottom - top);
            yield return (state.Document.Text.Substring(span.Start, span.Length), bounds);
        }
    }

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
    public static string? GetLinkAt(TextBlock textBlock, Point position, double reach = 0)
    {
        if (textBlock.GetValue(StateProperty) is not { } state)
            return null;

        int link = FindLink(textBlock, state, position, reach);
        if (link < 0)
            return null;

        // A link of the application is copied as written: its scheme is not one the system normalizes
        var uri = state.Links[link].Uri;
        return ChatAppLinks.IsAppLink(uri) ? uri.OriginalString : uri.AbsoluteUri;
    }

    /// <summary>
    /// The mention of a user at a point of the text block, or within <paramref name="reach"/> of it.
    /// </summary>
    public static ChatMentionSpan? GetMentionAt(TextBlock textBlock, Point position, double reach = 0)
    {
        if (textBlock.GetValue(StateProperty)?.Document is not { } body || body.Mentions.Count == 0)
            return null;

        var mentions = body.Mentions.Where(mention => mention.UserId != null).ToList();
        int index = FindSpan(textBlock, position, reach, mentions.Count, i => (mentions[i].Start, mentions[i].End));
        return index >= 0 ? mentions[index] : null;
    }

    static ChatMessageText()
    {
        CodeBlockBackgroundProperty.Changed.AddClassHandler<TextBlock>((block, _) => block.InvalidateVisual());
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
            : new ChatMarkdownDocument(text ?? "", mentions,
                ChatLinks.Find(text).Concat(ChatAppLinks.Find(text)).OrderBy(link => link.Start).ToArray(), []);
        var state = textBlock.GetValue(StateProperty);
        if (state == null)
        {
            state = new LinkState();
            textBlock.SetValue(StateProperty, state);
            textBlock.Bind(CodeBlockBackgroundProperty, new DynamicResourceExtension("ColorOverlayMedium"));
            Watch(textBlock);
        }
        var inlines = BuildInlines(document, out var runs);
        state.Reset(document, runs);
        textBlock.ClearValue(InputElement.CursorProperty);
        textBlock.Inlines!.Clear();
        textBlock.Inlines.AddRange(inlines);
        textBlock.Classes.Set("has-code-block", document.Styles.Any(span => span.Style.HasFlag(ChatTextStyle.CodeBlock)));
        textBlock.InvalidateVisual();
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
            if (style.HasFlag(ChatTextStyle.CodeBlock))
                run.Classes.Add("code-block");
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
        bool isTouch = e.Pointer.Type == PointerType.Touch;
        state.IsPressed = point.Properties.IsLeftButtonPressed;
        state.PressedAt = point.Position;
        state.Reach = isTouch ? TouchReach : 0;
        state.Tolerance = isTouch ? TapTolerance : ClickTolerance;

        // A finger sees the link it is on lit, before it is let go
        SetPressed(state, state.IsPressed && isTouch ? FindLink(textBlock, state, point.Position, state.Reach) : -1);
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not TextBlock textBlock || textBlock.GetValue(StateProperty) is not { } state)
            return;

        var position = e.GetPosition(textBlock);

        if (state.IsPressed && !IsNear(position, state.PressedAt, state.Tolerance))
        {
            state.IsPressed = false;
            SetPressed(state, -1);
        }

        // A finger has no hover: the link would stay underlined after the tap
        if (e.Pointer.Type != PointerType.Touch)
        {
            SetHovered(textBlock, state, FindLink(textBlock, state, position, 0));
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
        SetPressed(state, -1);

        if (!isPressed || e.InitialPressMouseButton != MouseButton.Left)
            return;

        var position = e.GetPosition(textBlock);
        if (!IsNear(position, state.PressedAt, state.Tolerance))
            return;

        // A double click selects the word under it; the user is selecting, not following the link
        if (textBlock is SelectableTextBlock { SelectionStart: var start, SelectionEnd: var end } && start != end)
            return;

        if (GetMentionAt(textBlock, position, state.Reach) is { UserId: { } userId } mention
            && textBlock.DataContext is ChatMessageVM message)
        {
            string name = GetText(textBlock)!.Substring(mention.Start + 1, mention.Length - 1);
            ChatUserMenu.Open(textBlock, message, userId, name);
            e.Handled = true;
            return;
        }

        int link = FindLink(textBlock, state, position, state.Reach);
        if (link < 0)
            return;

        _ = OpenAsync(textBlock, state.Links[link].Uri);
    }

    private static async Task OpenAsync(TextBlock textBlock, Uri uri)
    {
        // A link of the application opens here, not through the system, and asks nothing: it only fills in a form
        // the user confirms. An approval of a device is never one of the links, so it cannot get here
        if (ChatAppLinks.IsAppLink(uri))
        {
            ChatAppLinkVM.Open(uri.OriginalString);
            return;
        }

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
        {
            state.IsPressed = false;
            SetPressed(state, -1);
        }
    }

    private static void SetPressed(LinkState state, int link)
    {
        if (state.Pressed == link)
            return;

        if (state.Pressed >= 0)
            foreach (var run in state.Runs[state.Pressed]) run.Classes.Remove("pressed");
        if (link >= 0)
            foreach (var run in state.Runs[link]) run.Classes.Add("pressed");
        state.Pressed = link;
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

    // The index of the link under the point, or the nearest one within the reach; -1 when there is none
    private static int FindLink(TextBlock textBlock, LinkState state, Point position, double reach)
    {
        var links = state.Links;
        return FindSpan(textBlock, position, reach, links.Count, i => (links[i].Start, links[i].End));
    }

    // The span with the character under the point, else the span whose letters are nearest the point
    // and no further than the reach
    private static int FindSpan(TextBlock textBlock, Point position, double reach, int count, Func<int, (int Start, int End)> span)
    {
        if (count == 0)
            return -1;

        int character = FindCharacter(textBlock, position);
        for (int i = 0; i < count; i++)
        {
            var (start, end) = span(i);
            if (character >= start && character < end)
                return i;
        }

        if (reach <= 0)
            return -1;

        EnsureLayout(textBlock);
        var point = position - new Point(textBlock.Padding.Left, textBlock.Padding.Top);
        int nearest = -1;
        double nearestDistance = reach;
        for (int i = 0; i < count; i++)
        {
            var (start, end) = span(i);
            foreach (var rect in textBlock.TextLayout.HitTestTextRange(start, end - start))
            {
                double dx = Math.Max(0, Math.Max(rect.Left - point.X, point.X - rect.Right));
                double dy = Math.Max(0, Math.Max(rect.Top - point.Y, point.Y - rect.Bottom));
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance <= nearestDistance)
                {
                    nearest = i;
                    nearestDistance = distance;
                }
            }
        }

        return nearest;
    }

    private static int FindCharacter(TextBlock textBlock, Point position)
    {
        // A selection change invalidates the layout before the pointer handler runs.
        EnsureLayout(textBlock);

        var padding = textBlock.Padding;
        var hit = textBlock.TextLayout.HitTestPoint(position - new Point(padding.Left, padding.Top));
        return hit.IsInside ? hit.CharacterHit.FirstCharacterIndex : -1;
    }

    /// <summary>
    /// Measures the text first when it waits for a measure: the layout of a text block asked for in between is
    /// built from its empty Text instead of its inlines and stays, so the message would show nothing.
    /// </summary>
    public static void EnsureLayout(TextBlock textBlock)
    {
        if (!textBlock.IsMeasureValid)
            textBlock.UpdateLayout();
    }

    private static bool IsNear(Point a, Point b, double tolerance) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

    // What the text block shows now. The views are recycled, so one state serves many messages
    private sealed class LinkState
    {
        public ChatMarkdownDocument Document { get; private set; } = new("", [], [], []);
        public IReadOnlyList<ChatLink> Links => Document.Links;
        public List<Run>[] Runs { get; private set; } = [];
        public bool IsPressed { get; set; }
        public Point PressedAt { get; set; }
        public int Hovered { get; set; } = -1;
        // The link lit under a finger, or -1
        public int Pressed { get; set; } = -1;
        // How far from a link the press still hits it, and how far it may travel: a finger gets more
        public double Reach { get; set; }
        public double Tolerance { get; set; } = ClickTolerance;

        public void Reset(ChatMarkdownDocument document, List<Run>[] runs)
        {
            Document = document;
            Runs = runs;
            IsPressed = false;
            Hovered = -1;
            Pressed = -1;
        }
    }
}

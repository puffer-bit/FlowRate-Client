using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.Core.Chat;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// Gives a <see cref="TextBlock"/> the text of a message with its web addresses shown as links:
/// each one is a <see cref="Run"/> with the class "link" (and "hover" under the pointer), and a
/// click or a tap opens it in the browser, after <see cref="LinkConfirmationProperty"/> agreed when
/// one is set. Set it instead of <see cref="TextBlock.Text"/>, which stays empty while the text
/// has links; the text as it was given is read back with <see cref="GetText"/>.
/// <see cref="BodyProperty"/> sets the text together with its mentions, each a <see cref="Run"/>
/// with the class "mention", and "self" when it calls the current user.
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
        textBlock.GetValue(BodyProperty)?.Text ?? textBlock.GetValue(TextProperty);

    public static ChatMessageBody? GetBody(TextBlock textBlock) => textBlock.GetValue(BodyProperty);
    public static void SetBody(TextBlock textBlock, ChatMessageBody? value) => textBlock.SetValue(BodyProperty, value);
    public static void SetText(TextBlock textBlock, string? value) => textBlock.SetValue(TextProperty, value);

    public static Func<Uri, Task<bool>>? GetLinkConfirmation(TextBlock textBlock) =>
        textBlock.GetValue(LinkConfirmationProperty);
    public static void SetLinkConfirmation(TextBlock textBlock, Func<Uri, Task<bool>>? value) =>
        textBlock.SetValue(LinkConfirmationProperty, value);

    /// <summary>
    /// The link at a point of the text block, as it is written in the message, or null when
    /// there is none there.
    /// </summary>
    public static string? GetLinkAt(TextBlock textBlock, Point position)
    {
        if (textBlock.GetValue(StateProperty) is not { } state)
            return null;

        int link = FindLink(textBlock, state, position);
        return link >= 0 ? state.Runs[link].Text : null;
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
        var links = WithoutMentions(ChatLinks.Find(text), mentions);
        var state = textBlock.GetValue(StateProperty);

        if (mentions.Count > 0 && links.Count == 0)
        {
            // Mentions alone need no pointer handling
            if (state != null)
            {
                state.Reset([], []);
                textBlock.ClearValue(InputElement.CursorProperty);
            }
            textBlock.Inlines!.Clear();
            textBlock.Inlines.AddRange(BuildInlines(text!, links, mentions, out _));
            return;
        }

        if (links.Count == 0)
        {
            // Setting the text also drops the runs of the message shown before
            if (state != null)
            {
                state.Reset([], []);
                textBlock.ClearValue(InputElement.CursorProperty);
            }
            textBlock.Text = text;
            return;
        }

        if (state == null)
        {
            state = new LinkState();
            textBlock.SetValue(StateProperty, state);
            Watch(textBlock);
        }

        var inlines = BuildInlines(text!, links, mentions, out var runs);

        state.Reset(links, runs);
        textBlock.ClearValue(InputElement.CursorProperty);
        textBlock.Inlines!.Clear();
        textBlock.Inlines.AddRange(inlines);
    }

    // Links and mentions in the order of the text; the runs of the links are given out for the hover
    private static List<Inline> BuildInlines(string text, IReadOnlyList<ChatLink> links,
        IReadOnlyList<ChatMentionSpan> mentions, out Run[] linkRuns)
    {
        var inlines = new List<Inline>((links.Count + mentions.Count) * 2 + 1);
        linkRuns = new Run[links.Count];
        int position = 0, link = 0, mention = 0;

        while (link < links.Count || mention < mentions.Count)
        {
            bool isLink = mention >= mentions.Count
                          || (link < links.Count && links[link].Start < mentions[mention].Start);
            int start = isLink ? links[link].Start : mentions[mention].Start;
            int end = isLink ? links[link].End : mentions[mention].End;

            // A mention past the text is of a body that does not match it; it is left out
            if (start < position || end > text.Length)
            {
                if (isLink) link++; else mention++;
                continue;
            }

            if (start > position)
                inlines.Add(new Run(text[position..start]));

            var run = new Run(text[start..end]);
            if (isLink)
            {
                run.Classes.Add("link");
                linkRuns[link++] = run;
            }
            else
            {
                run.Classes.Add("mention");
                if (mentions[mention].IsSelf)
                    run.Classes.Add("self");
                mention++;
            }

            inlines.Add(run);
            position = end;
        }

        if (position < text.Length)
            inlines.Add(new Run(text[position..]));

        return inlines;
    }

    // A name may look like an address; inside a mention it is the name
    private static IReadOnlyList<ChatLink> WithoutMentions(IReadOnlyList<ChatLink> links,
        IReadOnlyList<ChatMentionSpan> mentions)
    {
        if (links.Count == 0 || mentions.Count == 0)
            return links;

        return links
            .Where(link => !mentions.Any(mention => link.Start < mention.End && mention.Start < link.End))
            .ToList();
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
            SetHovered(textBlock, state, FindLink(textBlock, state, position));
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

        int link = FindLink(textBlock, state, position);
        if (link < 0)
            return;

        // A double click selects the word under it; the user is selecting, not following the link
        if (textBlock is SelectableTextBlock { SelectionStart: var start, SelectionEnd: var end } && start != end)
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
            SetHovered(textBlock, state, -1);
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
            state.Runs[state.Hovered].Classes.Remove("hover");
        if (link >= 0)
            state.Runs[link].Classes.Add("hover");
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

        // The selectable text moves its selection on a press and on a right click, which throws its
        // text layout away; until it is measured again the layout has no runs and no link is found
        if (!textBlock.IsMeasureValid)
            textBlock.UpdateLayout();

        var padding = textBlock.Padding;
        var hit = textBlock.TextLayout.HitTestPoint(position - new Point(padding.Left, padding.Top));
        if (!hit.IsInside)
            return -1;

        int character = hit.CharacterHit.FirstCharacterIndex;

        for (int i = 0; i < state.Links.Count; i++)
        {
            if (character >= state.Links[i].Start && character < state.Links[i].End)
                return i;
        }

        return -1;
    }

    private static bool IsNear(Point a, Point b) =>
        Math.Abs(a.X - b.X) <= ClickTolerance && Math.Abs(a.Y - b.Y) <= ClickTolerance;

    // What the text block shows now. The views are recycled, so one state serves many messages
    private sealed class LinkState
    {
        public IReadOnlyList<ChatLink> Links { get; private set; } = [];
        public Run[] Runs { get; private set; } = [];
        public bool IsPressed { get; set; }
        public Point PressedAt { get; set; }
        public int Hovered { get; set; } = -1;

        public void Reset(IReadOnlyList<ChatLink> links, Run[] runs)
        {
            Links = links;
            Runs = runs;
            IsPressed = false;
            Hovered = -1;
        }
    }
}

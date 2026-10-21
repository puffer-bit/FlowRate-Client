using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using ReactiveUI;
using Shared.Enums.User;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Mentions;
using Shared.Validation;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// One line of the mention list: a user, or everyone / those online.
/// </summary>
public class ChatMentionSuggestionVM : ReactiveObject
{
    /// <summary>The name written after "@".</summary>
    public required string Name { get; init; }
    public int? UserId { get; init; }
    public string? AvatarUrl { get; init; }
    public UserStatus? Status { get; init; }
    public string? Hint { get; init; }
    public bool HasHint => Hint != null;
    public bool IsGroup => UserId == null;

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
}

/// <summary>
/// Mentions in the input of a chat. The input holds them as "@name", the way they are shown; the
/// names picked from the list are remembered with their users, and on sending they become the
/// tokens the server reads. Typing "@" opens the list of users matching what follows it.
/// </summary>
public class ChatMentionPickerVM : ReactiveObject
{
    private const int SuggestionsCount = 8;
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(150);

    private readonly ICoordinatorSession _session;
    private readonly int _currentUserId;

    // Names that stand for users in the input, as they were picked
    private readonly Dictionary<string, int> _pickedNames = new(StringComparer.Ordinal);

    // Where the "@" of the mention being typed is, or -1
    private int _queryStart = -1;
    private string? _query;
    private CancellationTokenSource? _search;

    public AvaloniaList<ChatMentionSuggestionVM> Suggestions { get; } = new();

    public bool IsOpen
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int SelectedIndex
    {
        get;
        private set
        {
            if (field >= 0 && field < Suggestions.Count)
                Suggestions[field].IsSelected = false;
            this.RaiseAndSetIfChanged(ref field, value);
            if (value >= 0 && value < Suggestions.Count)
                Suggestions[value].IsSelected = true;
        }
    } = -1;

    public ChatMentionPickerVM(ICoordinatorSession session, int currentUserId)
    {
        _session = session;
        _currentUserId = currentUserId;
    }

    /// <summary>
    /// Follows the input: opens, narrows or closes the list by the word at the caret.
    /// </summary>
    public void Update(string? text, int caretIndex)
    {
        var (start, query) = FindQuery(text, caretIndex);
        if (start < 0)
        {
            Close();
            return;
        }

        if (start == _queryStart && query == _query)
            return;

        _queryStart = start;
        _query = query;
        _ = SearchAsync(query);
    }

    public void MoveSelection(int step)
    {
        if (Suggestions.Count == 0)
            return;

        SelectedIndex = ((SelectedIndex + step) % Suggestions.Count + Suggestions.Count) % Suggestions.Count;
    }

    /// <summary>
    /// Writes the chosen name in place of the word being typed.
    /// </summary>
    /// <returns>The new text and where the caret goes, or null when the list has nothing chosen.</returns>
    public (string Text, int CaretIndex)? Complete(string? text, int caretIndex, ChatMentionSuggestionVM? suggestion = null)
    {
        suggestion ??= SelectedIndex >= 0 && SelectedIndex < Suggestions.Count ? Suggestions[SelectedIndex] : null;
        if (suggestion == null || text == null || _queryStart < 0 || _queryStart >= text.Length)
            return null;

        int end = Math.Clamp(caretIndex, _queryStart, text.Length);
        // The rest of the word at the caret goes too, so completing in the middle of a word does not leave a tail
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;

        string inserted = "@" + suggestion.Name + " ";
        if (suggestion.UserId is { } userId)
            _pickedNames[suggestion.Name] = userId;

        string result = text[.._queryStart] + inserted + text[end..].TrimStart(' ');
        int caret = _queryStart + inserted.Length;
        Close();
        return (result, caret);
    }

    public void Close()
    {
        _search?.Cancel();
        _search = null;
        _queryStart = -1;
        _query = null;
        SelectedIndex = -1;
        Suggestions.Clear();
        IsOpen = false;
    }

    /// <summary>
    /// Forgets the picked names, when the input starts a new message.
    /// </summary>
    public void Reset()
    {
        _pickedNames.Clear();
        Close();
    }

    /// <summary>
    /// The text to send: picked names and "@everyone" / "@here" become mention tokens.
    /// </summary>
    public string Encode(string text)
    {
        var names = _pickedNames
            .Select(pair => (pair.Key, Token: ChatMentions.UserToken(pair.Value)))
            .Append((ChatMessageBody.EveryoneName, ChatMentions.EveryoneToken))
            .Append((ChatMessageBody.HereName, ChatMentions.HereToken))
            // A longer name wins over a shorter one it starts with
            .OrderByDescending(pair => pair.Item1.Length)
            .ToList();

        var builder = new StringBuilder(text.Length);
        int position = 0;
        while (position < text.Length)
        {
            if (text[position] == '@' && (position == 0 || !IsNameCharacter(text[position - 1])))
            {
                var match = names.FirstOrDefault(pair => IsNameAt(text, position + 1, pair.Item1));
                if (match.Item1 != null)
                {
                    builder.Append(match.Item2);
                    position += 1 + match.Item1.Length;
                    continue;
                }
            }

            builder.Append(text[position]);
            position++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// The text of a message put into the input for editing: its tokens are written as names, and
    /// those names are remembered so they go back as tokens. A user with no known name keeps the token.
    /// </summary>
    public string Decode(string text, Func<int, string?> nameOf)
    {
        Close();

        var tokens = ChatMentions.Find(text);
        if (tokens.Count == 0)
            return text;

        var builder = new StringBuilder(text.Length);
        int position = 0;
        foreach (var token in tokens)
        {
            builder.Append(text, position, token.Start - position);
            string? name = token.Kind switch
            {
                ChatMentionKind.Everyone => ChatMessageBody.EveryoneName,
                ChatMentionKind.Here => ChatMessageBody.HereName,
                _ => nameOf(token.UserId)
            };

            if (name == null)
            {
                builder.Append(text, token.Start, token.Length);
            }
            else
            {
                if (token.Kind == ChatMentionKind.User)
                    _pickedNames[name] = token.UserId;
                builder.Append('@').Append(name);
            }

            position = token.End;
        }

        builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }

    private async Task SearchAsync(string query)
    {
        _search?.Cancel();
        var search = _search = new CancellationTokenSource();

        try
        {
            await Task.Delay(SearchDelay, search.Token);
            var users = await _session.SearchUsers(query, SuggestionsCount + 1);
            if (search.IsCancellationRequested)
                return;

            var suggestions = users
                .Where(user => user.Id != _currentUserId)
                .Take(SuggestionsCount)
                .Select(user => new ChatMentionSuggestionVM
                {
                    Name = user.Username,
                    UserId = user.Id,
                    AvatarUrl = user.AvatarUrl,
                    Status = user.Status
                })
                .ToList();

            foreach (var (name, hint) in new[]
                     {
                         (ChatMessageBody.EveryoneName, "chat.mention.everyone"),
                         (ChatMessageBody.HereName, "chat.mention.here")
                     })
            {
                if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    suggestions.Add(new ChatMentionSuggestionVM { Name = name, Hint = Strings.Get(hint) });
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (search != _search)
                    return;

                SelectedIndex = -1;
                Suggestions.Clear();
                Suggestions.AddRange(suggestions);
                IsOpen = suggestions.Count > 0;
                SelectedIndex = suggestions.Count > 0 ? 0 : -1;
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    // The "@" before the caret that starts the word being typed, and what follows it
    private static (int Start, string Query) FindQuery(string? text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text) || caretIndex <= 0 || caretIndex > text.Length)
            return (-1, "");

        for (int i = caretIndex - 1; i >= 0 && caretIndex - i <= Limits.UsernameMaxLength + 1; i--)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
                return (-1, "");

            if (c == '@')
            {
                // An "@" inside a word is an address, not a mention
                return i == 0 || char.IsWhiteSpace(text[i - 1]) || text[i - 1] == '('
                    ? (i, text[(i + 1)..caretIndex])
                    : (-1, "");
            }
        }

        return (-1, "");
    }

    private static bool IsNameAt(string text, int start, string name) =>
        string.CompareOrdinal(text, start, name, 0, name.Length) == 0
        && (start + name.Length == text.Length || !IsNameCharacter(text[start + name.Length]));

    private static bool IsNameCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';
}

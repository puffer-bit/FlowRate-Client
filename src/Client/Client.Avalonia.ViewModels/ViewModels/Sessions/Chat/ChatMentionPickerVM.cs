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
using Shared.Models.DataTransferObjects.ChatSession.Messages.Mentions;
using Shared.Validation;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// Mentions in the input of a chat. The input holds them as "@name", the way they are shown; the
/// names picked from the list are remembered with their users, and on sending they become the
/// tokens the server reads. Typing "@" opens the user list; "/" at the start offers commands.
/// </summary>
public class ChatMentionPickerVM : ReactiveObject
{
    private const int SuggestionsCount = 8;
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(150);

    private readonly ICoordinatorSession _session;
    private readonly int _currentUserId;

    // Names that stand for users in the input, as they were picked
    private readonly Dictionary<string, int> _pickedNames = new(StringComparer.Ordinal);

    // Where the mention or command being typed starts, or -1
    private int _queryStart = -1;
    private string? _query;
    private bool _isCommandQuery;
    private CancellationTokenSource? _search;

    public AvaloniaList<ChatMentionSuggestionVM> Suggestions { get; } = new();

    public bool IsOpen
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool ShowRollHint
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool ShowFlipHint
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSearching
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int SelectedIndex
    {
        get;
        private set
        {
            if (field == value)
                return;
            if (field >= 0 && field < Suggestions.Count)
                Suggestions[field].IsSelected = false;
            this.RaiseAndSetIfChanged(ref field, value);
            if (value >= 0 && value < Suggestions.Count)
                Suggestions[value].IsSelected = true;
            UpdateCommandHint();
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

        bool isCommand = text![start] == '/';
        if (start == _queryStart && query == _query && isCommand == _isCommandQuery)
            return;

        _queryStart = start;
        _query = query;
        _isCommandQuery = isCommand;
        if (isCommand)
        {
            _search?.Cancel();
            _search = null;
            IsSearching = false;
            int separator = query.IndexOfAny([' ', '\t']);
            var commands = new[]
            {
                new ChatMentionSuggestionVM { Name = "roll", IsCommand = true, CommandArguments = "1-100" },
                new ChatMentionSuggestionVM { Name = "flip", IsCommand = true }
            };
            var matches = commands.Where(command => separator < 0
                && command.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToList();
            ApplySuggestions(matches);
            IsOpen = ShowRollHint || ShowFlipHint;
            return;
        }

        ShowRollHint = false;
        ShowFlipHint = false;
        // Keep the popup in place while refreshing, but prevent picking an outdated result.
        IsSearching = true;
        _ = SearchAsync(query);
    }

    public void SelectSuggestion(ChatMentionSuggestionVM suggestion)
    {
        int index = Suggestions.IndexOf(suggestion);
        if (!IsSearching && index >= 0)
            SelectedIndex = index;
    }

    private void UpdateCommandHint()
    {
        string? command = null;
        if (_isCommandQuery && _queryStart >= 0)
        {
            command = SelectedIndex >= 0 && SelectedIndex < Suggestions.Count
                ? Suggestions[SelectedIndex].Name : _query?.Split([' ', '\t'])[0];
        }
        ShowRollHint = string.Equals(command, "roll", StringComparison.OrdinalIgnoreCase);
        ShowFlipHint = string.Equals(command, "flip", StringComparison.OrdinalIgnoreCase);
    }

    public void MoveSelection(int step)
    {
        if (IsSearching || Suggestions.Count == 0)
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
        var (start, query) = FindQuery(text, caretIndex);
        if (suggestion == null || text == null || !IsOpen || IsSearching || !Suggestions.Contains(suggestion)
            || start < 0 || start != _queryStart || query != _query
            || (text[start] == '/') != suggestion.IsCommand)
            return null;

        int end = Math.Clamp(caretIndex, _queryStart, text.Length);
        // The rest of the word at the caret goes too, so completing in the middle of a word does not leave a tail
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;

        string inserted = suggestion.IsCommand
            ? "/" + suggestion.Name + (string.IsNullOrWhiteSpace(text[end..]) && suggestion.CommandArguments != null
                ? " " + suggestion.CommandArguments : " ")
            : "@" + suggestion.Name + " ";
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
        IsSearching = false;
        SelectedIndex = -1;
        Suggestions.Clear();
        IsOpen = false;
        ShowRollHint = false;
        ShowFlipHint = false;
    }

    public string AppendUser(string? text, int userId, string name)
    {
        _pickedNames[name] = userId;
        Close();
        string prefix = text ?? string.Empty;
        if (prefix.Length > 0 && !char.IsWhiteSpace(prefix[^1]))
            prefix += " ";
        return prefix + "@" + name + " ";
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
            var cancellationToken = search.Token;
            await Task.Delay(SearchDelay, cancellationToken);
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

                _search = null;
                ApplySuggestions(suggestions);
                IsSearching = false;
                IsOpen = suggestions.Count > 0;
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // Close cancels a search, but the asynchronous operation owns its lifetime.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (search == _search)
                {
                    _search = null;
                    IsSearching = false;
                }
            });
            search.Dispose();
        }
    }

    private void ApplySuggestions(IReadOnlyList<ChatMentionSuggestionVM> results)
    {
        var selected = SelectedIndex >= 0 && SelectedIndex < Suggestions.Count ? Suggestions[SelectedIndex] : null;
        var updated = new List<ChatMentionSuggestionVM>(results.Count);
        foreach (var result in results)
        {
            var existing = Suggestions.FirstOrDefault(item => item.IsCommand == result.IsCommand && item.UserId == result.UserId
                && (item.UserId != null || item.Name == result.Name));
            if (existing == null)
            {
                updated.Add(result);
                continue;
            }

            existing.Name = result.Name;
            existing.AvatarUrl = result.AvatarUrl;
            existing.Status = result.Status;
            existing.Hint = result.Hint;
            updated.Add(existing);
        }

        // Preserve controls, avatar loads and hover state for users still in the results.
        for (int i = Suggestions.Count - 1; i >= 0; i--)
            if (!updated.Contains(Suggestions[i]))
                Suggestions.RemoveAt(i);
        for (int i = 0; i < updated.Count; i++)
        {
            int previous = Suggestions.IndexOf(updated[i]);
            if (previous < 0)
                Suggestions.Insert(i, updated[i]);
            else if (previous != i)
                Suggestions.Move(previous, i);
        }

        int selectedIndex = selected == null ? -1 : Suggestions.IndexOf(selected);
        // An index may now refer to a different row after a removal or move.
        if (selected != null && selectedIndex < 0)
            selected.IsSelected = false;
        SelectedIndex = selectedIndex >= 0 ? selectedIndex : Suggestions.Count > 0 ? 0 : -1;
        if (SelectedIndex >= 0)
            Suggestions[SelectedIndex].IsSelected = true;
        UpdateCommandHint();
    }

    // A leading command, or the "@" before the caret, and what follows it
    private static (int Start, string Query) FindQuery(string? text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text) || caretIndex <= 0 || caretIndex > text.Length)
            return (-1, "");

        // Commands belong at the start of a message; keep their argument help while editing the line.
        if (text[0] == '/' && !text.AsSpan(0, caretIndex).ContainsAny('\r', '\n'))
            return (0, text[1..caretIndex]);

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

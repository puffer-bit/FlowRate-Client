using System.Text;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Mentions;

namespace Client.Core.Chat;

/// <summary>
/// A mention as it is shown: where its "@name" sits in the shown text, and whether it calls the
/// current user (by name, or everyone).
/// </summary>
public readonly record struct ChatMentionSpan(int Start, int Length, bool IsSelf)
{
    public int End => Start + Length;
}

/// <summary>
/// The text of a message as it is shown, with its mention tokens written as "@name".
/// </summary>
public sealed record ChatMessageBody(string Text, IReadOnlyList<ChatMentionSpan> Mentions)
{
    public const string EveryoneName = "everyone";
    public const string HereName = "here";

    // Compared by content, so a body rendered again with the same names is no change for the view
    public bool Equals(ChatMessageBody? other) =>
        other is not null && Text == other.Text && Mentions.SequenceEqual(other.Mentions);

    public override int GetHashCode() => HashCode.Combine(Text, Mentions.Count);

    /// <param name="nameOf">The name of a mentioned user, or null when it is not known (yet).</param>
    /// <param name="unknownName">Shown for a user with no name.</param>
    public static ChatMessageBody Create(string text, Func<int, string?> nameOf, int currentUserId,
        string unknownName)
    {
        var tokens = ChatMentions.Find(text);
        if (tokens.Count == 0)
            return new ChatMessageBody(text, []);

        var builder = new StringBuilder(text.Length);
        var spans = new ChatMentionSpan[tokens.Count];
        int position = 0;

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            builder.Append(text, position, token.Start - position);

            string name = token.Kind switch
            {
                ChatMentionKind.Everyone => EveryoneName,
                ChatMentionKind.Here => HereName,
                _ => nameOf(token.UserId) ?? unknownName
            };

            bool isSelf = token.Kind != ChatMentionKind.User || token.UserId == currentUserId;
            spans[i] = new ChatMentionSpan(builder.Length, name.Length + 1, isSelf);
            builder.Append('@').Append(name);
            position = token.End;
        }

        builder.Append(text, position, text.Length - position);
        return new ChatMessageBody(builder.ToString(), spans);
    }

    /// <summary>
    /// The ids of the users the text mentions by id, to look their names up.
    /// </summary>
    public static IEnumerable<int> UserIdsIn(string? text) =>
        ChatMentions.UserIdsOf(ChatMentions.Find(text));
}

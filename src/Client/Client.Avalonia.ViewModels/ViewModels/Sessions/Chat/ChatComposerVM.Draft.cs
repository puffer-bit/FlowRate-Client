using System.Threading.Tasks;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// The draft of the chat: what is typed and not sent is kept, with its mentions as tokens like a sent
/// message, and comes back into the input when the chat is opened again. The text of a message being
/// edited is not a draft: the one put aside when the edit started stays kept.
/// </summary>
public sealed partial class ChatComposerVM
{
    private readonly ChatDrafts? _drafts;
    private readonly string _coordinatorInstanceId;
    // Set once the stored draft is in the input, or the user typed before it was read
    private bool _draftRestored;

    private async Task RestoreDraftAsync()
    {
        if (_drafts is not { } drafts)
            return;

        await drafts.LoadAsync(_coordinatorInstanceId);
        if (_draftRestored || _typingDisposed)
            return;

        _draftRestored = true;
        if (!string.IsNullOrEmpty(InputtedText) || IsEditingMessage
            || drafts.Get(_coordinatorInstanceId, _chatId) is not { } draft)
            return;

        SetRestoredText(MentionPicker.Decode(draft, _senders.FindName));
    }

    private void OnDraftInputChanged()
    {
        if (_drafts == null || IsEditingMessage || _typingDisposed)
            return;

        // Restoring the draft puts in the text it was made from
        if (!_draftRestored && _restoringText)
            return;

        _draftRestored = true;
        _drafts.Set(_coordinatorInstanceId, _chatId,
            string.IsNullOrWhiteSpace(InputtedText) ? null : MentionPicker.Encode(InputtedText));
    }

    private void FlushDraft()
    {
        if (_drafts == null)
            return;

        // Closed in the middle of an edit, the draft is what was put aside for it. Encoded once more: a
        // mention picked last is known to the picker only after the text with it came in
        var text = IsEditingMessage ? _draftText : InputtedText;
        if (_draftRestored || IsEditingMessage)
            _drafts.Set(_coordinatorInstanceId, _chatId, string.IsNullOrWhiteSpace(text) ? null : MentionPicker.Encode(text));
        _drafts.Flush(_coordinatorInstanceId, _chatId);
    }
}

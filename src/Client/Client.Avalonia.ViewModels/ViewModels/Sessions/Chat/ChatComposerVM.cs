using Shared.Models.DataTransferObjects.ChatSession.Messages.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Tasks;
using Client.Avalonia.Localization;
using Client.Avalonia.Styles.Animation;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Services;
using ReactiveUI;
using Shared.Enums;
using Shared.Validation;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// The input of a chat: the text being typed, the files attached to it, the message it answers
/// or edits, and sending all of that.
/// </summary>
public sealed partial class ChatComposerVM : ReactiveObject, IDisposable
{
    public string InputWatermark => CanWrite
        ? Strings.Format("chat.input.watermark", ("chat", _chatName))
        : Strings.Get("chat.input.noPermission");

    /// <summary>The roles of the current user let it write in the chat.</summary>
    public bool CanWrite
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(InputWatermark));
            RaiseLinkPreviewBar();
            this.RaisePropertyChanged(nameof(IsAppLinkBarVisible));
            if (!value) StopTyping();
        }
    } = true;

    public string? InputtedText
    {
        get;
        set
        {
            if (field == value)
                return;
            this.RaiseAndSetIfChanged(ref field, value);
            OnTypingInputChanged();
            OnLinkInputChanged();
            OnAppLinkInputChanged();
            OnDraftInputChanged();
            this.RaisePropertyChanged(nameof(RemainingCharacters));
            this.RaisePropertyChanged(nameof(IsLengthCounterVisible));
            // A failed send is about the text that was there; editing it starts over
            SendError = null;
            RaiseNotice();
        }
    }

    /// <summary>
    /// Why the last send failed, shown under the input until the text changes or a send succeeds.
    /// </summary>
    public string? SendError
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseNotice();
        }
    }

    /// <summary>
    /// Why some of the picked files were not attached, shown under the input until the draft changes.
    /// </summary>
    private string? AttachNotice
    {
        get;
        set
        {
            field = value;
            RaiseNotice();
        }
    }

    /// <summary>
    /// The line under the input and the color of its frame: an error outranks a warning.
    /// </summary>
    public string? ComposerNotice => SendError
        ?? AttachNotice
        ?? (RemainingCharacters <= 0 ? Strings.Format("chat.notice.limitReached", ("limit", MaxMessageLength)) : null);

    public bool HasComposerNotice => ComposerNotice != null;
    public bool IsComposerError => SendError != null;
    public bool IsComposerWarning => HasComposerNotice && !IsComposerError;

    private void RaiseNotice()
    {
        this.RaisePropertyChanged(nameof(ComposerNotice));
        this.RaisePropertyChanged(nameof(HasComposerNotice));
        this.RaisePropertyChanged(nameof(IsComposerError));
        this.RaisePropertyChanged(nameof(IsComposerWarning));
    }

    /// <summary>
    /// The input refuses typing and pasting past this length.
    /// </summary>
    public int MaxMessageLength => ChatTextContent.MaxLength;

    public int RemainingCharacters => MaxMessageLength - (InputtedText?.Length ?? 0);

    // The counter shows up only close to the limit so it does not distract in ordinary messages
    private const int LengthCounterThreshold = 500;
    public bool IsLengthCounterVisible => RemainingCharacters <= LengthCounterThreshold;

    public bool IsMessageSending
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// False while the coordinator connection is down: nothing can be sent.
    /// </summary>
    public bool IsConnected
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            if (!value) StopTyping();
        }
    }

    /// <summary>
    /// The message the next sent message will answer, shown in the reply bar above the input.
    /// </summary>
    public ChatReplyPreviewVM? ReplyPreview
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsReplyAttached));
            UpdateReplyBar(EditPreview ?? value);
        }
    }

    public bool IsReplyAttached => ReplyPreview != null;

    /// <summary>
    /// The message being edited. It takes the reply bar over; a reply attached to the draft
    /// comes back when the edit ends.
    /// </summary>
    public ChatReplyPreviewVM? EditPreview
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateReplyBar(value ?? ReplyPreview);
        }
    }

    /// <summary>
    /// What the reply bar shows: the attached reply, and for the time the bar takes to leave, the
    /// one that has just been detached, so that the bar does not go blank while it folds.
    /// </summary>
    public ChatReplyPreviewVM? ReplyBarPreview
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsReplyBarLeaving
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The message whose text is in the input: sending replaces its text instead of adding a message.
    /// </summary>
    public ChatMessageModel? EditMessage
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsEditingMessage));
            this.RaisePropertyChanged(nameof(CancelBarToolTip));
            this.RaisePropertyChanged(nameof(CanAttachMore));
        }
    }

    public bool IsEditingMessage => EditMessage != null;

    public string CancelBarToolTip => Strings.Get(IsEditingMessage ? "chat.cancelEdit" : "chat.cancelReply");

    // What was being typed when the edit started; put back when it ends
    private string? _draftText;

    /// <summary>
    /// Files attached to the message being typed. They are uploaded when the message is sent
    /// and stay here, with what was uploaded, if it does not go.
    /// </summary>
    public AvaloniaList<ChatAttachmentVM> Attachments { get; } = new();

    public bool HasAttachments => Attachments.Count > 0;
    public bool IsAttachmentsAllowed { get; }

    /// <summary>The roles of the current user let it attach files in the chat.</summary>
    public bool CanAttachFiles
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(CanAttachMore));
            this.RaisePropertyChanged(nameof(AttachToolTip));
        }
    } = true;
    public int MaxAttachments => Limits.ChatAttachmentsMaxCount;
    // An edit changes only the text of a message
    public bool CanAttachMore => IsAttachmentsAllowed && CanAttachFiles && CanWrite && !IsEditingMessage && Attachments.Count < MaxAttachments;

    public string AttachToolTip => !IsAttachmentsAllowed
        ? Strings.Get("chat.attach.turnedOff")
        : !CanAttachFiles || !CanWrite
        ? Strings.Get("chat.attach.noPermission")
        : Attachments.Count >= MaxAttachments
            ? Strings.Format("chat.attach.limit", ("limit", MaxAttachments))
            : Strings.Get("chat.attach.tooltip");

    /// <summary>
    /// The list of users that opens while a mention is typed in the input.
    /// </summary>
    public ChatMentionPickerVM MentionPicker { get; }

    public event Action? MentionInserted;

    public void MentionUser(int userId, string name)
    {
        if (!CanWrite || !IsConnected)
            return;

        InputtedText = MentionPicker.AppendUser(InputtedText, userId, name);
        MentionInserted?.Invoke();
    }

    public Interaction<RxVoid, IReadOnlyList<PickedFile>> PickAttachmentFiles { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> SendMessageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ResetReplyMessageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> JumpToReplyTargetCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AttachFilesCommand { get; }
    public ReactiveCommand<ChatAttachmentVM, RxVoid> RemoveAttachmentCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleDefaultCompressionCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleDefaultMetadataCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleDefaultVideoCompressionCommand { get; }
    public ReactiveCommand<int, RxVoid> ChooseDefaultVideoSideCommand { get; }

    public static IReadOnlyList<string> ImageFilePatterns { get; } = ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif"];

    /// <summary>What is done to images attached from now on can be chosen.</summary>
    public bool CanChooseAttachmentDefaults => _attachmentOptions != null;

    /// <summary>Images attached from now on are compressed.</summary>
    public bool DefaultCompressImages
    {
        get => _attachmentOptions?.Defaults.CompressImages == true || IsImageCompressionForced;
        set
        {
            if (_attachmentOptions is { } options && !IsImageCompressionForced)
                _ = options.SetDefaultsAsync(options.Defaults with { CompressImages = value });
        }
    }

    /// <summary>Images attached from now on are sent without their metadata.</summary>
    public bool DefaultStripMetadata
    {
        get => _attachmentOptions?.Defaults.StripMetadata == true;
        set
        {
            if (_attachmentOptions is { } options)
                _ = options.SetDefaultsAsync(options.Defaults with { StripMetadata = value });
        }
    }

    /// <summary>The server takes only compressed images.</summary>
    public bool IsImageCompressionForced => !ImagePolicy.AllowOriginals;
    public bool CanToggleDefaultCompression => CanChooseAttachmentDefaults && !IsImageCompressionForced;

    private ImageCompressionPolicy ImagePolicy => _session?.CoordinatorModel?.ImagePolicy ?? ImageCompressionPolicy.Default;

    /// <summary>What is done to videos attached from now on can be chosen: this client compresses them.</summary>
    public bool CanChooseVideoDefaults => _attachmentOptions?.CanPrepareVideos == true;

    /// <summary>Videos attached from now on are compressed.</summary>
    public bool DefaultCompressVideos
    {
        get => _attachmentOptions?.Defaults.CompressVideos == true || IsVideoCompressionForced;
        set
        {
            if (_attachmentOptions is { } options && !IsVideoCompressionForced)
                _ = options.SetDefaultsAsync(options.Defaults with { CompressVideos = value });
        }
    }

    /// <summary>The server takes only compressed videos.</summary>
    public bool IsVideoCompressionForced => !VideoPolicy.AllowOriginals;
    public bool CanToggleDefaultVideoCompression => CanChooseVideoDefaults && !IsVideoCompressionForced;

    /// <summary>The resolutions videos attached from now on may be compressed to, the chosen one selected.</summary>
    public IReadOnlyList<ChatVideoSideOptionVM> DefaultVideoSides
    {
        get
        {
            var chosen = VideoPolicy.SideFor(_attachmentOptions?.Defaults.VideoSide);
            return VideoPolicy.Sides.Select(side => new ChatVideoSideOptionVM(side) { IsSelected = side == chosen }).ToList();
        }
    }

    private VideoCompressionPolicy VideoPolicy => _session?.CoordinatorModel?.VideoPolicy ?? VideoCompressionPolicy.Default;

    private readonly int _chatId;
    private readonly string _chatName;
    private readonly ICoordinatorSession _session;
    private readonly IChatMediaUploader _mediaUploader;
    private readonly IChatImageService _images;
    private readonly ChatSenderDirectory _senders;
    private readonly ChatAttachmentOptions? _attachmentOptions;
    // Shows the uploads and compressions in the task list
    private readonly ITaskTracker? _tasks;
    // Ends the uploads of the message being sent
    private CancellationTokenSource? _sendCancellation;

    /// <param name="jumpToMessage">Shows a message of the chat, for the reply bar.</param>
    public ChatComposerVM(int chatId, string chatName, bool isAttachmentsAllowed, ICoordinatorSession session,
        int currentUserId, IChatMediaUploader mediaUploader, IChatImageService images, ChatSenderDirectory senders,
        Func<int, Task> jumpToMessage, ChatAttachmentOptions? attachmentOptions = null,
        Func<IReadOnlyCollection<int>?>? mentionableMembers = null, ChatDrafts? drafts = null,
        string? coordinatorInstanceId = null, ITaskTracker? tasks = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _tasks = tasks;
        _drafts = coordinatorInstanceId != null ? drafts : null;
        _coordinatorInstanceId = coordinatorInstanceId ?? string.Empty;
        _attachmentOptions = attachmentOptions;
        if (attachmentOptions != null)
        {
            attachmentOptions.DefaultsChanged += OnAttachmentDefaultsChanged;
            _ = attachmentOptions.LoadAsync();
        }
        session.CoordinatorReconfigured += OnCoordinatorReconfigured;
        _chatId = chatId;
        _session = session;
        _mediaUploader = mediaUploader;
        _images = images;
        _senders = senders;
        _chatName = chatName;
        IsAttachmentsAllowed = isAttachmentsAllowed;
        MentionPicker = new ChatMentionPickerVM(session, currentUserId, mentionableMembers);

        Attachments.CollectionChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(HasAttachments));
            this.RaisePropertyChanged(nameof(CanAttachMore));
            this.RaisePropertyChanged(nameof(AttachToolTip));
            // Both are about the draft as it was
            SendError = null;
            AttachNotice = null;
        };

        var canSend = this.WatchProperties(nameof(InputtedText), nameof(IsMessageSending), nameof(HasAttachments),
                nameof(IsConnected), nameof(IsEditingMessage), nameof(CanWrite))
            .Select(vm => vm.IsConnected && vm.CanWrite && !vm.IsMessageSending
                && (vm.InputtedText?.Length ?? 0) <= ChatTextContent.MaxLength
                // Attachments go without a text too; an edit is about the text alone
                && (!string.IsNullOrWhiteSpace(vm.InputtedText) || (vm.HasAttachments && !vm.IsEditingMessage)));
        SendMessageCommand = ReactiveCommand.CreateFromTask(SendMessage, canSend);

        // The close button of the bar: the edit goes first, the reply under it stays
        ResetReplyMessageCommand = ReactiveCommand.Create(() =>
        {
            if (IsEditingMessage)
                EndEdit();
            else
                ResetReply();
        });
        JumpToReplyTargetCommand = ReactiveCommand.CreateFromTask(
            () => (EditPreview ?? ReplyPreview) is { } shown ? jumpToMessage(shown.MessageId) : Task.CompletedTask);

        var canAttach = this.WatchProperties(nameof(IsMessageSending), nameof(CanAttachMore))
            .Select(vm => !vm.IsMessageSending && vm.CanAttachMore);
        AttachFilesCommand = ReactiveCommand.CreateFromTask(
            async () => AddAttachments(await PickAttachmentFiles.Handle(RxVoid.Default)), canAttach);
        RemoveAttachmentCommand = ReactiveCommand.Create<ChatAttachmentVM>(RemoveAttachment);
        ToggleDefaultCompressionCommand = ReactiveCommand.Create(() => { DefaultCompressImages = !DefaultCompressImages; },
            this.WatchProperties(nameof(CanToggleDefaultCompression)).Select(vm => vm.CanToggleDefaultCompression));
        ToggleDefaultMetadataCommand = ReactiveCommand.Create(() => { DefaultStripMetadata = !DefaultStripMetadata; },
            this.WatchProperties(nameof(CanChooseAttachmentDefaults)).Select(vm => vm.CanChooseAttachmentDefaults));
        ToggleDefaultVideoCompressionCommand = ReactiveCommand.Create(() => { DefaultCompressVideos = !DefaultCompressVideos; },
            this.WatchProperties(nameof(CanToggleDefaultVideoCompression)).Select(vm => vm.CanToggleDefaultVideoCompression));
        ChooseDefaultVideoSideCommand = ReactiveCommand.Create<int>(side =>
            {
                if (_attachmentOptions is { } options)
                    _ = options.SetDefaultsAsync(options.Defaults with { VideoSide = side });
            },
            this.WatchProperties(nameof(CanChooseVideoDefaults)).Select(vm => vm.CanChooseVideoDefaults));

        if (_drafts != null)
            _ = RestoreDraftAsync();
    }

    /// <summary>
    /// Attaches files up to the limit; a file attached already is not added twice. The files come from
    /// the picker, or from the view when they were dropped or pasted.
    /// </summary>
    public void AddAttachments(IReadOnlyList<PickedFile> files)
    {
        if (!CanAttachMore || IsMessageSending)
            return;

        string? notice = null;
        foreach (var file in files)
        {
            if (Attachments.Count >= MaxAttachments)
            {
                notice = Strings.Format("chat.attach.limit", ("limit", MaxAttachments));
                break;
            }

            if (file.Key != null && Attachments.Any(a => a.Key == file.Key))
                continue;

            // A picture by its name goes as an image; whether it is one is known when the file is read
            var isImage = ImageFilePatterns.Any(pattern =>
                file.Name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase));
            Attachments.Add(new ChatAttachmentVM(file, isImage, _images, _attachmentOptions?.Preparer,
                _attachmentOptions?.Defaults, ImagePolicy, _attachmentOptions?.VideoPreparer, VideoPolicy, _tasks, _chatName));
        }

        // After the collection has changed, which clears the notice of the draft before it
        if (notice != null)
            AttachNotice = notice;
    }

    private void OnAttachmentDefaultsChanged() => Dispatcher.UIThread.Post(() =>
    {
        this.RaisePropertyChanged(nameof(DefaultCompressImages));
        this.RaisePropertyChanged(nameof(DefaultStripMetadata));
        this.RaisePropertyChanged(nameof(DefaultCompressVideos));
        this.RaisePropertyChanged(nameof(DefaultVideoSides));
    });

    // The server may now take only compressed images, or uncompressed ones again
    private void OnCoordinatorReconfigured(CoordinatorSessionModel coordinator) => Dispatcher.UIThread.Post(() =>
    {
        foreach (var attachment in Attachments)
        {
            attachment.ApplyPolicy(coordinator.ImagePolicy);
            attachment.ApplyVideoPolicy(coordinator.VideoPolicy);
        }
        this.RaisePropertyChanged(nameof(IsImageCompressionForced));
        this.RaisePropertyChanged(nameof(CanToggleDefaultCompression));
        this.RaisePropertyChanged(nameof(DefaultCompressImages));
        this.RaisePropertyChanged(nameof(IsVideoCompressionForced));
        this.RaisePropertyChanged(nameof(CanToggleDefaultVideoCompression));
        this.RaisePropertyChanged(nameof(DefaultCompressVideos));
        this.RaisePropertyChanged(nameof(DefaultVideoSides));
    });

    /// <summary>
    /// Taking a file off the message being sent stops the sending: the message would go without it.
    /// </summary>
    private void RemoveAttachment(ChatAttachmentVM attachment)
    {
        if (IsMessageSending)
            _sendCancellation?.Cancel();

        Attachments.Remove(attachment);
        attachment.Dispose();
    }

    public void SetReply(ChatMessageModel messageModel)
    {
        ReplyPreview = new ChatReplyPreviewVM(messageModel);
        _senders.Apply(ReplyPreview);
    }

    private void ResetReply() => ReplyPreview = null;

    public void StartEdit(ChatMessageModel messageModel)
    {
        if (messageModel.TextContent == null)
            return;

        // Going from one edit to another keeps the draft that was put aside first
        BeginLinkPreviewEdit(messageModel, keepsDraft: IsEditingMessage);
        if (!IsEditingMessage)
            _draftText = InputtedText;

        EditMessage = messageModel;
        EditPreview = new ChatReplyPreviewVM(messageModel, isEdit: true);
        EditPreview.RenderText(_senders.RenderText);
        SetRestoredText(MentionPicker.Decode(messageModel.TextContent.Text, _senders.FindName));
    }

    /// <summary>The newest message of the user that can be edited, when the newest messages are loaded.</summary>
    public Func<ChatMessageModel?>? FindLastEditableMessage { get; set; }

    /// <summary>
    /// Up in an empty input edits the last message of the user, as in Discord and Telegram.
    /// </summary>
    /// <returns>False when the input is not empty or there is no message to edit.</returns>
    public bool TryEditLastMessage()
    {
        if (!string.IsNullOrEmpty(InputtedText) || HasAttachments || IsEditingMessage || IsMessageSending
            || !IsConnected || !CanWrite || FindLastEditableMessage?.Invoke() is not { } message)
            return false;

        StartEdit(message);
        return IsEditingMessage;
    }

    private void EndEdit()
    {
        if (!IsEditingMessage)
            return;

        EditMessage = null;
        EditPreview = null;
        EndLinkPreviewEdit();
        SetRestoredText(_draftText ?? string.Empty);
        _draftText = null;
    }

    /// <summary>
    /// The users the bars need, to be looked up with those of the messages.
    /// </summary>
    public void CollectUserIds(HashSet<int> userIds) =>
        userIds.UnionWith(ChatMessageBody.UserIdsIn(ReplyPreview?.SourceText));

    /// <summary>
    /// Shows the bars again after the names of users have changed.
    /// </summary>
    public void ApplySenders()
    {
        if (ReplyPreview != null)
            _senders.Apply(ReplyPreview);
        EditPreview?.RenderText(_senders.RenderText);
    }

    /// <summary>
    /// The current state of a message, which the bars follow when they show it.
    /// </summary>
    public void OnMessageChanged(ChatMessageModel message)
    {
        if (ReplyPreview?.MessageId == message.Id)
            ReplyPreview.Update(message);

        // Changed from another device meanwhile: the bar follows, the text being typed is left alone
        if (EditPreview?.MessageId == message.Id)
            EditPreview.Update(message);
    }

    public void OnMessageRemoved(int messageId)
    {
        if (ReplyPreview?.MessageId == messageId)
            ResetReply();

        if (EditMessage?.Id == messageId)
            EndEdit();
    }

    private void UpdateReplyBar(ChatReplyPreviewVM? reply)
    {
        if (reply != null)
        {
            ReplyBarPreview = reply;
            IsReplyBarLeaving = false;
            return;
        }

        var detached = ReplyBarPreview;
        if (detached == null || IsReplyBarLeaving)
            return;

        IsReplyBarLeaving = true;
        DispatcherTimer.RunOnce(() =>
        {
            // Another reply was attached in the meantime and the bar shows it now
            if (ReplyBarPreview != detached || !IsReplyBarLeaving)
                return;

            ReplyBarPreview = null;
            IsReplyBarLeaving = false;
        }, Presence.LeaveTime);
    }

    private async Task SendMessage()
    {
        // Enter in the input calls the command directly, so the checks are repeated here
        if (IsMessageSending || !IsConnected || !CanWrite)
            return;

        var textContent = ChatTextContent.FromUserInput(
            InputtedText is { } inputted ? MentionPicker.Encode(inputted) : null);
        if (textContent == null)
        {
            var length = ChatTextContent.Normalize(InputtedText).Length;
            if (length > ChatTextContent.MaxLength)
            {
                SendError = Strings.Format("chat.send.tooLong", ("length", length), ("limit", ChatTextContent.MaxLength));
                return;
            }

            // Nothing visible was typed: only attachments can make a message of it
            if (IsEditingMessage || !HasAttachments)
                return;
        }

        if (EditMessage is { } edited)
        {
            await SendEdit(edited, textContent!);
            return;
        }

        if (ChatCommandRequest.Parse(textContent?.Text) is { IsValid: false } command)
        {
            SendError = Strings.Get(command.Kind == ChatCommandKind.Roll
                ? "chat.command.roll.invalid" : "chat.command.flip.invalid");
            return;
        }

        StopTyping();
        IsMessageSending = true;
        SendError = null;
        using var cancellation = _sendCancellation = new CancellationTokenSource();
        try
        {
            // The same attachments the message is built from: the list may change while they upload
            var attachments = Attachments.ToList();
            foreach (var attachment in attachments)
                attachment.IsLocked = true;
            if (!await UploadAttachments(attachments, cancellation.Token))
                return;

            var message = CreateMessage(textContent, attachments);
            var result = await _session.SendChatMessage(message);
            ApplySendResult(result, attachments);
        }
        finally
        {
            _sendCancellation = null;
            IsMessageSending = false;
            foreach (var attachment in Attachments)
                attachment.IsLocked = false;
        }
    }

    private NewChatMessage CreateMessage(ChatTextContent? textContent, IReadOnlyList<ChatAttachmentVM> attachments) => new()
    {
        ChatId = _chatId,
        SenderUserId = _session.GetCurrentUser().Id,
        SentTime = DateTimeOffset.UtcNow,
        TextContent = textContent,
        ReplyContent = ReplyPreview != null ? new ChatReplyContent { MessageId = ReplyPreview.MessageId } : null,
        Images = attachments.Where(attachment => attachment.IsImage)
            .Select(attachment => attachment.Upload.Image!).ToList(),
        Files = attachments.Where(attachment => attachment.IsFile)
            .Select(attachment => attachment.FileUpload.File!).ToList(),
        IsLinkPreviewDisabled = IsLinkPreviewDisabledForSend()
    };

    private void ApplySendResult(RequestResult result, IReadOnlyList<ChatAttachmentVM> attachments)
    {
        switch (result)
        {
            case { IsSuccess: true }:
                ResetLinkPreview();
                InputtedText = string.Empty;
                MentionPicker.Reset();
                foreach (var attachment in Attachments)
                    attachment.Dispose();
                Attachments.Clear();
                ResetReply();
                break;
            // A rejection is reported by the main window
            case { IsRejected: true }:
                break;
            // The server takes only compressed images, which the cards now are, so the next try sends them anew
            case { Error: ErrorCode.ImageNotCompressed }:
                foreach (var attachment in attachments)
                {
                    attachment.ApplyPolicy(ImagePolicy);
                    attachment.ResetUpload();
                }
                SendError = Strings.Get("chat.send.imageNotCompressed");
                break;
            // The server takes only compressed videos, and this one could not be compressed here
            case { Error: ErrorCode.VideoNotCompressed }:
                foreach (var attachment in attachments)
                {
                    attachment.ApplyVideoPolicy(VideoPolicy);
                    attachment.ResetUpload();
                }
                SendError = Strings.Get("chat.send.videoNotCompressed");
                break;
            // The server does not hold the uploads anymore, so the next try sends the files again
            case { Error: ErrorCode.AttachmentNotReady } or { Error: ErrorCode.AttachmentInvalid }:
                foreach (var attachment in attachments)
                    attachment.ResetUpload();
                SendError = Strings.Get("chat.send.imagesLost");
                break;
            // The text and the files stay in the composer for another try
            default:
                SendError = Strings.Get(result switch
                {
                    { Error: ErrorCode.TimedOut } => "chat.send.timedOut",
                    { Error: ErrorCode.AttachmentsNotAllowed } => "chat.attach.turnedOff",
                    _ => "chat.send.refused"
                });
                break;
        }
    }

    /// <summary>
    /// Uploads the files one after another, each showing its progress on its card.
    /// </summary>
    /// <returns>False when one did not go; its card and the line under the input say why.</returns>
    private async Task<bool> UploadAttachments(IReadOnlyList<ChatAttachmentVM> attachments, CancellationToken cancellationToken)
    {
        foreach (var attachment in attachments)
        {
            attachment.Error = null;
            attachment.IsUploading = true;
            // Cancelled from the task list the same way as by taking a file off: the whole message stops
            var cancellation = _sendCancellation;
            using var task = _tasks?.Begin(TrackedTaskKind.Upload,
                attachment.IsPreparing ? TrackedTaskStage.Waiting : TrackedTaskStage.Uploading, attachment.FileName,
                _chatName, cancellation == null ? null : () => cancellation.Cancel());
            ChatMediaUploadResult result;
            try
            {
                var progress = new Progress<double>(share =>
                {
                    attachment.Progress = share;
                    task?.Report(share);
                });
                // A compressed copy may still be in the making
                ChatMediaSource? source;
                try { source = attachment.IsImage || attachment.IsVideo ? await attachment.GetUploadSourceAsync(cancellationToken) : null; }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
                task?.SetStage(TrackedTaskStage.Uploading);
                task?.SetTotalBytes((source ?? attachment.Source).Size);

                result = source != null && attachment.IsImage
                    ? await _mediaUploader.UploadImageAsync(_session, _chatId, source, attachment.Upload,
                        progress, cancellationToken)
                    : ChatMediaUploadResult.NotAnImage;

                // Named like a picture but not one the chat can show: it is still a file to send
                if (result == ChatMediaUploadResult.NotAnImage)
                {
                    // A video goes as the file it was compressed to, and its card keeps showing the sizes
                    if (!attachment.IsVideo)
                        attachment.SendAsFile();
                    result = await _mediaUploader.UploadFileAsync(_session, _chatId,
                        attachment.IsVideo ? source ?? attachment.Source : attachment.Source,
                        attachment.FileUpload, progress, cancellationToken);
                }
            }
            finally
            {
                attachment.IsUploading = false;
            }

            if (result == ChatMediaUploadResult.NoError)
            {
                attachment.Progress = 1;
                task?.Complete();
                continue;
            }

            // The user took a file off the message; nothing went wrong
            if (result == ChatMediaUploadResult.Canceled)
                return false;

            attachment.Error = Strings.Get(result switch
            {
                ChatMediaUploadResult.NotAnImage => "chat.attach.error.notAnImage",
                ChatMediaUploadResult.Empty => "chat.attach.error.empty",
                ChatMediaUploadResult.TooLarge => "chat.attach.error.tooLarge",
                ChatMediaUploadResult.NotAllowed => "chat.attach.turnedOff",
                ChatMediaUploadResult.TooManyUploads => "chat.attach.error.tooMany",
                ChatMediaUploadResult.StorageFull => "chat.attach.error.storageFull",
                ChatMediaUploadResult.ConnectionFailed => "chat.attach.error.connection",
                ChatMediaUploadResult.Unreadable => "chat.attach.error.unreadable",
                _ => "chat.attach.error.failed"
            });
            SendError = Strings.Format("chat.send.imageFailed", ("file", attachment.FileName), ("reason", attachment.Error));
            task?.Fail(attachment.Error);
            return false;
        }

        return true;
    }

    /// <summary>
    /// The message changes in the list once the server broadcasts the edit, like for any other client.
    /// </summary>
    private async Task SendEdit(ChatMessageModel edited, ChatTextContent textContent)
    {
        // Sent only when it changes, so an edit from an older client never turns a preview back on
        var linkPreviewDisabled = IsLinkPreviewDisabledForSend();
        bool? linkPreviewChange = linkPreviewDisabled != edited.IsLinkPreviewDisabled ? linkPreviewDisabled : null;
        if (textContent.Text == edited.TextContent?.Text && linkPreviewChange == null)
        {
            EndEdit();
            return;
        }

        StopTyping();
        IsMessageSending = true;
        try
        {
            var result = await _session.EditChatMessage(edited.ChatId, edited.Id, textContent, linkPreviewChange);
            switch (result)
            {
                // The message may have been deleted while the request was on its way, which ended the edit already
                case { IsSuccess: true } when EditMessage?.Id == edited.Id:
                    EndEdit();
                    break;
                // A rejection is reported by the main window
                case { IsSuccess: true } or { IsRejected: true }:
                    break;
                // The text stays in the input for another try
                default:
                    SendError = Strings.Get(result.Error == ErrorCode.TimedOut ? "chat.edit.timedOut" : "chat.edit.refused");
                    break;
            }
        }
        finally
        {
            IsMessageSending = false;
        }
    }

    public void Dispose()
    {
        StopTyping();
        _typingDisposed = true;
        FlushDraft();
        _sendCancellation?.Cancel();
        DisposeLinkPreview();
        if (_attachmentOptions != null)
            _attachmentOptions.DefaultsChanged -= OnAttachmentDefaultsChanged;
        foreach (var attachment in Attachments)
            attachment.Dispose();
        _session.CoordinatorReconfigured -= OnCoordinatorReconfigured;
    }
}

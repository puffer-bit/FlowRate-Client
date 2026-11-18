using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Core.Chat;
using ReactiveUI;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Links;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// The preview of the link being typed, shown above the input as the message will show it. Closing it
/// sends the message without a preview; another link brings it back.
/// </summary>
public sealed partial class ChatComposerVM
{
    /// <summary>
    /// How long an address has to stay unchanged before the server is asked for its preview: typing
    /// changes it with every key.
    /// </summary>
    public TimeSpan LinkPreviewDelay { get; init; } = TimeSpan.FromMilliseconds(600);

    // Previews asked for in this chat, null for pages that have none; the server keeps them anyway
    private readonly Dictionary<string, ChatLinkPreview?> _linkPreviews = new();
    private CancellationTokenSource? _linkPreviewRequest;

    // The address whose preview the user closed, in the form ChatLinks finds it
    private string? _closedLinkPreview;
    private string? _draftClosedLinkPreview;

    /// <summary>The address the text shows a preview of; null without a link.</summary>
    public string? LinkPreviewTarget
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseLinkPreviewBar();
        }
    }

    /// <summary>The card above the input, once the server has answered.</summary>
    public ChatLinkPreviewVM? ComposerLinkPreview
    {
        get;
        private set
        {
            var previous = field;
            this.RaiseAndSetIfChanged(ref field, value);
            if (previous != value)
            {
                previous?.Hide();
                value?.Show();
            }
            RaiseLinkPreviewBar();
        }
    }

    /// <summary>The server is being asked for the preview of <see cref="LinkPreviewTarget"/>.</summary>
    public bool IsLinkPreviewLoading
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseLinkPreviewBar();
        }
    }

    /// <summary>The message will go without the preview of its link: the user closed it.</summary>
    public bool IsLinkPreviewClosed => LinkPreviewTarget != null && LinkPreviewTarget == _closedLinkPreview;

    public bool IsLinkPreviewBarVisible => CanWrite && !IsLinkPreviewClosed
        && (ComposerLinkPreview != null || IsLinkPreviewLoading);

    /// <summary>What the bar says while the page is being fetched: the site it comes from.</summary>
    public string? LinkPreviewLoadingHost =>
        LinkPreviewTarget != null && Uri.TryCreate(LinkPreviewTarget, UriKind.Absolute, out var url) ? url.Host : null;

    public ReactiveCommand<RxVoid, RxVoid> CloseLinkPreviewCommand =>
        field ??= ReactiveCommand.Create(CloseLinkPreview);

    private void RaiseLinkPreviewBar()
    {
        this.RaisePropertyChanged(nameof(IsLinkPreviewClosed));
        this.RaisePropertyChanged(nameof(IsLinkPreviewBarVisible));
        this.RaisePropertyChanged(nameof(LinkPreviewLoadingHost));
    }

    private void CloseLinkPreview()
    {
        _closedLinkPreview = LinkPreviewTarget;
        CancelLinkPreviewRequest();
        RaiseLinkPreviewBar();
    }

    /// <summary>Follows the text: a new link is asked for after a pause, a link taken out takes its card along.</summary>
    private void OnLinkInputChanged()
    {
        var target = KeyOf(ChatLinks.PreviewTarget(InputtedText));
        if (target == LinkPreviewTarget)
            return;

        CancelLinkPreviewRequest();
        LinkPreviewTarget = target;
        ComposerLinkPreview = null;

        if (target == null || target == _closedLinkPreview)
            return;

        if (_linkPreviews.TryGetValue(target, out var known))
        {
            ShowLinkPreview(known);
            return;
        }

        IsLinkPreviewLoading = true;
        var cancellation = _linkPreviewRequest = new CancellationTokenSource();
        _ = RequestLinkPreviewAsync(target, cancellation.Token);
    }

    private async Task RequestLinkPreviewAsync(string target, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(LinkPreviewDelay, cancellationToken);
            if (!IsConnected)
                return;

            // Started from the input, so this goes on on the UI thread
            var result = await _session.RequestLinkPreview(_chatId, target);
            if (cancellationToken.IsCancellationRequested || LinkPreviewTarget != target)
                return;

            // A page that failed now may work a moment later, so only answers are kept
            if (result.IsSuccess)
                _linkPreviews[target] = result.Value;
            ShowLinkPreview(result.Value);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ShowLinkPreview(ChatLinkPreview? preview)
    {
        IsLinkPreviewLoading = false;
        ComposerLinkPreview = preview == null ? null : new ChatLinkPreviewVM(preview, _session, _images);
    }

    private void CancelLinkPreviewRequest()
    {
        _linkPreviewRequest?.Cancel();
        _linkPreviewRequest = null;
        IsLinkPreviewLoading = false;
    }

    /// <summary>Whether the message being sent goes without a preview.</summary>
    private bool IsLinkPreviewDisabledForSend() =>
        KeyOf(ChatLinks.PreviewTarget(InputtedText)) is { } target && target == _closedLinkPreview;

    /// <summary>The closed preview of the message being edited is kept closed, its draft's aside.</summary>
    private void BeginLinkPreviewEdit(ChatMessageModel message, bool keepsDraft)
    {
        if (!keepsDraft)
            _draftClosedLinkPreview = _closedLinkPreview;
        _closedLinkPreview = message.IsLinkPreviewDisabled
            ? KeyOf(ChatLinks.PreviewTarget(message.TextContent?.Text))
            : null;
        if (message.LinkPreview is { } preview)
            _linkPreviews.TryAdd(preview.Url, preview);
    }

    private void EndLinkPreviewEdit() => _closedLinkPreview = _draftClosedLinkPreview;

    private void ResetLinkPreview()
    {
        _closedLinkPreview = null;
        CancelLinkPreviewRequest();
    }

    private void DisposeLinkPreview()
    {
        CancelLinkPreviewRequest();
        ComposerLinkPreview = null;
    }

    // The form the server answers with: the fragment never reaches a server, so it is not part of a page
    private static string? KeyOf(Uri? url) =>
        url == null ? null : new UriBuilder(url) { Fragment = string.Empty }.Uri.AbsoluteUri;
}

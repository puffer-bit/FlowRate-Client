using Client.Avalonia.Localization;
using Client.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Client.Views.Common;
using Client.Views.Dialogs;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

public partial class ChatSessionControl : ViewControl<ActiveChatSessionVM>
{
    private readonly ChatScrollController _scroll;

    internal ChatSelectionController Selection { get; }

    public ChatSessionControl()
    {
        InitializeComponent();
        _scroll = new ChatScrollController(this, MessagesList);
        Selection = new ChatSelectionController(MessagesList);

        // Files dropped anywhere on the chat are attached to the message being typed
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        this.WhenActivated(disposables =>
        {
            // The template reuses this view for every chat, and cached chats outlive it,
            // so the subscriptions to one chat end as soon as another one is shown
            this.WhenViewModelChanged((vm, subscriptions) =>
            {
                Selection.Clear();
                vm.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(subscriptions);

                vm.Composer.PickAttachmentFiles.RegisterHandler(async interaction =>
                    interaction.SetOutput(await PickFiles())).DisposeWith(subscriptions);

                vm.PickFileSaveTarget.RegisterHandler(async interaction =>
                    interaction.SetOutput(await PickSaveTarget(interaction.Input))).DisposeWith(subscriptions);

                // The viewer of an image is made anew for each image; saving asks this view where to
                var viewerSubscription = new SerialDisposable().DisposeWith(subscriptions);
                ObservableExtensions.Subscribe(
                    vm.WatchProperty(nameof(ActiveChatSessionVM.ImageViewer), x => x.ImageViewer),
                    viewer =>
                    {
                        viewerSubscription.Disposable = viewer?.PickSaveTarget.RegisterHandler(async interaction =>
                            interaction.SetOutput(await PickSaveTarget(interaction.Input)));

                        // The keys go to the viewer while it is open, not to the input under it
                        if (viewer != null)
                            Dispatcher.UIThread.Post(() => ImageViewerHost.Focus(), DispatcherPriority.Background);
                        else
                            Dispatcher.UIThread.Post(Composer.FocusInput, DispatcherPriority.Background);
                    }).DisposeWith(subscriptions);

                vm.ScrollToEndInteraction.RegisterHandler(interaction =>
                {
                    _scroll.ScrollToEnd();
                    interaction.SetOutput(RxVoid.Default);
                }).DisposeWith(subscriptions);

                vm.ScrollToMessageIndexInteraction.RegisterHandler(interaction =>
                {
                    _scroll.ScrollIndexToCenter(interaction.Input);
                    interaction.SetOutput(RxVoid.Default);
                }).DisposeWith(subscriptions);

                // The next message can be typed right away, also after sending with the button
                ObservableExtensions.Subscribe(vm.Composer.WatchProperty(nameof(ChatComposerVM.IsMessageSending), x => x.IsMessageSending)
                        .Skip(1)
                        .Where(sending => !sending),
                    _ => Dispatcher.UIThread.Post(Composer.FocusInput, DispatcherPriority.Background))
                    .DisposeWith(subscriptions);

                // Choosing Reply moves the caret to the input so the answer can be typed right away
                ObservableExtensions.Subscribe(vm.Composer.WatchProperty(nameof(ChatComposerVM.ReplyPreview), x => x.ReplyPreview)
                        .Where(reply => reply != null),
                    _ => Dispatcher.UIThread.Post(Composer.FocusInput, DispatcherPriority.Background))
                    .DisposeWith(subscriptions);

                // Choosing Edit puts the text into the input; the caret goes after it
                ObservableExtensions.Subscribe(vm.Composer.WatchProperty(nameof(ChatComposerVM.EditMessage), x => x.EditMessage)
                        .Where(edited => edited != null),
                    _ => Dispatcher.UIThread.Post(Composer.FocusInputAtEnd, DispatcherPriority.Background))
                    .DisposeWith(subscriptions);

                // A chat opens at its newest message, also one that was shown before and scrolled away from
                _scroll.ScrollToEnd();
                _scroll.Follow(vm).DisposeWith(subscriptions);
            }).DisposeWith(disposables);
        });
    }

    private async Task<IReadOnlyList<PickedFile>> PickFiles()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return [];

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("chat.attach.tooltip"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                PickedFiles.AnyFileType(Strings.Get("chat.attach.filter.all")),
                PickedFiles.ImageFileType(Strings.Get("chat.attach.filter.images"))
            ]
        });

        // The files stay open: they are read when the message is sent
        var picked = await PickedFiles.FromAsync(files);

        Composer.FocusInput();
        return picked;
    }

    private async Task<Stream?> PickSaveTarget(string fileName)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = fileName,
            ShowOverwritePrompt = true
        });

        return file == null ? null : await file.OpenWriteAsync();
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm || e.DataTransfer.TryGetFiles() is not { } files)
            return;

        e.Handled = true;
        vm.Composer.AddAttachments(await PickedFiles.FromAsync(files));
        Composer.FocusInput();
    }

    // Tunnels, so Esc closes the image before the input under it takes the key for itself
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel is { IsImageViewerOpen: true } vm)
        {
            vm.CloseImageViewer();
            e.Handled = true;
        }
    }
}

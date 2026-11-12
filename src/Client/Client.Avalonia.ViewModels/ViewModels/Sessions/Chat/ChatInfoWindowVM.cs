using System;
using System.Reactive.Linq;
using Client.Avalonia.Localization;
using Client.Core.Sessions;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

public sealed class ChatInfoWindowVM : ReactiveObject, IDisposable
{
    public string Name { get; }
    public string Description { get; }

    public Interaction<RxVoid, RxVoid> CloseWindow { get; } = new();
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    public ChatInfoWindowVM(ChatSessionModel session)
    {
        Name = session.Name;
        Description = string.IsNullOrWhiteSpace(session.Description)
            ? Strings.Get("chat.info.noDescription")
            : session.Description.Trim();
        CloseCommand = ReactiveCommand.CreateFromTask(async () =>
            await CloseWindow.Handle(RxVoid.Default));
    }

    public void Dispose() => CloseCommand.Dispose();
}

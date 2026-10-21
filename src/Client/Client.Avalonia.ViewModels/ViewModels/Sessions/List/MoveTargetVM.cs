using System;
using System.Threading.Tasks;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.List;

/// <summary>
/// A place a session can be moved to from its menu: a category, or none.
/// </summary>
public sealed class MoveTargetVM : ReactiveObject
{
    public int? CategoryId { get; }

    public string Name
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<ISessionVM, RxVoid> MoveHereCommand { get; }

    public MoveTargetVM(int? categoryId, string name, Func<ISessionVM, int?, Task> move)
    {
        CategoryId = categoryId;
        Name = name;
        MoveHereCommand = ReactiveCommand.CreateFromTask<ISessionVM>(session => move(session, CategoryId));
    }
}

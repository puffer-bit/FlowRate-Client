using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Services;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Roles;
using ReactiveUI;
using Shared.Enums;

using Result = Client.ViewModels.Dialogs.Result;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.ServerSettings;

/// <summary>
/// The roles of the server: the list, highest first, and the editor of the chosen one. Every change is saved at
/// once, apart from the settings of the other tabs, and changes made elsewhere show up as they come.
/// </summary>
public sealed class RolesTabVM : ReactiveObject, IDisposable
{
    /// <summary>The colors offered for a role, as on most chat servers; none is offered as well.</summary>
    public static IReadOnlyList<int> Palette { get; } =
    [
        0x1ABC9C, 0x2ECC71, 0x3498DB, 0x9B59B6, 0xE91E63, 0xF1C40F, 0xE67E22, 0xE74C3C, 0x95A5A6, 0x607D8B
    ];

    private readonly ICoordinatorSession _coordinatorSession;
    private readonly Interaction<MessageBoxVM, Result> _showMessageBox;
    private readonly ObservableAsPropertyHelper<bool> _isBusy;

    // Set while the list is rebuilt: the list box drops its selection when the list is cleared, which is not a
    // choice of the user and must not drop the edits; the editor follows the rebuilt list instead
    private bool _isRebuilding;

    // A role just created, to be chosen once the list has it; the list may come after the answer to the request
    private int? _pendingSelectionId;

    public ObservableCollection<RoleRowVM> Roles { get; } = new();

    public RoleRowVM? SelectedRole
    {
        get;
        set
        {
            var previousId = field?.Role.Id;
            this.RaiseAndSetIfChanged(ref field, value);

            // The same role chosen again keeps its edits
            if (_isRebuilding || value?.Role.Id == previousId)
                return;

            // Chosen by the user: a role created before no longer has to be chosen
            _pendingSelectionId = null;
            Editor.Load(value?.Role, _coordinatorSession.Permissions);
            Error = null;
        }
    }

    public RoleEditorVM Editor { get; } = new();

    public bool IsBusy => _isBusy.Value;

    /// <summary>Why the last change was not made; one reason.</summary>
    public string? Error
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool CanCreate => _coordinatorSession.Permissions.Has(Permission.ManageRoles);

    public ReactiveCommand<RxVoid, RxVoid> CreateCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RoleRowVM, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RoleRowVM, RxVoid> MoveUpCommand { get; }
    public ReactiveCommand<RoleRowVM, RxVoid> MoveDownCommand { get; }

    /// <summary>Takes the role as it was saved elsewhere, dropping the edits.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }

    /// <param name="showMessageBox">Asks before a role is deleted.</param>
    public RolesTabVM(ICoordinatorSession coordinatorSession, Interaction<MessageBoxVM, Result> showMessageBox)
    {
        _coordinatorSession = coordinatorSession;
        _showMessageBox = showMessageBox;

        CreateCommand = ReactiveCommand.CreateFromTask(CreateAsync);
        var canSave = Editor.WatchProperties(nameof(RoleEditorVM.IsDirty), nameof(RoleEditorVM.IsChangedElsewhere),
                nameof(RoleEditorVM.CanEdit), nameof(RoleEditorVM.NameError))
            .Select(editor => editor is { IsDirty: true, IsChangedElsewhere: false, CanEdit: true, NameError: null });
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canSave);
        DeleteCommand = ReactiveCommand.CreateFromTask<RoleRowVM>(DeleteAsync);
        MoveUpCommand = ReactiveCommand.CreateFromTask<RoleRowVM>(row => MoveAsync(row, 1));
        MoveDownCommand = ReactiveCommand.CreateFromTask<RoleRowVM>(row => MoveAsync(row, -1));
        ReloadCommand = ReactiveCommand.Create(() => Editor.Load(SelectedRole?.Role, _coordinatorSession.Permissions));

        _isBusy = Observable.CombineLatest(CreateCommand.IsExecuting, SaveCommand.IsExecuting,
                DeleteCommand.IsExecuting, MoveUpCommand.IsExecuting, MoveDownCommand.IsExecuting)
            .Select(states => states.Any(executing => executing))
            .ToProperty(this, nameof(IsBusy));

        Rebuild(_coordinatorSession.Permissions);
        _coordinatorSession.PermissionsChanged += OnPermissionsChanged;
    }

    // Raised on the transport thread
    private void OnPermissionsChanged(CoordinatorPermissions permissions) =>
        Dispatcher.UIThread.Post(() => Rebuild(permissions));

    /// <summary>Shows the roles as they are now, keeping the chosen one and the edits made to it.</summary>
    private void Rebuild(CoordinatorPermissions permissions)
    {
        // The editor, not the list box, knows which role was chosen: the list box may have dropped it already
        var selectedId = Editor.Role?.Id;
        var highestFirst = permissions.Roles.Reverse().ToList();
        var movable = highestFirst.Where(role => !role.IsEveryone).ToList();

        RoleRowVM? selected;
        _isRebuilding = true;
        try
        {
            Roles.Clear();
            foreach (var role in highestFirst)
            {
                var index = movable.IndexOf(role);
                var canManage = permissions.CanManage(role);
                Roles.Add(new RoleRowVM(this, role, canManage,
                    canMoveUp: !role.IsEveryone && canManage && index > 0 && permissions.CanManage(movable[index - 1]),
                    canMoveDown: !role.IsEveryone && canManage && index >= 0 && index < movable.Count - 1));
            }

            var pending = Roles.FirstOrDefault(row => row.Role.Id == _pendingSelectionId);
            if (pending != null)
                _pendingSelectionId = null;

            selected = pending
                       ?? Roles.FirstOrDefault(row => row.Role.Id == selectedId)
                       ?? Roles.FirstOrDefault();
            SelectedRole = selected;
        }
        finally
        {
            _isRebuilding = false;
        }

        this.RaisePropertyChanged(nameof(CanCreate));

        // Its edits stay; it is told when its role changed elsewhere meanwhile. Another role is loaded afresh
        if (selected?.Role.Id != selectedId)
            Error = null;
        Editor.Follow(selected?.Role, permissions);
    }

    private async Task CreateAsync()
    {
        Error = null;
        var name = Strings.Get("roles.newRoleName");
        var (result, role) = await _coordinatorSession.CreateRole(new RoleChange(name, null, Permission.None));
        Error = RoleResultMessages.Describe(result);

        if (role == null)
            return;

        // Chosen now if the list has it already, otherwise when it comes
        _pendingSelectionId = role.Id;
        Select(role.Id);
    }

    private async Task SaveAsync()
    {
        if (Editor.Role is not { } role)
            return;

        Error = null;
        var (result, saved) = await _coordinatorSession.UpdateRole(role.Id, Editor.ToChange(), role.Revision);

        if (result.Error == ErrorCode.Conflict)
        {
            Editor.IsChangedElsewhere = true;
            return;
        }

        Error = RoleResultMessages.Describe(result);
        if (saved != null)
            Editor.Load(saved, _coordinatorSession.Permissions);
    }

    private async Task DeleteAsync(RoleRowVM row)
    {
        if (!row.CanDelete)
            return;

        var role = row.Role;
        if (await _showMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.YesNo,
                Strings.Format("serverSettings.roles.delete.question", ("name", role.Name)),
                Strings.Get("serverSettings.roles.delete.title"))) != Result.Yes)
            return;

        Error = RoleResultMessages.Describe(await _coordinatorSession.DeleteRole(role.Id));
    }

    /// <param name="step">1 to rank the role higher, -1 lower.</param>
    private async Task MoveAsync(RoleRowVM row, int step)
    {
        // Lowest first, as the server keeps them
        var order = _coordinatorSession.Permissions.Roles.Where(role => !role.IsEveryone).Select(role => role.Id)
            .ToList();
        var from = order.IndexOf(row.Role.Id);
        var to = from + step;
        if (from < 0 || to < 0 || to >= order.Count)
            return;

        (order[from], order[to]) = (order[to], order[from]);

        Error = null;
        var (result, _) = await _coordinatorSession.ReorderRoles(order);
        Error = RoleResultMessages.Describe(result);
    }

    private void Select(int roleId)
    {
        if (Roles.FirstOrDefault(row => row.Role.Id == roleId) is { } row)
            SelectedRole = row;
    }

    public void Dispose()
    {
        _coordinatorSession.PermissionsChanged -= OnPermissionsChanged;
        _isBusy.Dispose();
        CreateCommand.Dispose();
        SaveCommand.Dispose();
        DeleteCommand.Dispose();
        MoveUpCommand.Dispose();
        MoveDownCommand.Dispose();
        ReloadCommand.Dispose();
    }
}

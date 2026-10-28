using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using Shared.Enums;
using Client.Avalonia.Localization;
using Client.Application.Coordinator;
using Client.Core.Coordinator;
using Client.Services;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Main;
using Result = Client.ViewModels.Dialogs.Result;

namespace Client.ViewModels.ServerSettings;

/// <summary>
/// The settings of the server we are connected to, laid out like the settings of the application:
/// tabs on the left, the page of the chosen one on the right. The settings the server has are loaded when the
/// dialog opens and saved all together; a change saved meanwhile elsewhere, in the control panel or by another
/// user, shows up here at once, or is announced while there are unsaved edits.
/// </summary>
public sealed class ServerSettingsVM : ReactiveObject, IDisposable
{
    private readonly ICoordinatorSession _coordinatorSession;
    private readonly ObservableAsPropertyHelper<bool> _isBusy;
    private readonly ObservableAsPropertyHelper<bool> _isLoading;
    private readonly IDisposable _formChanges;

    // The SFU nodes of the last load, which a save does not send back
    private IReadOnlyList<SfuNodeModel> _sfuNodes = [];

    // In the order of the tabs in the view
    private readonly IReadOnlyList<ReactiveObject> _tabs;

    public int SelectedTabIndex
    {
        get;
        set
        {
            // A list box reports no selection while its items are rebuilt
            if (value < 0 || value >= _tabs.Count)
                return;

            this.RaiseAndSetIfChanged(ref field, value);
            SelectedTab = _tabs[value];
        }
    }

    public ReactiveObject SelectedTab
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsRolesTab));
            this.RaisePropertyChanged(nameof(IsPageShown));
            this.RaisePropertyChanged(nameof(ShowsSettingsFooter));
            this.RaisePropertyChanged(nameof(CanEditPage));
            this.RaisePropertyChanged(nameof(IsReadOnly));
        }
    }

    public RolesTabVM Roles { get; }

    public ServerUsersTabVM Users { get; }

    /// <summary>The roles tab saves each change at once, without the footer of the settings.</summary>
    public bool IsRolesTab => SelectedTab is RolesTabVM;

    /// <summary>The roles of the current user let it see the settings; the settings tabs show only then.</summary>
    public bool CanViewSettings
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The settings are shown read-only without this.</summary>
    public bool CanEditSettings
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(ShowsSettingsFooter));
            this.RaisePropertyChanged(nameof(CanEditPage));
            this.RaisePropertyChanged(nameof(IsReadOnly));
        }
    }

    public bool CanManageRoles
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The page of the chosen tab has something to show.</summary>
    public bool IsPageShown => IsRolesTab || IsLoaded;

    public bool ShowsSettingsFooter => !IsRolesTab && CanEditSettings;

    /// <summary>
    /// The page may be changed: the roles, the users, whose page leaves alone what the roles do not allow, or the
    /// settings with the roles to change them.
    /// </summary>
    public bool CanEditPage => IsRolesTab || SelectedTab is ServerUsersTabVM || CanEditSettings;

    /// <summary>The settings are shown without the roles to change them.</summary>
    public bool IsReadOnly => !IsRolesTab && !CanEditSettings;

    /// <summary>
    /// The server these settings belong to.
    /// </summary>
    public ServerTitleVM Server { get; }

    public CoordinatorSettingsFormVM Form { get; } = new();

    /// <summary>The settings came from the server and can be edited.</summary>
    public bool IsLoaded
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsPageShown));
        }
    }

    public bool IsBusy => _isBusy.Value;

    public bool IsLoading => _isLoading.Value;

    /// <summary>
    /// Someone saved other settings while these had unsaved edits. They cannot be saved over them; the user
    /// loads the new ones instead.
    /// </summary>
    public bool IsChangedElsewhere
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>A failure to load or to save, shown above the buttons; one reason on a line.</summary>
    public string? GeneralError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Set after a save until the next edit.</summary>
    public bool IsSaved
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public Interaction<RxVoid, RxVoid> CloseWindow { get; } = new();

    /// <summary>
    /// Asks before a save that restarts the server and disconnects everyone, and before a role or a user is
    /// deleted or a user banned.
    /// </summary>
    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ResetCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    /// <param name="viewProfileCommand">Opens the profile of a user, where it can be edited.</param>
    public ServerSettingsVM(ICoordinatorSession coordinatorSession, ServerTitleVM server,
        ReactiveCommand<int, RxVoid> viewProfileCommand)
    {
        _coordinatorSession = coordinatorSession;
        Server = server;
        Roles = new RolesTabVM(coordinatorSession, ShowMessageBox);
        Users = new ServerUsersTabVM(Form, coordinatorSession, ShowMessageBox, viewProfileCommand);
        _tabs = [new ServerInfoTabVM(server, coordinatorSession.CoordinatorModel, Form), new NetworkTabVM(Form),
            new SfuNodesTabVM(Form), Users, new ServerSecurityTabVM(Form),
            new ServerStorageTabVM(Form), Roles];
        ApplyAccess(coordinatorSession.Permissions);
        // Without the settings the roles are all there is to show
        SelectedTabIndex = CanViewSettings ? 0 : _tabs.Count - 1;
        SelectedTab = _tabs[SelectedTabIndex];

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);

        var isDirty = Form.WatchProperty(nameof(Form.IsDirty), form => form.IsDirty);
        var canSave = this.WatchProperties(nameof(IsLoaded), nameof(IsChangedElsewhere))
            .CombineLatest(isDirty, LoadCommand.IsExecuting,
                (vm, dirty, loading) => vm.IsLoaded && dirty && !vm.IsChangedElsewhere && !loading)
            .CombineLatest(this.WatchProperty(nameof(CanEditSettings), vm => vm.CanEditSettings),
                (canSave, canEdit) => canSave && canEdit);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canSave);

        var isIdle = SaveCommand.IsExecuting.CombineLatest(LoadCommand.IsExecuting,
            (saving, loading) => !saving && !loading);
        ResetCommand = ReactiveCommand.Create(Reset, isIdle.CombineLatest(isDirty, (idle, dirty) => idle && dirty));
        CloseCommand = ReactiveCommand.CreateFromTask(async () => { await CloseWindow.Handle(RxVoid.Default); },
            SaveCommand.IsExecuting.Select(busy => !busy));

        _isBusy = SaveCommand.IsExecuting.ToProperty(this, nameof(IsBusy));
        _isLoading = LoadCommand.IsExecuting.ToProperty(this, nameof(IsLoading));

        // Saved shows until the next edit
        _formChanges = isDirty.Where(dirty => dirty).Subscribe(_ => IsSaved = false);

        _coordinatorSession.CoordinatorReconfigured += OnCoordinatorReconfigured;
        _coordinatorSession.PermissionsChanged += OnPermissionsChanged;
        if (CanViewSettings)
        {
            LoadCommand.Execute().Subscribe();
            Users.LoadCommand.Execute().Subscribe();
        }
    }

    // Raised on the transport thread
    private void OnPermissionsChanged(CoordinatorPermissions permissions) => Dispatcher.UIThread.Post(() =>
    {
        var couldView = CanViewSettings;
        ApplyAccess(permissions);
        if (CanViewSettings && !couldView)
        {
            LoadCommand.Execute().Subscribe();
            Users.LoadCommand.Execute().Subscribe();
        }
    });

    private void ApplyAccess(CoordinatorPermissions permissions)
    {
        CanViewSettings = permissions.Has(Permission.ViewCoordinatorSettings);
        CanEditSettings = permissions.Has(Permission.ManageCoordinatorSettings);
        CanManageRoles = permissions.Has(Permission.ManageRoles);
    }

    private async Task LoadAsync()
    {
        GeneralError = null;

        var (result, snapshot) = await _coordinatorSession.GetCoordinatorSettings();
        if (snapshot == null)
        {
            // Rejected is told by the main window
            if (!result.IsRejected)
                GeneralError = result.Error == ErrorCode.TimedOut
                    ? Strings.Get("serverSettings.error.timedOut")
                    : Strings.Format("serverSettings.error.notLoaded", ("reason", result));
            return;
        }

        _sfuNodes = snapshot.SfuNodes;
        Form.Load(snapshot.Settings, snapshot.SfuNodes);
        IsChangedElsewhere = false;
        IsLoaded = true;
    }

    private async Task SaveAsync()
    {
        if (Form.MovesEndpoint && await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.YesNo,
                Strings.Get("serverSettings.restart.question"), Strings.Get("serverSettings.restart.title"))) != Result.Yes)
            return;

        GeneralError = null;
        IsSaved = false;

        var outcome = await _coordinatorSession.UpdateCoordinatorSettings(Form.ToUpdate(), Form.Loaded!.Revision);

        switch (outcome)
        {
            case { IsSuccess: true }:
                Form.Load(outcome.Value, _sfuNodes);
                IsSaved = true;
                break;
            case { Error: ErrorCode.Conflict }:
                IsChangedElsewhere = true;
                break;
            case { Error: ErrorCode.InvalidValue }:
                GeneralError = string.Join(Environment.NewLine,
                    outcome.Fields?.Values.SelectMany(messages => messages) ?? []);
                break;
            case { Error: ErrorCode.TimedOut }:
                // A new endpoint restarts the coordinator, and the answer goes down with the connection
                GeneralError = Strings.Get(Form.MovesEndpoint
                    ? "serverSettings.error.restarting"
                    : "serverSettings.error.timedOut");
                break;
            // The main window tells why; the form stays filled in for another try
            case { IsRejected: true }:
                break;
            default:
                GeneralError = Strings.Format("serverSettings.error.notSaved", ("reason", outcome));
                break;
        }

        // Our own change is announced to us too, usually before the answer, and is skipped while saving;
        // a change of someone else that came meanwhile is caught up with here
        if (_coordinatorSession.CoordinatorModel is { } coordinator)
            Dispatcher.UIThread.Post(() => OnSettingsRevision(coordinator.SettingsRevision));
    }

    private void Reset()
    {
        if (Form.Loaded is { } loaded)
            Form.Load(loaded, _sfuNodes);
        GeneralError = null;
    }

    private void OnCoordinatorReconfigured(CoordinatorSessionModel coordinator) =>
        Dispatcher.UIThread.Post(() => OnSettingsRevision(coordinator.SettingsRevision));

    private void OnSettingsRevision(long revision)
    {
        if (!CanViewSettings || IsBusy || IsLoading || Form.Loaded is not { } loaded || revision <= loaded.Revision)
            return;

        if (Form.IsDirty)
            IsChangedElsewhere = true;
        else
            LoadCommand.Execute().Subscribe();
    }

    public void Dispose()
    {
        _coordinatorSession.CoordinatorReconfigured -= OnCoordinatorReconfigured;
        _coordinatorSession.PermissionsChanged -= OnPermissionsChanged;
        Roles.Dispose();
        Users.Dispose();
        _formChanges.Dispose();
        _isBusy.Dispose();
        _isLoading.Dispose();
        LoadCommand.Dispose();
        SaveCommand.Dispose();
        ResetCommand.Dispose();
        CloseCommand.Dispose();
    }
}

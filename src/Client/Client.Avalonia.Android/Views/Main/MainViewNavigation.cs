using Client.Droid.Views.Connections;
using Client.Droid.Views.Dialogs;
using Client.Droid.Views.Profile;
using Client.Droid.Views.Sessions.Create;
using Client.Droid.Views.Sessions.Hybrid;
using Client.Droid.Views.Settings;
using Client.Avalonia.Controls.Avatars;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Services;
using Client.ViewModels.Connections;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Profile;
using Client.ViewModels.Sessions.Chat;
using Client.ViewModels.Sessions.Create;
using Client.ViewModels.Sessions.Hybrid;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Droid.Views.Main;

/// <summary>Owns overlay screens, modal dialogs and their back navigation.</summary>
internal sealed class MainViewNavigation
{
    // The message boxes on screen, the topmost one last, each with what the back button answers it
    private readonly List<(TaskCompletionSource<Result> Answer, Result BackAnswer)> _messageBoxes = [];

    private ConnectView? _connectView;
    private ProfileView? _profileView;
    private Action? _cancelAvatarEdit;
    private AccountSecurityView? _securityView;
    private readonly List<LoginPromptVM> _loginPrompts = [];
    private SettingsView? _settingsView;
    private HybridSessionStatsPage? _hybridStatsPage;
    private CreateSessionView? _createSessionView;

    private readonly Control _owner;
    private readonly Panel _dialogLayer;
    private readonly Action _showCurrentScreen;
    private readonly Func<ActiveHybridSessionVM?> _currentHybrid;
    private readonly Func<IServiceProvider?> _getServices;
    private IServiceProvider? Services => _getServices();

    public MainViewNavigation(Control owner, Panel dialogLayer, Action showCurrentScreen,
        Func<ActiveHybridSessionVM?> currentHybrid, Func<IServiceProvider?> getServices)
    {
        _owner = owner;
        _dialogLayer = dialogLayer;
        _showCurrentScreen = showCurrentScreen;
        _currentHybrid = currentHybrid;
        _getServices = getServices;
    }

    public Control? GetOverlay()
    {
        // Statistics belong to the session that opened them.
        if (_hybridStatsPage != null && (_currentHybrid() is not ActiveHybridSessionVM currentHybrid
            || !ReferenceEquals(_hybridStatsPage.DataContext, currentHybrid.StatusBar)))
        {
            _hybridStatsPage.DataContext = null;
            _hybridStatsPage = null;
        }

        var overlay = (Control?)_connectView ?? (Control?)_securityView ?? (Control?)_profileView
                      ?? (Control?)_createSessionView ?? (Control?)_settingsView ?? _hybridStatsPage;
        return overlay;
    }

    public bool TryGoBack()
    {
        if (_cancelAvatarEdit != null)
        {
            _cancelAvatarEdit();
            return true;
        }

        if (_messageBoxes.Count > 0)
        {
            var (answer, backAnswer) = _messageBoxes[^1];
            answer.TrySetResult(backAnswer);
            return true;
        }

        if (_loginPrompts.Count > 0)
        {
            _loginPrompts[^1].CancelCommand.Execute().Subscribe();
            return true;
        }

        if (_connectView?.DataContext is ConnectWindowVM connect)
        {
            // While it connects the form stays: it closes itself or tells why it could not connect
            if (!connect.IsBusy)
                connect.CloseCommand.Execute().Subscribe();

            return true;
        }

        if (_securityView?.DataContext is AccountSecurityVM security)
        {
            // Back leaves a change first, then the screen; a change being made is waited for
            if (!security.IsBusy)
                (security.IsChanging ? security.BackCommand : security.CloseCommand).Execute().Subscribe();

            return true;
        }

        if (_profileView?.DataContext is UserProfileWindowVM profile)
        {
            // Back leaves the editing first, then the profile; a name being saved is waited for
            if (!profile.IsBusy)
                (profile.IsEditing ? profile.CancelEditCommand : profile.CloseCommand).Execute().Subscribe();

            return true;
        }

        if (_createSessionView?.DataContext is CreateSessionWindowVM createSession)
        {
            // A session being created is waited for: the form closes itself or tells why it could not
            if (!createSession.IsBusy)
                createSession.CloseCommand.Execute().Subscribe();

            return true;
        }

        if (_settingsView != null)
        {
            if (!_settingsView.TryGoBack())
                CloseSettings();

            return true;
        }

        if (_hybridStatsPage != null)
        {
            CloseHybridStats();
            return true;
        }

        return false;
    }

    public void OpenHybridStats()
    {
        if (_hybridStatsPage != null || _currentHybrid() is not ActiveHybridSessionVM hybrid)
            return;

        _hybridStatsPage = new HybridSessionStatsPage { DataContext = hybrid.StatusBar };
        _hybridStatsPage.CloseRequested += CloseHybridStats;
        _showCurrentScreen();
    }

    private void CloseHybridStats()
    {
        if (_hybridStatsPage == null)
            return;

        _hybridStatsPage.DataContext = null;
        _hybridStatsPage = null;
        _showCurrentScreen();
    }

    // The settings are built anew each time, so they open on what is saved
    public void OpenSettings()
    {
        var services = Services;
        if (_settingsView != null || services == null)
            return;

        _settingsView = new SettingsView(services, ShowMessageBoxAsync);
        _settingsView.CloseRequested += CloseSettings;
        _showCurrentScreen();
    }

    private void CloseSettings()
    {
        _settingsView?.Release();
        _settingsView = null;
        _showCurrentScreen();
    }

    /// <summary>
    /// Shows the connect form until it is closed: with the session it connected, or with nothing.
    /// </summary>
    public async Task<ICoordinatorSession?> ShowConnectAsync(ConnectWindowVM connect)
    {
        // The form is taken off the screen after the view model is done with closing it
        var closed = new TaskCompletionSource<ICoordinatorSession?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var close = connect.CloseWindow.RegisterHandler(context =>
        {
            closed.TrySetResult(context.Input);
            context.SetOutput(RxVoid.Default);
        });
        using var messageBoxes = connect.ShowMessageBox.RegisterHandler(async context =>
            context.SetOutput(await ShowMessageBoxAsync(context.Input)));
        using var loginPrompts = connect.AskLogin.RegisterHandler(async context =>
            context.SetOutput(await ShowLoginPromptAsync(context.Input)));

        _connectView = new ConnectView { DataContext = connect };
        _showCurrentScreen();

        try
        {
            return await closed.Task;
        }
        finally
        {
            _connectView = null;
            _showCurrentScreen();

            // The form is gone, however it was closed: stop connecting in the background
            connect.CancelTasks();
        }
    }

    /// <summary>
    /// Shows the profile of a user over the screen it was asked from, until it is closed.
    /// </summary>
    public async Task ShowProfileAsync(UserProfileWindowVM profile)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new ProfileView { DataContext = profile };

        using var close = profile.CloseWindow.RegisterHandler(context =>
        {
            closed.TrySetResult();
            context.SetOutput(RxVoid.Default);
        });
        using var focus = profile.FocusName.RegisterHandler(context =>
        {
            view.FocusNameInput();
            context.SetOutput(RxVoid.Default);
        });
        using var pickAvatar = profile.AvatarPicker.PickAvatarFile.RegisterHandler(async context =>
        {
            var storage = TopLevel.GetTopLevel(_owner)?.StorageProvider;
            var files = storage == null
                ? []
                : await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Strings.Get("profile.avatar.pickerTitle"),
                    AllowMultiple = false,
                    FileTypeFilter = [PickedFiles.ImageFileType(Strings.Get("profile.avatar.fileType"))]
                });

            context.SetOutput((await PickedFiles.FromAsync(files)).FirstOrDefault());
        });

        using var editAvatar = profile.AvatarPicker.EditAvatar.RegisterHandler(async context =>
        {
            var session = context.Input;
            var editor = new AvatarEditor();
            editor.SetPicture(session.Picture);
            var answer = new TaskCompletionSource<PreparedAvatar?>(TaskCreationOptions.RunContinuationsAsynchronously);
            editor.TransformRequested += mirror =>
            {
                session.Transform(mirror);
                editor.SetPicture(session.Picture);
            };
            editor.ResetRequested += () =>
            {
                session.Reset();
                editor.SetPicture(session.Picture);
            };
            editor.Accepted += () => answer.TrySetResult(session.Prepare(editor.Crop));
            editor.Cancelled += () => answer.TrySetResult(null);
            var backdrop = new Border
            {
                Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromArgb(180, 0, 0, 0)),
                Child = new Border
                {
                    Background = view.FindResource("GeneralBrush") as global::Avalonia.Media.IBrush,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(16),
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Child = editor
                }
            };
            _cancelAvatarEdit = () => answer.TrySetResult(null);
            _dialogLayer.Children.Add(backdrop);
            try
            {
                context.SetOutput(await answer.Task);
            }
            finally
            {
                _dialogLayer.Children.Remove(backdrop);
                _cancelAvatarEdit = null;
            }
        });

        _profileView = view;
        _showCurrentScreen();

        try
        {
            await closed.Task;
        }
        finally
        {
            _cancelAvatarEdit?.Invoke();
            _profileView = null;
            _showCurrentScreen();
        }
    }

    /// <summary>
    /// Shows the form of a new session until it is closed: by the user, or by itself once the session is created.
    /// </summary>
    public async Task ShowCreateSessionAsync(CreateSessionWindowVM createSession)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var close = createSession.CloseWindow.RegisterHandler(context =>
        {
            closed.TrySetResult();
            context.SetOutput(RxVoid.Default);
        });

        _createSessionView = new CreateSessionView { DataContext = createSession };
        _showCurrentScreen();

        try
        {
            if (createSession.IsPrivateMode)
                await createSession.InitializeMembersAsync();
            await closed.Task;
        }
        finally
        {
            _createSessionView = null;
            _showCurrentScreen();
        }
    }

    /// <summary>
    /// Shows what the user logs in with, opened from the menu of the server, until it is closed.
    /// </summary>
    public async Task ShowAccountSecurityAsync(AccountSecurityVM security)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var close = security.CloseWindow.RegisterHandler(context =>
        {
            closed.TrySetResult();
            context.SetOutput(RxVoid.Default);
        });

        _securityView = new AccountSecurityView { DataContext = security };
        _showCurrentScreen();

        try
        {
            await closed.Task;
        }
        finally
        {
            _securityView = null;
            _showCurrentScreen();
        }
    }

    /// <summary>Asks for what finishes a login over everything else, like a message box.</summary>
    public async Task<LoginAnswer?> ShowLoginPromptAsync(LoginPromptVM prompt)
    {
        var view = new LoginPromptView { DataContext = prompt };
        var answer = new TaskCompletionSource<LoginAnswer?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var answers = prompt.Answers.Subscribe(given => answer.TrySetResult(given));

        _loginPrompts.Add(prompt);
        _dialogLayer.Children.Add(view);

        try
        {
            return await answer.Task;
        }
        finally
        {
            _dialogLayer.Children.Remove(view);
            _loginPrompts.Remove(prompt);
        }
    }

    public Task<Result> ShowMessageBoxAsync(MessageBoxVM messageBox) => ShowMessageBoxAsync(messageBox, Result.Cancel);

    /// <param name="backAnswer">What the system back button answers while the box is the topmost one.</param>
    public async Task<Result> ShowMessageBoxAsync(MessageBoxVM messageBox, Result backAnswer)
    {
        var view = new MessageBoxView { DataContext = messageBox };
        var answer = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var buttons = messageBox.Answers.Subscribe(result => answer.TrySetResult(result));

        _messageBoxes.Add((answer, backAnswer));
        _dialogLayer.Children.Add(view);

        try
        {
            return await answer.Task;
        }
        finally
        {
            _dialogLayer.Children.Remove(view);
            _messageBoxes.RemoveAll(box => ReferenceEquals(box.Answer, answer));
        }
    }

}

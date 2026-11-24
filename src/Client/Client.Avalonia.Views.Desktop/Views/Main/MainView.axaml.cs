using System;
using System.Threading.Tasks;
using Client.Services;
using Avalonia.Controls;
using Avalonia.Input;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using Client.Application.Coordinator;
using Client.Application.Sounds;
using Client.Core.Push;
using Client.Views.Connections;
using Client.Views.Dialogs;
using Client.Views.Profile;
using Client.Views.ServerSettings;
using Client.Views.Sessions.Create;
using Client.Views.Sessions.Chat;
using Client.Views.Sessions.Hybrid;
using Client.Views.Settings;
using Client.Views.Common;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Views.Main;

/// <summary>
/// Everything the application shows, with the dialogs its view model asks for. It knows nothing of
/// the window around it, so that it can be the whole page where there are no windows.
/// </summary>
public partial class MainView : ViewControl<MainWindowVM>
{
    /// <summary>
    /// Plays the sound of a message that came to a chat. Without it the messages come silently.
    /// </summary>
    public ISoundService? Sounds { get; set; }

    public MainView()
    {
        InitializeComponent();
        if (Design.IsDesignMode)
            return;

        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowConnectWindow.RegisterHandler(async interaction =>
            {
                interaction.SetOutput(await InWindowDialog.ShowAsync<ICoordinatorSession?>(
                    new ConnectView { DataContext = interaction.Input }, this, null));
            }).DisposeWith(disposables);

            ViewModel!.ShowSettings
                .RegisterDialogHandler(this, () => new SettingsView(), fillWindow: true)
                .DisposeWith(disposables);
            ViewModel!.ShowServerSettings
                .RegisterDialogHandler(this, () => new ServerSettingsView(), fillWindow: true)
                .DisposeWith(disposables);
            ViewModel!.ShowCreateSessionWindow
                .RegisterDialogHandler(this, () => new CreateSessionView())
                .DisposeWith(disposables);
            ViewModel!.ShowEditProfileWindow
                .RegisterDialogHandler(this, () => new EditProfileView())
                .DisposeWith(disposables);
            ViewModel!.ShowUserProfileWindow
                .RegisterDialogHandler(this, () => new UserProfileView())
                .DisposeWith(disposables);
            ViewModel!.ShowAccountSecurityWindow
                .RegisterDialogHandler(this, () => new AccountSecurityView())
                .DisposeWith(disposables);
            ViewModel!.AskLogin.RegisterLoginPromptHandler(this).DisposeWith(disposables);
            ViewModel!.ShowChatSessionSettingsWindow
                .RegisterDialogHandler(this, () => new ChatSessionSettingsView())
                .DisposeWith(disposables);

            ViewModel!.ShowHybridSessionSettingsWindow
                .RegisterDialogHandler(this, () => new HybridSessionSettingsView())
                .DisposeWith(disposables);
            ViewModel!.ShowSessionPermissionsWindow
                .RegisterDialogHandler(this, () => new Client.Views.Sessions.Permissions.SessionPermissionsView())
                .DisposeWith(disposables);

            ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
            ViewModel!.AskDatabasePassword.RegisterDatabasePasswordHandler(this).DisposeWith(disposables);

            // Content shown fullscreen takes the place of the side panel too
            ObservableExtensions.Subscribe(ViewModel!.WatchProperty(nameof(MainWindowVM.IsContentFullScreen), vm => vm.IsContentFullScreen),
                ApplyContentFullScreen).DisposeWith(disposables);

            // The hybrid session runs on while a chat is shown, with its controls in the side panel,
            // so its dialogs belong to the main view and not to the view of the session
            var hybridDialogs = new System.Reactive.Disposables.SerialDisposable().DisposeWith(disposables);
            ObservableExtensions.Subscribe(ViewModel!.WatchProperty(nameof(MainWindowVM.HybridSession), vm => vm.HybridSession),
                session => hybridDialogs.Disposable = session == null ? null : RegisterHybridDialogs(session))
                .DisposeWith(disposables);

            var viewModel = ViewModel!;
            viewModel.ChatMessageReceived += OnChatMessageReceived;
            System.Reactive.Disposables.Disposable.Create(() => viewModel.ChatMessageReceived -= OnChatMessageReceived)
                .DisposeWith(disposables);
        });
    }

    /// <summary>
    /// Loads the application under the startup screen. <paramref name="show"/> makes the view visible,
    /// for prompts that come before the application is loaded.
    /// </summary>
    public async Task InitializeBeforeShowAsync(Action show)
    {
        var viewModel = ViewModel!;

        // Startup interactions must work before view activation. A password or recovery prompt
        // needs a visible owner even though the saved appearance cannot yet be read.
        using var messages = viewModel.ShowMessageBox.RegisterHandler(async context =>
        {
            show();
            context.SetOutput(await MessageBoxInteraction.ShowAsync(context.Input, this));
        });
        using var restart = viewModel.RestartWithoutMedia.RegisterHandler(context =>
        {
            DesktopHost.Current?.RestartWithoutMedia();
            context.SetOutput(default(RxVoid));
        });
        using var passwords = viewModel.AskDatabasePassword.RegisterHandler(async context =>
        {
            show();
            context.SetOutput(await DatabasePasswordInteraction.ShowAsync(context.Input, this));
        });

        await viewModel.Initialize(show, () => StartupOverlay.FadeOutAsync(MainInterface));
    }

    // The message is of somebody else and its chat is not muted. The sound is made also for the
    // chat the user is looking at
    private void OnChatMessageReceived(PushPayload message) =>
        Sounds?.Play(message.Mention ? SoundEvent.Mention : SoundEvent.Message);

    private MultipleDisposable RegisterHybridDialogs(ActiveHybridSessionVM session) => new()
    {
        session.ShowMessageBox.RegisterMessageBoxHandler(this),
        session.SelectSources.RegisterHandler(async interaction =>
        {
            interaction.SetOutput(await InWindowDialog.ShowAsync<ScreenShareRequest?>(
                new HybridSourceSelectView(interaction.Input), this, null));
        })
    };

    private void ApplyContentFullScreen(bool isFullScreen)
    {
        Grid.SetColumn(MainContentControl, isFullScreen ? 0 : 2);
        Grid.SetColumnSpan(MainContentControl, isFullScreen ? 3 : 1);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || e.Key != Key.Escape || ViewModel is not { IsContentFullScreen: true } viewModel)
            return;

        viewModel.ExitFullScreen();
        e.Handled = true;
    }
}

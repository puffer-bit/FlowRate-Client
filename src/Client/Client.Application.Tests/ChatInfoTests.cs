using System.Reflection;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Sessions;
using Client.ViewModels.Sessions.Chat;
using Client.ViewModels.Sessions.Create;
using Shared.Enums;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatInfoTests
{
    private static ChatSessionModel Chat(string? description, string name = "General") => new()
    {
        Id = 7, Name = name, Capacity = null, SessionType = SessionType.Chat,
        CreatorUserId = 1, Description = description
    };

    [Fact]
    public Task InfoPreservesMultilineDescriptionAndShowsEmptyState() => TestUiThread.RunAsync(() =>
    {
        using var info = new ChatInfoWindowVM(Chat("  First line\nSecond line  "));
        Assert.Equal("General", info.Name);
        Assert.Equal("First line\nSecond line", info.Description);
        using var empty = new ChatInfoWindowVM(Chat(" \n "));
        Assert.Equal(Strings.Get("chat.info.noDescription"), empty.Description);
    });

    [Fact]
    public Task InfoUsesLatestSessionDetails() => TestUiThread.RunAsync(() =>
    {
        using var status = new ChatStatusBarVM(Chat("Old description"));
        status.ApplySession(Chat("New description", "Renamed"));
        Assert.Equal("Renamed", status.Name);
        Assert.Equal("New description", status.StatusText);
        ChatInfoWindowVM? shown = null;
        using var handler = status.ShowInfo.RegisterHandler(context =>
        {
            shown = context.Input;
            context.SetOutput(ReactiveUI.Primitives.RxVoid.Default);
        });
        ((ICommand)status.OpenInfoCommand).Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(shown);
        Assert.Equal("Renamed", shown.Name);
        Assert.Equal("New description", shown.Description);
    });

    [Fact]
    public async Task HeaderStaysEnabledWhileInfoIsOpenAndDoesNotOpenDuplicates()
    {
        using var status = new ChatStatusBarVM(Chat("Description"));
        using var closed = new ReplaySubject<ReactiveUI.Primitives.RxVoid>(1);
        var opened = 0;
        using var handler = status.ShowInfo.RegisterHandler(context =>
        {
            opened++;
            return closed.Do(_ => context.SetOutput(ReactiveUI.Primitives.RxVoid.Default));
        });
        var command = (ICommand)status.OpenInfoCommand;

        async Task ClickHeader()
        {
            Task? execution = null;
            await TestUiThread.RunAsync(() => execution = status.OpenInfoCommand.Execute().ToTask());
            await execution!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await status.OpenInfoCommand.CanExecute.FirstAsync(enabled => enabled).ToTask()
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        await ClickHeader();
        Assert.Equal(1, opened);
        Assert.True(command.CanExecute(null));

        await ClickHeader();
        Assert.Equal(1, opened);
        Assert.True(command.CanExecute(null));

        await TestUiThread.RunAsync(() =>
        {
            closed.OnNext(ReactiveUI.Primitives.RxVoid.Default);
            closed.OnCompleted();
            Dispatcher.UIThread.RunJobs();
        });
        Assert.True(command.CanExecute(null));
        await ClickHeader();
        Assert.Equal(2, opened);
    }

    [Fact]
    public Task ChatCreationDoesNotRequireCapacityAndHybridStillDoes() => TestUiThread.RunAsync(() =>
    {
        var session = DispatchProxy.Create<ICoordinatorSession, UnusedSession>();
        using var form = new CreateSessionWindowVM(session, SessionType.Chat) { Name = "General", Capacity = null };
        Dispatcher.UIThread.RunJobs();
        Assert.Null(form.CapacityError);
        Assert.True(((ICommand)form.CreateCommand).CanExecute(null));

        ((ICommand)form.SelectTypeCommand).Execute(form.HybridOption);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(form.CapacityError);
        Assert.False(((ICommand)form.CreateCommand).CanExecute(null));

        form.Capacity = 12;
        ((ICommand)form.SelectTypeCommand).Execute(form.ChatOption);
        ((ICommand)form.SelectTypeCommand).Execute(form.HybridOption);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(12m, form.Capacity);
        Assert.True(((ICommand)form.CreateCommand).CanExecute(null));
    });

    public class UnusedSession : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new InvalidOperationException(method!.Name);
    }
}

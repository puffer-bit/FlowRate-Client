using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.ViewModels.Sessions.Chat;
using ReactiveUI;
using Shared.Enums;
using Xunit;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Application.Tests;

public sealed class ChatSessionSettingsTests
{
    private static ChatSessionModel Chat(string? description = null) => new()
    {
        Id = 7, Name = "General", SessionType = SessionType.Chat, CreatorUserId = 1, Description = description
    };

    [Fact]
    public Task SavesTrimmedNameAndClosesAfterSuccess() => RunOnUiThread(async () =>
    {
        (int SessionId, string Name, string? Description)? sent = null;
        using var change = ReactiveCommand.Create<(int SessionId, string Name, string? Description), RequestResult>(value =>
        {
            sent = value;
            return RequestResult.Success;
        });
        using var vm = new ChatSessionSettingsWindowVM(Chat(), change);
        var closed = false;
        using var handler = vm.CloseWindow.RegisterHandler(context =>
        {
            closed = true;
            context.SetOutput(RxVoid.Default);
        });
        vm.Name = "  Announcements  ";
        await vm.SaveCommand.Execute().ToTask();
        Assert.Equal((7, "Announcements", (string?)null), sent);
        Assert.True(closed);
    });

    [Fact]
    public Task RejectsInvalidNamesAndShowsDuplicateError() => RunOnUiThread(async () =>
    {
        using var change = ReactiveCommand.Create<(int SessionId, string Name, string? Description), RequestResult>(_ => ErrorCode.NameTaken);
        using var vm = new ChatSessionSettingsWindowVM(Chat(), change);
        Assert.False(await vm.SaveCommand.CanExecute.FirstAsync());
        vm.Name = "  ";
        Assert.NotNull(vm.NameError);
        vm.Name = "Bad\nName";
        Assert.NotNull(vm.NameError);
        vm.Name = "Announcements";
        Assert.Null(vm.NameError);
        await vm.SaveCommand.Execute().ToTask();
        Assert.Equal(Strings.Get("createSession.name.taken"), vm.NameError);
        vm.Name = "Another";
        Assert.Null(vm.NameError);
    });
    [Fact]
    public Task SavesDescriptionWithoutRenamingAndAllowsClearingIt() => RunOnUiThread(async () =>
    {
        (int SessionId, string Name, string? Description)? sent = null;
        using var change = ReactiveCommand.Create<(int SessionId, string Name, string? Description), RequestResult>(value =>
        {
            sent = value;
            return RequestResult.Success;
        });
        var chat = Chat("Original");
        using var vm = new ChatSessionSettingsWindowVM(chat, change);
        using var handler = vm.CloseWindow.RegisterHandler(context => context.SetOutput(RxVoid.Default));
        Assert.Equal("Original", vm.Description);
        vm.Description = "  First line\nSecond line  ";
        Assert.True(await vm.SaveCommand.CanExecute.FirstAsync());
        await vm.SaveCommand.Execute().ToTask();
        Assert.Equal((7, "General", "First line\nSecond line"), sent);
        vm.Description = "   ";
        await vm.SaveCommand.Execute().ToTask();
        Assert.Equal((7, "General", (string?)null), sent);
        vm.Description = new string('a', vm.DescriptionMaxLength + 1);
        Assert.NotNull(vm.DescriptionError);
        Assert.False(await vm.SaveCommand.CanExecute.FirstAsync());
    });

    private static async Task RunOnUiThread(Func<Task> action)
    {
        Task? execution = null;
        await TestUiThread.RunAsync(() => execution = action());
        await execution!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

}

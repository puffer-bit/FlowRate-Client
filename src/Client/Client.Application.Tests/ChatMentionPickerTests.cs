using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Core.Users;
using Client.ViewModels.Sessions.Chat;
using Xunit;

namespace Client.Application.Tests;

public class ChatMentionPickerTests
{
    [Fact]
    public Task RefreshReusesRowsAndPreservesSelectionWithoutVisualStateChanges() => TestUiThread.RunAsync(() =>
    {
        var (picker, search) = CreatePicker();
        picker.Update("@a", 2);
        WaitUntil(() => search.Requests.Count == 1);
        search.Requests[0].Result.SetResult([User(2, "Andy"), User(3, "Ann")]);
        WaitUntil(() => picker.IsOpen);
        picker.MoveSelection(1);
        var selected = picker.Suggestions[1];
        int collectionChanges = 0;
        int selectionChanges = 0;
        picker.Suggestions.CollectionChanged += (_, _) => collectionChanges++;
        selected.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(selected.IsSelected)) selectionChanges++;
        };

        picker.Update("@an", 3);
        Assert.True(selected.IsSelected);
        WaitUntil(() => search.Requests.Count == 2);
        search.Requests[1].Result.SetResult([User(2, "Andy"), User(3, "Ann")]);
        WaitUntil(() => !picker.IsSearching);
        Assert.Equal(0, collectionChanges);
        Assert.Equal(0, selectionChanges);
        Assert.Same(selected, picker.Suggestions[picker.SelectedIndex]);

        picker.Update("@ann", 4);
        WaitUntil(() => search.Requests.Count == 3);
        search.Requests[2].Result.SetResult([new UserModel
        {
            Id = 3, Username = "Anne", CoordinatorInstanceId = "server", AvatarUrl = "new-avatar"
        }]);
        WaitUntil(() => !picker.IsSearching);
        Assert.Same(selected, Assert.Single(picker.Suggestions));
        Assert.Equal("Anne", selected.Name);
        Assert.Equal("new-avatar", selected.AvatarUrl);
        Assert.Equal(0, picker.SelectedIndex);
        Assert.True(selected.IsSelected);
        Assert.Equal(0, selectionChanges);
        picker.Close();
    });

    [Fact]
    public Task RefreshKeepsPopupOpenAndPreventsPickingPreviousSuggestions() => TestUiThread.RunAsync(() =>
    {
        var (picker, search) = CreatePicker();
        picker.Update("@Ann", 4);
        WaitUntil(() => search.Requests.Count == 1);
        search.Requests[0].Result.SetResult([User(2, "Ann")]);
        WaitUntil(() => picker.IsOpen);

        var oldSuggestion = Assert.Single(picker.Suggestions);
        var openChanges = 0;
        picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(picker.IsOpen)) openChanges++;
        };
        picker.Update("@Bob", 4);

        Assert.True(picker.IsOpen);
        Assert.True(picker.IsSearching);
        Assert.Same(oldSuggestion, Assert.Single(picker.Suggestions));
        Assert.Null(picker.Complete("@Bob", 4, oldSuggestion));
        picker.MoveSelection(1);
        Assert.Equal(0, picker.SelectedIndex);
        WaitUntil(() => search.Requests.Count == 2);
        search.Requests[1].Result.SetResult([User(3, "Bob")]);
        WaitUntil(() => !picker.IsSearching);
        Assert.True(picker.IsOpen);
        Assert.Equal("Bob", Assert.Single(picker.Suggestions).Name);
        Assert.Equal(0, openChanges);
        picker.Close();
    });

    [Fact]
    public Task ClosedPickerStaysClosedWhenPendingSearchCompletes() => TestUiThread.RunAsync(() =>
    {
        var (picker, search) = CreatePicker();
        picker.Update("@Ann", 4);
        WaitUntil(() => search.Requests.Count == 1);
        picker.Close();
        search.Requests[0].Result.SetResult([User(2, "Ann")]);

        PumpFor(TimeSpan.FromMilliseconds(100));
        Assert.False(picker.IsOpen);
        Assert.False(picker.IsSearching);
        Assert.Empty(picker.Suggestions);
    });

    [Fact]
    public Task OlderSearchCannotReplaceNewerResults() => TestUiThread.RunAsync(() =>
    {
        var (picker, search) = CreatePicker();
        picker.Update("@Ann", 4);
        WaitUntil(() => search.Requests.Count == 1);
        picker.Update("@Bob", 4);
        WaitUntil(() => search.Requests.Count == 2);
        search.Requests[1].Result.SetResult([User(3, "Bob")]);
        WaitUntil(() => picker.IsOpen);
        search.Requests[0].Result.SetResult([User(2, "Ann")]);
        PumpFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal("Bob", Assert.Single(picker.Suggestions).Name);
        picker.Close();
        picker.Close();
    });

    [Fact]
    public Task CompletionValidatesCaretAndKeepsMentionIdentity() => TestUiThread.RunAsync(() =>
    {
        var (picker, search) = CreatePicker();
        picker.Update("Hi @An", 6);
        WaitUntil(() => search.Requests.Count == 1);
        search.Requests[0].Result.SetResult([User(1, "Andy"), User(2, "Ann")]);
        WaitUntil(() => picker.IsOpen);

        Assert.Null(picker.Complete("Hi @Bob", 7));
        Assert.Null(picker.Complete("Hi @An", 2));
        Assert.Equal(("Hi @Ann ", 8), picker.Complete("Hi @An", 6)!.Value);
        Assert.Equal("Hi <@2> ", picker.Encode("Hi @Ann "));
        Assert.False(picker.IsOpen);
    });

    [Fact]
    public Task SlashOffersCommandsAndCompletesRollWithDefaultRangeWithoutSearchingUsers() => TestUiThread.RunAsync(() =>
    {
        var (picker, search) = CreatePicker();
        picker.Update("/", 1);
        Assert.True(picker.IsOpen);
        Assert.True(picker.ShowRollHint);
        Assert.Collection(picker.Suggestions,
            roll => Assert.Equal("/roll 1-100", roll.DisplayName),
            flip => Assert.Equal("/flip", flip.DisplayName));
        Assert.All(picker.Suggestions, suggestion => Assert.True(suggestion.IsCommand));
        Assert.False(picker.ShowFlipHint);
        Assert.Equal(("/roll 1-100", 11), picker.Complete("/", 1)!.Value);
        Assert.Equal("/roll 1-100", picker.Encode("/roll 1-100"));
        Assert.Empty(search.Requests);
    });

    [Fact]
    public Task CommandHelpFollowsKeyboardAndPointerSelection() => TestUiThread.RunAsync(() =>
    {
        var (picker, _) = CreatePicker();
        picker.Update("/", 1);
        Assert.True(picker.ShowRollHint);
        Assert.False(picker.ShowFlipHint);
        picker.MoveSelection(1);
        Assert.False(picker.ShowRollHint);
        Assert.True(picker.ShowFlipHint);
        picker.SelectSuggestion(picker.Suggestions[0]);
        Assert.True(picker.ShowRollHint);
        Assert.False(picker.ShowFlipHint);
        picker.Close();
    });

    [Theory]
    [InlineData("/f")]
    [InlineData("/flip")]
    [InlineData("/FLI")]
    public Task FlipCompletesWithoutRollArguments(string text) => TestUiThread.RunAsync(() =>
    {
        var (picker, search) = CreatePicker();
        picker.Update(text, text.Length);
        Assert.Equal("/flip", Assert.Single(picker.Suggestions).DisplayName);
        Assert.True(picker.ShowFlipHint);
        Assert.False(picker.ShowRollHint);
        var completed = picker.Complete(text, text.Length)!.Value;
        Assert.Equal(("/flip ", 6), completed);
        Assert.Equal("/flip ", picker.Encode(completed.Text));
        picker.Update(completed.Text, completed.CaretIndex);
        Assert.True(picker.ShowFlipHint);
        Assert.Empty(picker.Suggestions);
        Assert.Null(picker.Complete(completed.Text, completed.CaretIndex));
        picker.Close();
        Assert.False(picker.ShowFlipHint);
        Assert.Empty(search.Requests);
    });

    [Theory]
    [InlineData("/roll ")]
    [InlineData("/roll 1-")]
    [InlineData("/roll 1-1000")]
    [InlineData("/ROLL 10-20")]
    public Task RollArgumentsKeepHelpWithoutOfferingCompletion(string text) => TestUiThread.RunAsync(() =>
    {
        var (picker, search) = CreatePicker();
        picker.Update(text, text.Length);
        Assert.True(picker.IsOpen);
        Assert.True(picker.ShowRollHint);
        Assert.Empty(picker.Suggestions);
        Assert.Null(picker.Complete(text, text.Length));
        Assert.Empty(search.Requests);
        picker.Close();
        Assert.False(picker.ShowRollHint);
    });

    [Theory]
    [InlineData("/unknown")]
    [InlineData("Hi /roll")]
    [InlineData("https://roll")]
    [InlineData("/roll\ntext")]
    public Task OtherTextDoesNotOfferCommands(string text) => TestUiThread.RunAsync(() =>
    {
        var (picker, _) = CreatePicker();
        picker.Update(text, text.Length);
        Assert.False(picker.IsOpen);
        Assert.False(picker.ShowRollHint);
        Assert.Empty(picker.Suggestions);
    });

    [Fact]
    public Task SwitchingToCommandsCancelsMentionSearch() => TestUiThread.RunAsync(() =>
    {
        var (picker, search) = CreatePicker();
        picker.Update("@r", 2);
        WaitUntil(() => search.Requests.Count == 1);
        picker.Update("/r", 2);
        Assert.True(Assert.Single(picker.Suggestions).IsCommand);
        search.Requests[0].Result.SetResult([User(2, "Robin")]);
        PumpFor(TimeSpan.FromMilliseconds(100));
        Assert.True(Assert.Single(picker.Suggestions).IsCommand);
        Assert.False(picker.IsSearching);
        Assert.Null(picker.Complete("@r", 2));
        Assert.Equal(("/roll 5-10", 6), picker.Complete("/r 5-10", 2)!.Value);
    });

    private static (ChatMentionPickerVM Picker, SearchSession Search) CreatePicker()
    {
        var session = DispatchProxy.Create<ICoordinatorSession, SearchSession>();
        return (new ChatMentionPickerVM(session, 1), (SearchSession)(object)session);
    }

    private static UserModel User(int id, string name) => new()
    {
        Id = id, Username = name, CoordinatorInstanceId = "server"
    };

    private static void WaitUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), "Mention search timed out.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static void PumpFor(TimeSpan duration)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < duration)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    public class SearchSession : DispatchProxy
    {
        private readonly ConcurrentQueue<(string Query, TaskCompletionSource<IReadOnlyList<UserModel>> Result)> _requests = new();
        public IReadOnlyList<(string Query, TaskCompletionSource<IReadOnlyList<UserModel>> Result)> Requests => _requests.ToArray();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != "SearchUsers") throw new NotSupportedException(method.Name);
            var result = new TaskCompletionSource<IReadOnlyList<UserModel>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.Enqueue(((string)args![0]!, result));
            return result.Task;
        }
    }
}

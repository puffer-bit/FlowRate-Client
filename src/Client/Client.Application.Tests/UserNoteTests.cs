using System.Reactive.Linq;
using Client.Application.Ports.Repositories;
using Client.ViewModels.Profile;
using Xunit;

namespace Client.Application.Tests;

public class UserNoteTests
{
    [Fact]
    public async Task LoadsAndSavesUsingTheOriginalCoordinatorAndUser()
    {
        var repository = new Notes { Text = "Existing note" };
        using var note = new UserNoteVM(repository, "coordinator-instance", 42);
        Assert.False(await note.SaveCommand.CanExecute.FirstAsync());
        await note.LoadCommand.Execute();
        Assert.True(note.IsLoaded);
        Assert.Equal("Existing note", note.Text);
        Assert.False(await note.SaveCommand.CanExecute.FirstAsync());

        note.Text = "Updated\nMultiline note";
        Assert.True(await note.SaveCommand.CanExecute.FirstAsync());
        await note.SaveCommand.Execute();

        Assert.Equal(("coordinator-instance", 42, "Updated\nMultiline note"), Assert.Single(repository.Writes));
        Assert.Equal(("coordinator-instance", 42), Assert.Single(repository.Reads));
        Assert.True(note.IsSaved);
        Assert.False(await note.SaveCommand.CanExecute.FirstAsync());
        note.Text = "Another change";
        Assert.False(note.IsSaved);
        Assert.True(await note.SaveCommand.CanExecute.FirstAsync());
    }

    [Fact]
    public async Task AFailedLoadCannotOverwriteAnExistingNoteAndCanBeRetried()
    {
        var repository = new Notes { Text = "Keep this", FailLoad = true };
        using var note = new UserNoteVM(repository, "server", 7);
        await note.LoadCommand.Execute();

        Assert.False(note.IsLoaded);
        Assert.NotNull(note.Error);
        Assert.False(note.IsBusy);
        Assert.False(await note.SaveCommand.CanExecute.FirstAsync());
        Assert.Empty(repository.Writes);

        repository.FailLoad = false;
        await note.LoadCommand.Execute();
        Assert.Equal("Keep this", note.Text);
        Assert.True(note.IsLoaded);
        Assert.Null(note.Error);
    }

    [Fact]
    public async Task AFailedSaveKeepsTheDraftAndAllowsRetry()
    {
        var repository = new Notes { FailSave = true };
        using var note = new UserNoteVM(repository, "server", 7);
        await note.LoadCommand.Execute();
        note.Text = "Do not lose this";
        await note.SaveCommand.Execute();

        Assert.Equal("Do not lose this", note.Text);
        Assert.NotNull(note.Error);
        Assert.False(note.IsSaved);
        Assert.False(note.IsBusy);
        Assert.True(await note.SaveCommand.CanExecute.FirstAsync());

        repository.FailSave = false;
        await note.SaveCommand.Execute();
        Assert.Equal("Do not lose this", repository.Text);
        Assert.True(note.IsSaved);
        Assert.Null(note.Error);
    }

    [Fact]
    public async Task ClearingANoteSavesAnEmptyValue()
    {
        var repository = new Notes { Text = "Old note" };
        using var note = new UserNoteVM(repository, "server", 7);
        await note.LoadCommand.Execute();
        note.Text = " \n\t";
        await note.SaveCommand.Execute();

        Assert.Equal(string.Empty, Assert.Single(repository.Writes).Text);
        Assert.Equal(string.Empty, note.Text);
        Assert.True(note.IsSaved);
        Assert.False(await note.SaveCommand.CanExecute.FirstAsync());
    }

    private sealed class Notes : IUserNotesRepository
    {
        public string? Text { get; set; }
        public bool FailLoad { get; set; }
        public bool FailSave { get; set; }
        public List<(string Coordinator, int UserId)> Reads { get; } = [];
        public List<(string Coordinator, int UserId, string Text)> Writes { get; } = [];

        public Task<string?> GetAsync(string coordinatorInstanceId, int userId)
        {
            Reads.Add((coordinatorInstanceId, userId));
            return FailLoad ? Task.FromException<string?>(new IOException("Read failed")) : Task.FromResult(Text);
        }

        public Task SetAsync(string coordinatorInstanceId, int userId, string text)
        {
            if (FailSave)
                return Task.FromException(new IOException("Write failed"));
            Writes.Add((coordinatorInstanceId, userId, text));
            Text = text;
            return Task.CompletedTask;
        }
    }
}

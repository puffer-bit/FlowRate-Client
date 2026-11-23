using Client.Core.Chat;
using Client.Database.Sqlite.Repositories;
using Xunit;

namespace Client.Database.Sqlite.Tests;

[Collection(DatabaseTestCollection.Name)]
public class ChatAttachmentPreferencesRepositoryTests
{
    [Fact]
    public async Task NothingSaved_GivesTheDefaults()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();

        Assert.Equal(new ChatAttachmentPreferences(), await new ChatAttachmentPreferencesRepository(database.Database).GetAsync());
    }

    [Theory]
    [InlineData(1080)]
    [InlineData(null)]
    public async Task VideoChoices_AreKeptWithTheImageOnes(int? videoSide)
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var saved = new ChatAttachmentPreferences(CompressImages: false, StripMetadata: true, CompressVideos: false,
            VideoSide: videoSide);
        await new ChatAttachmentPreferencesRepository(database.Database).SaveAsync(saved);

        var reopened = new ChatAttachmentPreferencesRepository(new Client.Database.Sqlite.SqliteDatabase(database.FilePath));
        Assert.Equal(saved, await reopened.GetAsync());
    }
}

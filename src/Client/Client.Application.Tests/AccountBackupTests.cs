using System.Security.Cryptography;
using Client.Application.Security;
using Client.Application.Sync;
using Xunit;

namespace Client.Application.Tests;

public class AccountBackupTests
{
    private static AccountBackup Backup() => new()
    {
        CoordinatorId = "coordinator",
        UserId = 7,
        Username = "alice",
        Address = "chat.example.com",
        Port = 26666,
        Notes = new() { [3] = "Bob plays support" },
        MutedChats = [12],
        ExportedAt = DateTimeOffset.UnixEpoch
    };

    [Fact]
    public void Backup_OpensWithTheKeyOfItsPhraseOnly()
    {
        using var keys = RecoveryKeySet.FromPhrase(RecoveryPhrase.Generate())!;
        using var other = RecoveryKeySet.FromPhrase(RecoveryPhrase.Generate())!;

        var file = AccountBackupFile.Seal(Backup(), keys.BackupKey);

        var opened = AccountBackupFile.TryOpen(file, keys.BackupKey);
        Assert.NotNull(opened);
        Assert.Equal("Bob plays support", opened!.Notes[3]);
        Assert.Equal([12], opened.MutedChats);
        Assert.Null(AccountBackupFile.TryOpen(file, other.BackupKey));
    }

    [Fact]
    public void ChangedFile_DoesNotOpen()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var file = AccountBackupFile.Seal(Backup(), key);

        // A byte of the header, which is authenticated too, and one of the content
        foreach (var index in new[] { 6, file.Length - 1 })
        {
            var changed = (byte[])file.Clone();
            changed[index] ^= 1;
            Assert.Null(AccountBackupFile.TryOpen(changed, key));
        }

        Assert.Null(AccountBackupFile.TryOpen("not a backup"u8.ToArray(), key));
    }

    [Fact]
    public void SyncName_IsTheSameForTheAccountOnEveryDevice()
    {
        var first = AccountBackupFile.SyncName("coordinator", 7, Guid.NewGuid());
        var second = AccountBackupFile.SyncName("coordinator", 7, Guid.NewGuid());

        Assert.Equal(first[..25], second[..25]);
        Assert.NotEqual(first, second);
        Assert.EndsWith(".frb", first);
    }
}

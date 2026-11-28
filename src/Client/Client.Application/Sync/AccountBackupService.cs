using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Security;
using Client.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Client.Application.Sync;

/// <summary>
/// Encrypted backups of the accounts saved here, as files the user keeps or as a folder other devices of the account
/// share (a synced cloud folder, Syncthing...). Each is encrypted with the backup key of the account, which only its
/// recovery phrase derives: whoever holds the folder learns nothing.
/// </summary>
public interface IAccountBackupService
{
    /// <summary>The backup of a saved account; null when its backup key is not known here and no phrase was given.</summary>
    Task<byte[]?> ExportAsync(string coordinatorId, int userId, string? phrase = null);

    /// <summary>
    /// Restores a backup, opened with the keys of the accounts saved here or with <paramref name="phrase"/>; null when
    /// none opens it. An account not saved here is added, to log in to with the phrase.
    /// </summary>
    Task<AccountBackup?> ImportAsync(byte[] file, string? phrase = null);

    Task<string?> GetSyncFolderAsync();

    /// <summary>Sets the sync folder, or turns syncing off with null.</summary>
    Task SetSyncFolderAsync(string? folder);

    /// <summary>
    /// Takes what the other devices of the account left in the sync folder and leaves the backup of this one; does
    /// nothing without a folder or the backup key.
    /// </summary>
    Task SyncAsync(string coordinatorId, int userId);
}

public sealed partial class AccountBackupService(
    IAccountingSettingsRepository accounts,
    IUserNotesRepository notes,
    IMutedChatsRepository mutedChats,
    ISecretVault vault,
    ILogger<AccountBackupService> logger) : IAccountBackupService
{
    public async Task<byte[]?> ExportAsync(string coordinatorId, int userId, string? phrase = null)
    {
        var key = await BackupKeyAsync(coordinatorId, userId, phrase);
        if (key == null || await accounts.GetCoordinatorInstanceAsync((coordinatorId, userId)) is not ({ } server, { } user))
            return null;

        var backup = new AccountBackup
        {
            CoordinatorId = coordinatorId,
            UserId = userId,
            Username = user.Username,
            Address = server.Address,
            Port = server.Port,
            ServerName = server.Name,
            Notes = new Dictionary<int, string>(await notes.GetAllAsync(coordinatorId)),
            // A mute for a while is over soon, so only those until the user ends them are kept
            MutedChats = (await mutedChats.GetUntilAsync(coordinatorId))
                .Where(mute => mute.Value == null).Select(mute => mute.Key).Order().ToList(),
            ExportedAt = DateTimeOffset.UtcNow,
            DeviceId = user.DeviceId
        };
        return AccountBackupFile.Seal(backup, key);
    }

    public async Task<AccountBackup?> ImportAsync(byte[] file, string? phrase = null)
    {
        var (backup, key) = await OpenAsync(file, phrase);
        if (backup == null || key == null)
            return null;

        await MergeAsync(backup);
        await vault.SaveAsync(SecretNames.BackupKey(backup.CoordinatorId, backup.UserId), Convert.ToBase64String(key));
        return backup;
    }

    public Task<string?> GetSyncFolderAsync() => vault.LoadAsync(SecretNames.SyncFolder);

    public Task SetSyncFolderAsync(string? folder) => string.IsNullOrWhiteSpace(folder)
        ? vault.DeleteAsync(SecretNames.SyncFolder)
        : vault.SaveAsync(SecretNames.SyncFolder, folder);

    public async Task SyncAsync(string coordinatorId, int userId)
    {
        try
        {
            var folder = await GetSyncFolderAsync();
            var key = await BackupKeyAsync(coordinatorId, userId, null);
            if (folder == null || key == null || !Directory.Exists(folder)
                || await accounts.GetCoordinatorInstanceAsync((coordinatorId, userId)) is not (_, { } user)
                || user.DeviceId is not { } deviceId)
                return;

            var own = AccountBackupFile.SyncName(coordinatorId, userId, deviceId);
            var prefix = $"flowrate-{AccountBackupFile.AccountTag(coordinatorId, userId)}-";
            foreach (var path in Directory.EnumerateFiles(folder, prefix + "*." + AccountBackupFile.Extension))
            {
                if (Path.GetFileName(path) == own)
                    continue;
                if (AccountBackupFile.TryOpen(await File.ReadAllBytesAsync(path), key) is { } backup
                    && backup.CoordinatorId == coordinatorId && backup.UserId == userId)
                    await MergeAsync(backup);
            }

            if (await ExportAsync(coordinatorId, userId) is { } file)
            {
                // Written aside and moved over, so a syncing tool never takes a half-written file
                var target = Path.Combine(folder, own);
                var temporary = target + ".tmp";
                await File.WriteAllBytesAsync(temporary, file);
                File.Move(temporary, target, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSyncFailed(exception, coordinatorId);
        }
    }

    /// <summary>
    /// Adds what the backup has and this device has not: the server and the account, notes about users without one
    /// here, muted chats. A note written here is kept: the folder never deletes nor overwrites what the user typed.
    /// </summary>
    private async Task MergeAsync(AccountBackup backup)
    {
        if (await accounts.GetCoordinatorAsync(backup.CoordinatorId) == null)
            await accounts.SaveCoordinatorAsync(new SavedCoordinatorModel
            {
                CoordinatorId = backup.CoordinatorId,
                Name = backup.ServerName,
                Address = backup.Address,
                Port = backup.Port
            });
        if (await accounts.GetCoordinatorInstanceAsync((backup.CoordinatorId, backup.UserId)) == null)
            await accounts.SaveUserAsync(new SavedUserModel
            {
                CoordinatorId = backup.CoordinatorId,
                Id = backup.UserId,
                Username = backup.Username,
                IsRecoveryPhraseSaved = true
            });

        var local = await notes.GetAllAsync(backup.CoordinatorId);
        foreach (var (userId, text) in backup.Notes)
        {
            if (!local.ContainsKey(userId))
                await notes.SetAsync(backup.CoordinatorId, userId, text);
        }

        var muted = (await mutedChats.GetAsync(backup.CoordinatorId)).ToHashSet();
        foreach (var chatId in backup.MutedChats.Where(chatId => !muted.Contains(chatId)))
            await mutedChats.SetAsync(backup.CoordinatorId, chatId, true);
    }

    private async Task<(AccountBackup? Backup, byte[]? Key)> OpenAsync(byte[] file, string? phrase)
    {
        if (phrase != null)
        {
            using var keys = RecoveryKeySet.FromPhrase(phrase);
            if (keys != null && AccountBackupFile.TryOpen(file, keys.BackupKey) is { } byPhrase)
                return (byPhrase, keys.BackupKey.ToArray());
        }

        foreach (var server in await accounts.GetAllCoordinatorsAsync())
        {
            foreach (var user in server.Users)
            {
                if (await BackupKeyAsync(server.CoordinatorId, user.Id, null) is { } key
                    && AccountBackupFile.TryOpen(file, key) is { } backup)
                    return (backup, key);
            }
        }

        return (null, null);
    }

    private async Task<byte[]?> BackupKeyAsync(string coordinatorId, int userId, string? phrase)
    {
        if (phrase != null)
        {
            using var keys = RecoveryKeySet.FromPhrase(phrase);
            return keys?.BackupKey.ToArray();
        }

        var stored = await vault.LoadAsync(SecretNames.BackupKey(coordinatorId, userId));
        return stored == null ? null : Convert.FromBase64String(stored);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The sync folder could not be used for coordinator {Coordinator}.")]
    private partial void LogSyncFailed(Exception exception, string coordinator);
}

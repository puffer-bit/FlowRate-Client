using System.Globalization;
using Client.Application.Ports;

namespace Client.TestBot.Accounts;

/// <summary>A device an account of the test bot logs in as, with the account it belongs to.</summary>
internal sealed record SavedDevice(Guid DeviceId, string KeyName, int UserId);

/// <summary>
/// The accounts of the bots, in the table of secrets of the SQLite database of the test bot: the recovery phrases of
/// registered accounts and the devices of bot accounts. Plain text on purpose, these are throwaway test accounts.
/// </summary>
internal sealed class BotAccountStore(ISecretsRepository secrets, string directory)
{
    private const string PhrasePrefix = "testbot-phrase-";
    private const string DevicePrefix = "testbot-device-";

    /// <summary>
    /// The recovery phrase of the profile. A phrase the earlier file store kept is moved over, so the bots keep their
    /// accounts.
    /// </summary>
    public async Task<string?> LoadPhraseAsync(string profileId)
    {
        if (await secrets.LoadAsync(PhrasePrefix + profileId) is { } phrase)
            return phrase;

        var legacyPath = Path.Combine(directory, $"phrase-{profileId}.txt");
        if (!File.Exists(legacyPath))
            return null;

        var legacy = (await File.ReadAllTextAsync(legacyPath)).Trim();
        if (legacy.Length == 0)
            return null;

        await SavePhraseAsync(profileId, legacy);
        File.Delete(legacyPath);
        return legacy;
    }

    /// <summary>Stores the phrase; a missing one leaves the stored phrase alone.</summary>
    public async Task SavePhraseAsync(string profileId, string? phrase)
    {
        if (!string.IsNullOrWhiteSpace(phrase))
            await secrets.SaveAsync(PhrasePrefix + profileId, phrase);
    }

    public async Task<SavedDevice?> LoadDeviceAsync(string profileId)
    {
        var parts = (await secrets.LoadAsync(DevicePrefix + profileId))?.Split('\n');
        return parts is [var deviceId, var keyName, var userId] && Guid.TryParse(deviceId, out var id) &&
               int.TryParse(userId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var user)
            ? new SavedDevice(id, keyName, user)
            : null;
    }

    public Task SaveDeviceAsync(string profileId, SavedDevice device) =>
        secrets.SaveAsync(DevicePrefix + profileId,
            $"{device.DeviceId:D}\n{device.KeyName}\n{device.UserId.ToString(CultureInfo.InvariantCulture)}");

    public Task ForgetDeviceAsync(string profileId) => secrets.DeleteAsync(DevicePrefix + profileId);
}

/// <summary>
/// The secrets of the test bot in its database only: a keyring would ask for every one of a thousand bots, and
/// would keep their keys after the database is gone.
/// </summary>
internal sealed class DatabaseSecretVault(ISecretsRepository secrets) : ISecretVault
{
    public bool IsProtected => false;

    public Task SaveAsync(string name, string value, CancellationToken cancellationToken = default) =>
        secrets.SaveAsync(name, value);

    public Task<string?> LoadAsync(string name, CancellationToken cancellationToken = default) =>
        secrets.LoadAsync(name);

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default) =>
        secrets.DeleteAsync(name);
}

using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.Database.File;

/// <summary>Plaintext files for test accounts. The directory is supplied by the host.</summary>
internal sealed class FileRecoveryPhraseStore(string directory, ILogger<FileRecoveryPhraseStore> logger)
    : IRecoveryPhraseStore
{
    public string? Load(string profileId)
    {
        var path = GetPath(profileId);
        try
        {
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Save(string profileId, string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return;

        var path = GetPath(profileId);
        try
        {
            Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(path, phrase);
        }
        catch (IOException exception)
        {
            logger.LogWarning("Failed to save the recovery phrase for {ProfileId}: {Message}",
                profileId, exception.Message);
        }
    }

    private string GetPath(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (profileId.Contains('/') || profileId.Contains('\\'))
            throw new ArgumentException("The profile identifier must not contain directory separators.", nameof(profileId));
        return Path.Combine(directory, $"phrase-{profileId}.txt");
    }
}

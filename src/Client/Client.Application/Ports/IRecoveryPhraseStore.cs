namespace Client.Application.Ports;

/// <summary>Recovery phrases retained between runs, keyed by a host-defined local profile identifier.</summary>
public interface IRecoveryPhraseStore
{
    string? Load(string profileId);

    /// <summary>Stores a phrase. A missing or blank phrase leaves the existing value untouched.</summary>
    void Save(string profileId, string? phrase);
}

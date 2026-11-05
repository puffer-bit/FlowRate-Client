using Client.Application.Updates;
using Client.Core.Settings;

namespace Client.Application.Ports;

public interface IReleaseNotesSource
{
    /// <summary>Up to six published releases of the selected kind, newest first.</summary>
    Task<IReadOnlyList<PublishedRelease>> GetAsync(UpdateChannel channel, bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

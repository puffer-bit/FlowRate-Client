using Client.Application.Updates;
using Client.Core.Settings;

namespace Client.Application.Ports;

public interface IReleaseNotesSource
{
    /// <summary>The published releases of the selected kind, newest first; the oldest may be left out.</summary>
    Task<IReadOnlyList<PublishedRelease>> GetAsync(UpdateChannel channel, bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

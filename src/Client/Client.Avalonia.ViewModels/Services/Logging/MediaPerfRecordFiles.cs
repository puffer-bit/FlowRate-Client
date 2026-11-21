using Client.Application.Ports;
using Client.Tools.Logging;

namespace Client.Services.Logging;

/// <summary>
/// The detailed statistics of the media in the files of <see cref="MediaPerfRecords"/>, next to the journal.
/// </summary>
public sealed class MediaPerfRecordFiles : IMediaPerfRecords
{
    public void Write(string json) => MediaPerfRecords.Write(json);
}

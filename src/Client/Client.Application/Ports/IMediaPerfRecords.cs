namespace Client.Application.Ports;

/// <summary>
/// Where the detailed statistics of the media go, one JSON object a record, when the Media statistics
/// part of the settings asks for them. The desktop client writes them to files next to the journal.
/// </summary>
public interface IMediaPerfRecords
{
    /// <summary>Appends one record, a whole JSON object. Any thread.</summary>
    void Write(string json);
}

/// <summary>
/// Keeps no records: for a host that has nowhere to write them.
/// </summary>
public sealed class NoMediaPerfRecords : IMediaPerfRecords
{
    public static NoMediaPerfRecords Instance { get; } = new();

    public void Write(string json)
    {
    }
}

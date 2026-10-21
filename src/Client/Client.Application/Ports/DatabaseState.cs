namespace Client.Application.Ports;

public enum DatabaseState
{
    UnexpectedError,
    NoError,

    /// <summary>The file was created or upgraded by a newer build, this one cannot use it.</summary>
    NewerThanApplication,

    Corrupted,

    /// <summary>Another process, e.g. a second copy of the application, holds the file locked.</summary>
    InUse
}

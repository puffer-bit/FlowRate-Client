namespace Client.Services;

public enum ApplicationLoadResult
{
    UnexpectedError,
    NoError,
    DatabaseError,
    DatabaseCorrupted,
    DatabaseNewerThanApplication,

    /// <summary>Another copy of the application holds the database file locked.</summary>
    DatabaseInUse,

    /// <summary>The database is encrypted and the user gave up on its password.</summary>
    DatabaseLocked,
    NativeLibraryFail,
    NativeAssembliesFailedToLoad,
    MediaComponentsBroken,
}
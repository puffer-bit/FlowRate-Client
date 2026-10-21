using System.Runtime.InteropServices;
using System.Text;
using Client.Tools.AppInfo;
using Microsoft.Data.Sqlite;

namespace Client.Infrastructure.Database;

/// <summary>
/// The local SQLite file. Repositories open a short-lived connection per operation; the pool keeps
/// the actual file handles, and SQLite serializes the writers.
/// </summary>
/// <remarks>
/// SQLite is SQLite3 Multiple Ciphers: with a password every page of the file and of its journal
/// is encrypted. See <see cref="DatabaseProtection"/>.
/// </remarks>
internal sealed partial class SqliteDatabase
{
    private const string FileName = "client.db";

    // The 16 bytes every plain SQLite file starts with; an encrypted file starts with its salt
    private static ReadOnlySpan<byte> PlainHeader => "SQLite format 3\0"u8;

    // The encrypting shim over the unix VFS that holds the locks of the file until it is closed.
    // Connections of this process share them, other processes get SQLITE_BUSY
    private const string ExclusiveVfs = "unix-excl";
    private const string ExclusiveCipherVfs = "multipleciphers-" + ExclusiveVfs;

    private static readonly Lock ExclusiveVfsLock = new();
    private static bool _isExclusiveVfsCreated;

    private string? _password;
    private bool _isFileLocked;

    // In the data folder of the user: an update replaces the folder of the executable
    public SqliteDatabase() : this(Path.Combine(AppDirectories.Data, FileName))
    {
    }

    /// <summary>
    /// The database in <paramref name="directory"/>, for a platform with a data folder of its own.
    /// </summary>
    public static SqliteDatabase In(string directory) => new(Path.Combine(directory, FileName));

    internal SqliteDatabase(string filePath)
    {
        FilePath = filePath;
        // SQLite creates the file, not the folders above it
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

        ConnectionString = Build(pooling: true);
    }

    public string FilePath { get; }

    public string ConnectionString { get; private set; }

    /// <summary>The password the connections open the file with, null for a plain file.</summary>
    public string? Password => _password;

    /// <summary>Whether the connections hold the file locked against other processes.</summary>
    public bool IsFileLocked => _isFileLocked;

    /// <summary>
    /// Whether the file is encrypted, judged by its header: a missing or empty file is not.
    /// </summary>
    public bool IsFileEncrypted
    {
        get
        {
            try
            {
                using var file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                if (file.Length == 0)
                    return false;

                Span<byte> header = stackalloc byte[PlainHeader.Length];
                return file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length ||
                       !header.SequenceEqual(PlainHeader);
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Whether the file can be held locked against other processes, see <see cref="Configure"/>.
    /// </summary>
    public static bool CanLockFile => !OperatingSystem.IsWindows();

    /// <summary>
    /// The password the next connections open the file with, null for a plain file, and whether
    /// they hold it locked against other processes. Pooled connections opened the old way are dropped.
    /// </summary>
    /// <remarks>
    /// A connection in use at the moment keeps the old way until it is closed. Switching the lock
    /// while another connection is open would leave two copies of the WAL index, so it happens at
    /// start, before the repositories are used.
    /// </remarks>
    public void Configure(string? password, bool isFileLocked)
    {
        if (isFileLocked && !CanLockFile)
            throw new PlatformNotSupportedException("The database file cannot be locked on this system");

        if (isFileLocked)
            CreateExclusiveVfs();

        _password = string.IsNullOrEmpty(password) ? null : password;
        _isFileLocked = isFileLocked;
        ConnectionString = Build(pooling: true);

        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// The connection string of the current configuration with another password, outside the pool,
    /// that gives up after <paramref name="busyTimeoutSeconds"/> on a file another process holds.
    /// </summary>
    public string BuildProbe(string? password, int busyTimeoutSeconds) =>
        Build(pooling: false, password, busyTimeoutSeconds);

    public async Task<SqliteConnection> OpenAsync() => await OpenAsync(ConnectionString);

    public static async Task<SqliteConnection> OpenAsync(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);

        try
        {
            await connection.OpenAsync();
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return connection;
    }

    private string Build(bool pooling) => Build(pooling, _password, null);

    private string Build(bool pooling, string? password, int? busyTimeoutSeconds)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            // A URI names the VFS; Microsoft.Data.Sqlite opens a data source starting with "file:" as one
            DataSource = _isFileLocked ? $"file:{EscapeUriPath(Path.GetFullPath(FilePath))}?vfs={ExclusiveCipherVfs}" : FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = pooling
        };

        // Microsoft.Data.Sqlite keys the connection with PRAGMA key as it opens
        if (!string.IsNullOrEmpty(password))
            builder.Password = password;

        if (busyTimeoutSeconds is { } timeout)
            builder.DefaultTimeout = timeout;

        return builder.ToString();
    }

    // SQLite reads %XX in a URI path, and ? and # end it
    private static string EscapeUriPath(string path)
    {
        var escaped = new StringBuilder(path.Length);

        foreach (var c in path)
        {
            escaped.Append(c switch
            {
                '%' => "%25",
                '?' => "%3F",
                '#' => "%23",
                _ => c.ToString()
            });
        }

        return escaped.ToString();
    }

    /// <summary>
    /// SQLite3 Multiple Ciphers wraps only the default VFS at start; the shim over the exclusive
    /// one is created on demand.
    /// </summary>
    private static void CreateExclusiveVfs()
    {
        lock (ExclusiveVfsLock)
        {
            if (_isExclusiveVfsCreated)
                return;

            // Loads the native library and initializes SQLite before the call into it
            SQLitePCL.Batteries_V2.Init();

            var result = CreateCipherVfs(ExclusiveVfs, makeDefault: 0);
            if (result != 0)
                throw new InvalidOperationException($"SQLite3 Multiple Ciphers could not wrap the {ExclusiveVfs} VFS: {result}");

            _isExclusiveVfsCreated = true;
        }
    }

    [LibraryImport("sqlite3mc", EntryPoint = "sqlite3mc_vfs_create", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int CreateCipherVfs(string realVfsName, int makeDefault);

    /// <summary>
    /// Stored as UTC; SQLite gives the value back without the kind.
    /// </summary>
    public static DateTime FromStorage(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime ToStorage(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
}

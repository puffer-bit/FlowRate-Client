using System.Buffers.Binary;
using System.Collections;
using Client.Application.Ports;
using Microsoft.Win32.SafeHandles;

namespace Client.Infrastructure.Media;

/// <summary>
/// The chunks of an attachment downloaded so far: the bytes in a data file at their own offsets, and which chunks
/// are there in a map file next to it. The map is written after the chunk, so a chunk it names is always whole.
/// When the last chunk arrives the data file is moved to the place of the cached attachment and the map is deleted.
/// </summary>
internal sealed class PartialMediaFile
{
    public const int DefaultChunkSize = 512 * 1024;

    private const uint MapMagic = 0x464D5246; // "FRMF"
    private const int MapHeaderSize = 4 + 4 + 8;

    private readonly Lock _lock = new();
    private readonly string _dataPath;
    private readonly string _mapPath;
    private readonly string _completePath;
    private readonly Action<long> _written;
    private readonly BitArray _chunks;
    private SafeFileHandle? _data;
    private int _users = 1;

    public string DataPath => _dataPath;
    public long Length { get; }
    public int ChunkSize { get; }
    public int ChunkCount { get; }
    public int ChunksPresent { get; private set; }
    public string? CompletePath { get; private set; }

    /// <param name="written">Told how many bytes were added to the cache.</param>
    public PartialMediaFile(string dataPath, string completePath, long length, Action<long> written,
        int chunkSize = DefaultChunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        _dataPath = dataPath;
        _mapPath = dataPath + ".map";
        _completePath = completePath;
        _written = written;
        Length = length;
        ChunkSize = chunkSize;
        ChunkCount = checked((int)((length + chunkSize - 1) / chunkSize));
        _chunks = new BitArray(ChunkCount);

        Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
        if (LoadMap())
        {
            _data = File.OpenHandle(dataPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            // A run that ended before the whole file could be moved into place
            if (ChunksPresent == ChunkCount)
                Complete();
        }
        else
        {
            // What was kept does not belong to a file of this size: it starts again
            TryDelete(_mapPath);
            _data = File.OpenHandle(dataPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        }
        Touch(dataPath);
    }

    /// <summary>Called by the cache when one more caller shares the file.</summary>
    public bool TryAddUser()
    {
        lock (_lock)
        {
            if (_users == 0)
                return false;
            _users++;
            return true;
        }
    }

    /// <returns>True when that was the last caller and the file is closed.</returns>
    public bool RemoveUser()
    {
        lock (_lock)
        {
            if (_users == 0 || --_users > 0)
                return false;
            _data?.Dispose();
            _data = null;
            return true;
        }
    }

    public bool HasChunk(int chunk)
    {
        lock (_lock)
            return chunk >= 0 && chunk < ChunkCount && _chunks[chunk];
    }

    public int Read(long position, Span<byte> buffer)
    {
        if (position < 0 || position >= Length || buffer.IsEmpty)
            return 0;

        var chunk = (int)(position / ChunkSize);
        var end = Math.Min(Length, (long)(chunk + 1) * ChunkSize);
        var count = (int)Math.Min(buffer.Length, end - position);
        lock (_lock)
        {
            if (_data == null || !_chunks[chunk])
                return 0;

            var total = 0;
            while (total < count)
            {
                var read = RandomAccess.Read(_data, buffer.Slice(total, count - total), position + total);
                if (read == 0)
                    break;
                total += read;
            }
            return total;
        }
    }

    public void WriteChunk(int chunk, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunk);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunk, ChunkCount);
        var offset = (long)chunk * ChunkSize;
        if (data.Length != Math.Min(ChunkSize, Length - offset))
            throw new ArgumentException("A chunk is written whole.", nameof(data));

        lock (_lock)
        {
            if (_data == null || _chunks[chunk])
                return;

            RandomAccess.Write(_data, data, offset);
            _chunks[chunk] = true;
            ChunksPresent++;
            if (ChunksPresent == ChunkCount)
                Complete();
            else
                SaveMap();
        }
        _written(data.Length);
    }

    private void Complete()
    {
        _data!.Dispose();
        _data = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_completePath)!);
            File.Move(_dataPath, _completePath, overwrite: true);
            TryDelete(_mapPath);
            CompletePath = _completePath;
            _data = File.OpenHandle(_completePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The file stays where it was; the map says it is whole, so it is found complete next time
            SaveMap();
            _data = File.OpenHandle(_dataPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        }
    }

    private bool LoadMap()
    {
        try
        {
            if (!File.Exists(_mapPath) || !File.Exists(_dataPath))
                return false;

            var bytes = File.ReadAllBytes(_mapPath);
            if (bytes.Length != MapHeaderSize + (ChunkCount + 7) / 8
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != MapMagic
                || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) != ChunkSize
                || BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(8)) != Length)
                return false;

            var dataLength = new FileInfo(_dataPath).Length;
            for (var chunk = 0; chunk < ChunkCount; chunk++)
            {
                if ((bytes[MapHeaderSize + chunk / 8] & (1 << (chunk % 8))) == 0)
                    continue;
                // A chunk the data file does not reach was lost with it
                if (dataLength < Math.Min(Length, (long)(chunk + 1) * ChunkSize))
                    return false;
                _chunks[chunk] = true;
                ChunksPresent++;
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void SaveMap()
    {
        var bytes = new byte[MapHeaderSize + (ChunkCount + 7) / 8];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, MapMagic);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), ChunkSize);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), Length);
        for (var chunk = 0; chunk < ChunkCount; chunk++)
            if (_chunks[chunk])
                bytes[MapHeaderSize + chunk / 8] |= (byte)(1 << (chunk % 8));

        try
        {
            var temporary = _mapPath + ".new";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _mapPath, overwrite: true);
            Touch(_dataPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The chunk is still there for this run; the next one downloads it again
        }
    }

    private static void Touch(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>What one caller holds: disposing it lets go of the shared file once.</summary>
    internal sealed class Lease(PartialMediaFile file, Action<PartialMediaFile> closed) : IPartialMediaFile
    {
        private int _disposed;

        public long Length => file.Length;
        public int ChunkSize => file.ChunkSize;
        public int ChunkCount => file.ChunkCount;
        public int ChunksPresent => file.ChunksPresent;
        public string? CompletePath => file.CompletePath;

        public bool HasChunk(int chunk) => file.HasChunk(chunk);

        public int Read(long position, Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return file.Read(position, buffer);
        }

        public void WriteChunk(int chunk, ReadOnlySpan<byte> data)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            file.WriteChunk(chunk, data);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && file.RemoveUser())
                closed(file);
        }
    }
}

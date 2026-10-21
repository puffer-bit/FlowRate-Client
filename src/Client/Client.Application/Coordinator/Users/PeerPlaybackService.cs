using System.Collections.Concurrent;
using Client.Core.Users;
using Client.Application.Ports.Repositories;
using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator.Users;

/// <summary>
/// Keeps the playback settings of the users of the connected coordinator in memory and stores
/// them with the remembered users.
/// </summary>
public sealed partial class PeerPlaybackService : IPeerPlaybackService
{
    // A slider produces a value per pixel, only the one it stops at is stored
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly IRememberedUsersRepository _rememberedUsers;
    private readonly ILogger<PeerPlaybackService> _logger;

    private readonly Lock _lock = new();
    private readonly Dictionary<(int UserId, PeerAudioChannel Channel), PeerPlayback> _playback = new();

    // Users whose stored settings were loaded; the others are loaded on the next LoadAsync
    private readonly HashSet<int> _loadedUsers = [];
    private readonly ConcurrentDictionary<int, bool> _isBlocked = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _pendingSaves = new();

    // User ids are only unique within a coordinator, the settings are reset when it changes
    private string? _coordinatorInstanceId;

    public event Action<int, PeerAudioChannel, PeerPlayback>? Changed;

    public PeerPlaybackService(IRememberedUsersRepository rememberedUsers, ILogger<PeerPlaybackService> logger)
    {
        _rememberedUsers = rememberedUsers;
        _logger = logger;
    }

    public PeerPlayback Get(int userId, PeerAudioChannel channel)
    {
        lock (_lock)
            return _playback.GetValueOrDefault((userId, channel), PeerPlayback.Default);
    }

    public void Set(int userId, PeerAudioChannel channel, PeerPlayback playback)
    {
        string? coordinatorInstanceId;

        lock (_lock)
        {
            if (_playback.TryGetValue((userId, channel), out var current) && current == playback)
                return;

            _playback[(userId, channel)] = playback;
            coordinatorInstanceId = _coordinatorInstanceId;
        }

        Changed?.Invoke(userId, channel, playback);

        if (coordinatorInstanceId != null)
            ScheduleSave(coordinatorInstanceId, userId);
    }

    public async Task LoadAsync(string coordinatorInstanceId, IReadOnlyCollection<int> userIds)
    {
        List<int> missing;

        lock (_lock)
        {
            if (_coordinatorInstanceId != coordinatorInstanceId)
            {
                _coordinatorInstanceId = coordinatorInstanceId;
                _playback.Clear();
                _loadedUsers.Clear();
                _isBlocked.Clear();
            }

            missing = userIds.Where(id => !_loadedUsers.Contains(id)).Distinct().ToList();
        }

        if (missing.Count == 0)
            return;

        IReadOnlyDictionary<int, UserHybridSessionData> stored;

        try
        {
            stored = await _rememberedUsers.GetHybridSessionDataAsync(coordinatorInstanceId, missing);
        }
        catch (Exception exception)
        {
            LogLoadFailed(exception, missing.Count);
            return;
        }

        foreach (var (userId, data) in stored)
        {
            var voice = FromStorage(data.Volume, data.IsMuted, data.IsVolumeOverrideEnabled);
            var screen = FromStorage(data.ScreenVolume, data.IsScreenMuted, data.IsScreenVolumeOverrideEnabled);
            bool isVoiceAdded, isScreenAdded;

            lock (_lock)
            {
                if (_coordinatorInstanceId != coordinatorInstanceId || !_loadedUsers.Add(userId))
                    continue;

                // A setting changed while loading wins over the stored one, channel by channel
                isVoiceAdded = _playback.TryAdd((userId, PeerAudioChannel.Voice), voice);
                isScreenAdded = _playback.TryAdd((userId, PeerAudioChannel.Screen), screen);
            }

            _isBlocked[userId] = data.IsBlocked;

            if (isVoiceAdded)
                Changed?.Invoke(userId, PeerAudioChannel.Voice, voice);

            if (isScreenAdded)
                Changed?.Invoke(userId, PeerAudioChannel.Screen, screen);
        }
    }

    private void ScheduleSave(string coordinatorInstanceId, int userId)
    {
        var pending = new CancellationTokenSource();

        if (_pendingSaves.TryGetValue(userId, out var previous))
            previous.Cancel();

        _pendingSaves[userId] = pending;

        _ = SaveAsync(coordinatorInstanceId, userId, pending);
    }

    private async Task SaveAsync(string coordinatorInstanceId, int userId, CancellationTokenSource pending)
    {
        try
        {
            await Task.Delay(SaveDelay, pending.Token);

            PeerPlayback voice, screen;

            lock (_lock)
            {
                if (_coordinatorInstanceId != coordinatorInstanceId ||
                    !_playback.ContainsKey((userId, PeerAudioChannel.Voice)) &&
                    !_playback.ContainsKey((userId, PeerAudioChannel.Screen)))
                    return;

                voice = _playback.GetValueOrDefault((userId, PeerAudioChannel.Voice), PeerPlayback.Default);
                screen = _playback.GetValueOrDefault((userId, PeerAudioChannel.Screen), PeerPlayback.Default);
            }

            var data = ToStorage(voice, screen, _isBlocked.GetValueOrDefault(userId));

            if (!await _rememberedUsers.SaveHybridSessionDataAsync(coordinatorInstanceId, userId, data))
                LogUserNotRemembered(userId);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            LogSaveFailed(exception, userId);
        }
        finally
        {
            _pendingSaves.TryRemove(new KeyValuePair<int, CancellationTokenSource>(userId, pending));
            pending.Dispose();
        }
    }

    // The volume is stored in percent of the original level
    private static PeerPlayback FromStorage(int volumePercent, bool isMuted, bool isOverdriveEnabled)
    {
        var playback = new PeerPlayback(volumePercent / 100.0, isMuted, isOverdriveEnabled);

        return playback with { Volume = Math.Clamp(playback.Volume, 0, playback.MaxVolume) };
    }

    private static UserHybridSessionData ToStorage(PeerPlayback voice, PeerPlayback screen, bool isBlocked) => new()
    {
        Volume = (int)Math.Round(voice.Volume * 100),
        IsVolumeOverrideEnabled = voice.IsOverdriveEnabled,
        IsMuted = voice.IsMuted,
        ScreenVolume = (int)Math.Round(screen.Volume * 100),
        IsScreenVolumeOverrideEnabled = screen.IsOverdriveEnabled,
        IsScreenMuted = screen.IsMuted,
        IsBlocked = isBlocked
    };
}

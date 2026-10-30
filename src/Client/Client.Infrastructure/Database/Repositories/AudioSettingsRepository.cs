using Client.Application.Ports.Repositories;
using Client.Core.Settings;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class AudioSettingsRepository : IAudioSettingsRepository
{
    private readonly SqliteDatabase _database;

    public AudioSettingsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<AudioSettings> GetAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<AudioSettingsRow>(
            "SELECT InputDeviceId, InputDeviceName, InputVolume, IsVoiceGateEnabled, VoiceGateThresholdDb, IsVoiceDetectionEnabled, IsAdaptiveThresholdEnabled, NoiseSuppression, IsEchoCancellationEnabled, IsAutoGainControlEnabled, AreSoundsEnabled, SoundsVolume, SoundPack, DisabledSounds FROM AudioSettings WHERE Id = 1");

        if (row == null)
            return new AudioSettings();

        return new AudioSettings
        {
            InputDeviceId = row.InputDeviceId,
            InputDeviceName = row.InputDeviceName,
            InputVolume = row.InputVolume,
            IsVoiceGateEnabled = row.IsVoiceGateEnabled,
            VoiceGateThresholdDb = row.VoiceGateThresholdDb,
            IsVoiceDetectionEnabled = row.IsVoiceDetectionEnabled,
            IsAdaptiveThresholdEnabled = row.IsAdaptiveThresholdEnabled,
            NoiseSuppression = (NoiseSuppressionLevel)row.NoiseSuppression,
            IsEchoCancellationEnabled = row.IsEchoCancellationEnabled,
            IsAutoGainControlEnabled = row.IsAutoGainControlEnabled,
            AreSoundsEnabled = row.AreSoundsEnabled,
            SoundsVolume = row.SoundsVolume,
            SoundPack = row.SoundPack,
            DisabledSounds = row.DisabledSounds.Split(',', StringSplitOptions.RemoveEmptyEntries)
        };
    }

    public async Task SaveAsync(AudioSettings settings)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO AudioSettings (Id, InputDeviceId, InputDeviceName, InputVolume, IsVoiceGateEnabled,
                                       VoiceGateThresholdDb, IsVoiceDetectionEnabled, IsAdaptiveThresholdEnabled,
                                       NoiseSuppression, IsEchoCancellationEnabled, IsAutoGainControlEnabled,
                                       AreSoundsEnabled, SoundsVolume, SoundPack, DisabledSounds)
            VALUES (1, @InputDeviceId, @InputDeviceName, @InputVolume, @IsVoiceGateEnabled, @VoiceGateThresholdDb,
                    @IsVoiceDetectionEnabled, @IsAdaptiveThresholdEnabled, @NoiseSuppression, @IsEchoCancellationEnabled,
                    @IsAutoGainControlEnabled, @AreSoundsEnabled, @SoundsVolume, @SoundPack, @DisabledSounds)
            ON CONFLICT (Id) DO UPDATE SET
                InputDeviceId = excluded.InputDeviceId,
                InputDeviceName = excluded.InputDeviceName,
                InputVolume = excluded.InputVolume,
                IsVoiceGateEnabled = excluded.IsVoiceGateEnabled,
                VoiceGateThresholdDb = excluded.VoiceGateThresholdDb,
                IsVoiceDetectionEnabled = excluded.IsVoiceDetectionEnabled,
                IsAdaptiveThresholdEnabled = excluded.IsAdaptiveThresholdEnabled,
                NoiseSuppression = excluded.NoiseSuppression,
                IsEchoCancellationEnabled = excluded.IsEchoCancellationEnabled,
                IsAutoGainControlEnabled = excluded.IsAutoGainControlEnabled,
                AreSoundsEnabled = excluded.AreSoundsEnabled,
                SoundsVolume = excluded.SoundsVolume,
                SoundPack = excluded.SoundPack,
                DisabledSounds = excluded.DisabledSounds
            """, new
            {
                settings.InputDeviceId,
                // The name only helps to find the chosen device again, the default one has none
                InputDeviceName = settings.InputDeviceId == null ? null : settings.InputDeviceName,
                settings.InputVolume,
                settings.IsVoiceGateEnabled,
                settings.VoiceGateThresholdDb,
                settings.IsVoiceDetectionEnabled,
                settings.IsAdaptiveThresholdEnabled,
                NoiseSuppression = (int)settings.NoiseSuppression,
                settings.IsEchoCancellationEnabled,
                settings.IsAutoGainControlEnabled,
                settings.AreSoundsEnabled,
                settings.SoundsVolume,
                settings.SoundPack,
                DisabledSounds = string.Join(',', settings.DisabledSounds)
            });
    }

    public async Task<bool> GetUseGStreamerAsync()
    {
        await using var connection = await _database.OpenAsync();

        return await connection.QueryFirstOrDefaultAsync<bool?>(
            "SELECT UseGStreamer FROM AudioSettings WHERE Id = 1") ?? false;
    }

    public async Task UpdateUseGStreamerAsync(bool useGStreamer)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO AudioSettings (Id, UseGStreamer) VALUES (1, @useGStreamer)
            ON CONFLICT (Id) DO UPDATE SET UseGStreamer = excluded.UseGStreamer
            """, new { useGStreamer });
    }
}

internal sealed record AudioSettingsRow(string? InputDeviceId, string? InputDeviceName, double InputVolume,
    bool IsVoiceGateEnabled, double VoiceGateThresholdDb, bool IsVoiceDetectionEnabled, bool IsAdaptiveThresholdEnabled,
    int NoiseSuppression, bool IsEchoCancellationEnabled, bool IsAutoGainControlEnabled, bool AreSoundsEnabled, double SoundsVolume, string? SoundPack, string DisabledSounds);

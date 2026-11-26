using System.Diagnostics;
using System.Globalization;
using Client.Application.Coordinator;
using Client.Core.Coordinator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Enums;
using Shared.Models.DataTransferObjects;

namespace Client.TestBot.Accounts;

/// <summary>
/// The account that makes the bot accounts of the test bot and gives them login codes: one that may manage bots,
/// logged in once with a login code from the control panel and as its device from then on. Connects on the first
/// request and stays connected until disposed.
/// </summary>
internal sealed partial class BotOwner(BotOptions options, IServiceProvider services, BotAccountStore accounts,
    ILogger<BotOwner> logger) : IAsyncDisposable
{
    /// <summary>The names of the bots: the number is the one of the bot, so a bot finds its account again.</summary>
    public const string NamePrefix = "TestBot ";

    // Making a bot and its codes count in the Moderation rate group, 10 per 10 seconds by default
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(1.1);

    // The logging generator looks for a field
    private readonly ILogger<BotOwner> _logger = logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private ICoordinatorSession? _session;
    private long _lastRequest;

    private string ProfileId => $"{options.Host}-{options.Port}-owner";

    public static string NameOf(int index) => NamePrefix + (index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A login code for the bot account of the bot: a new one for its account when it has one (ending its earlier
    /// devices), or the first one of an account made now.
    /// </summary>
    /// <returns>The id of the bot account and the code.</returns>
    public async Task<(int BotId, BotLoginCodeDTO Code)> GetCodeAsync(int index, int? knownBotId,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var session = await ConnectAsync();
            var name = NameOf(index);

            if (knownBotId is { } botId)
            {
                var renewed = await PacedAsync(() => session.NewBotLoginCode(botId, endDevices: true), cancellationToken);
                if (renewed.Value is { } code)
                    return (botId, code);
                if (renewed.Error != ErrorCode.UserNotFound)
                    throw new InvalidOperationException($"{name}: no new login code: {renewed}");
            }

            var created = await PacedAsync(() => session.CreateBot(name, options.DebugBots), cancellationToken);
            if (created.IsSuccess)
            {
                LogMade(name, options.DebugBots ? "debug bot" : "bot");
                return (created.Value.Bot.Id, created.Value.LoginCode);
            }

            if (created.Error != ErrorCode.NameTaken)
                throw new InvalidOperationException($"{name}: the bot was not made: {created}");

            // The account is there but the database of the test bot forgot it
            var mine = await FindMineAsync(session, name);
            if (mine == null)
                throw new InvalidOperationException($"{name}: the name is taken by someone who is not a bot of the owner");

            var renewedCode = await PacedAsync(() => session.NewBotLoginCode(mine.Value, endDevices: true), cancellationToken);
            return renewedCode.Value is { } value
                ? (mine.Value, value)
                : throw new InvalidOperationException($"{name}: no new login code: {renewedCode}");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Deletes the bots of the test bot the owner made and forgets their devices.</summary>
    /// <returns>How many were deleted.</returns>
    public async Task<int> DeleteBotsAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var session = await ConnectAsync();
            var ownerId = session.GetCurrentUser().Id;
            var bots = await session.GetBots();
            if (bots.Value is not { } list)
                throw new InvalidOperationException($"The bots could not be listed: {bots}");

            var deleted = 0;
            foreach (var bot in list.Where(bot => bot.OwnerUserId == ownerId && IndexOf(bot.Username) != null))
            {
                var result = await PacedAsync(() => session.DeleteBot(bot.Id), cancellationToken);
                if (!result.IsSuccess)
                {
                    LogNotDeleted(bot.Username, result.ToString());
                    continue;
                }

                await accounts.ForgetDeviceAsync(BotProfileId(options, IndexOf(bot.Username)!.Value));
                LogDeleted(bot.Username);
                deleted++;
            }

            return deleted;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Where the device of a bot account is kept.</summary>
    public static string BotProfileId(BotOptions options, int index) =>
        $"{options.Host}-{options.Port}-bot-{(index + 1).ToString(CultureInfo.InvariantCulture)}";

    private static int? IndexOf(string username) =>
        username.StartsWith(NamePrefix, StringComparison.Ordinal) &&
        int.TryParse(username.AsSpan(NamePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture,
            out var number) && number > 0
            ? number - 1
            : null;

    private async Task<int?> FindMineAsync(ICoordinatorSession session, string name)
    {
        var ownerId = session.GetCurrentUser().Id;
        var bots = await session.GetBots();
        return bots.Value?.FirstOrDefault(bot => bot.Username == name && bot.OwnerUserId == ownerId)?.Id;
    }

    private async Task<ICoordinatorSession> ConnectAsync()
    {
        if (_session is { ConnectionStatus: CoordinatorState.Connected })
            return _session;

        if (_session != null)
        {
            await _session.Disconnect();
            _session = null;
        }

        var session = ActivatorUtilities.CreateInstance<CoordinatorSession>(services);
        var saved = await accounts.LoadDeviceAsync(ProfileId);
        var state = CoordinatorState.DeviceEnded;

        if (saved != null)
        {
            state = await BotLogin.ConnectAsync(session, options, new DeviceLoginPlan(saved.DeviceId, saved.KeyName),
                message => LogOwner(message));
            if (state == CoordinatorState.DeviceEnded)
            {
                LogDeviceEnded();
                await accounts.ForgetDeviceAsync(ProfileId);
            }
        }

        if (state == CoordinatorState.DeviceEnded)
        {
            if (options.OwnerCode == null)
                throw new InvalidOperationException(
                    "Bot accounts are made by an account that may manage bots: give its login code from the control " +
                    "panel with --owner-code once");

            state = await BotLogin.ConnectAsync(session, options, new LoginCodeLoginPlan(options.OwnerCode),
                message => LogOwner(message));
        }

        if (state != CoordinatorState.Connected)
        {
            await session.Disconnect();
            throw new InvalidOperationException($"The owner of the bots failed to connect: {state}");
        }

        if (BotLogin.DeviceOf(session) is { } device)
            await accounts.SaveDeviceAsync(ProfileId, device);

        var user = session.GetCurrentUser();
        if (!session.Permissions.Has(Permission.ManageBots))
        {
            await session.Disconnect();
            throw new InvalidOperationException(
                $"The owner {user.Username} may not manage bots: give one of their roles the permission");
        }

        LogOwner($"connected as {user.Username}");
        _session = session;
        return session;
    }

    /// <summary>Keeps the requests of the owner under the rate limit of their group; called under the lock.</summary>
    private async Task<T> PacedAsync<T>(Func<Task<T>> request, CancellationToken cancellationToken)
    {
        var wait = RequestSpacing - Stopwatch.GetElapsedTime(_lastRequest);
        if (_lastRequest != 0 && wait > TimeSpan.Zero)
            await Task.Delay(wait, cancellationToken);

        try
        {
            return await request();
        }
        finally
        {
            _lastRequest = Stopwatch.GetTimestamp();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_session != null)
            await _session.Disconnect();
        _lock.Dispose();
    }

    [LoggerMessage(LogLevel.Information, "[Owner] {Message}")]
    private partial void LogOwner(string message);

    [LoggerMessage(LogLevel.Information, "[Owner] made the {Kind} {Name}")]
    private partial void LogMade(string name, string kind);

    [LoggerMessage(LogLevel.Information, "[Owner] deleted {Name}")]
    private partial void LogDeleted(string name);

    [LoggerMessage(LogLevel.Warning, "[Owner] failed to delete {Name}: {Error}")]
    private partial void LogNotDeleted(string name, string error);

    [LoggerMessage(LogLevel.Warning, "[Owner] the saved device of the owner has ended, the login code is needed again")]
    private partial void LogDeviceEnded();
}

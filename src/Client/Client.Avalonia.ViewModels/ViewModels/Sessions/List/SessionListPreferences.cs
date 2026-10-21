using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Application.Ports.Repositories;
using Microsoft.Extensions.Logging;

namespace Client.ViewModels.Sessions.List;

/// <summary>
/// What the user chose for the list of a coordinator and is kept between runs: the muted chats and
/// the collapsed categories. A failed read gives nothing, a failed write is only logged.
/// </summary>
public sealed partial class SessionListPreferences(IMutedChatsRepository mutedChats,
    ICollapsedSectionsRepository collapsedSections, ILogger<SessionListPreferences> logger)
{
    private const string CollapsedCategoryKeyPrefix = "sessionCategory:";
    private readonly ILogger _logger = logger;

    public async Task<IReadOnlyCollection<int>> LoadMutedChatsAsync(string coordinatorInstanceId)
    {
        try
        {
            return (await mutedChats.GetAsync(coordinatorInstanceId)).ToHashSet();
        }
        catch (Exception exception)
        {
            LogMutedChatsLoadFailed(exception);
            return [];
        }
    }

    public async Task SaveChatMuteAsync(string coordinatorInstanceId, int chatId, bool isMuted)
    {
        try
        {
            await mutedChats.SetAsync(coordinatorInstanceId, chatId, isMuted);
        }
        catch (Exception exception)
        {
            LogChatMuteSaveFailed(exception, chatId);
        }
    }

    public async Task<IReadOnlyCollection<int>> LoadCollapsedCategoriesAsync(string coordinatorInstanceId)
    {
        var prefix = CollapsedCategoryKey(coordinatorInstanceId, 0)[..^1];
        try
        {
            return (await collapsedSections.GetAsync())
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(key => int.TryParse(key.AsSpan(prefix.Length), out var id) ? id : 0)
                .Where(id => id > 0)
                .ToHashSet();
        }
        catch (Exception exception)
        {
            LogCollapsedCategoriesLoadFailed(exception);
            return [];
        }
    }

    public async Task SaveCategoryCollapseAsync(string coordinatorInstanceId, int categoryId, bool isCollapsed)
    {
        try
        {
            await collapsedSections.SetAsync(CollapsedCategoryKey(coordinatorInstanceId, categoryId), isCollapsed);
        }
        catch (Exception exception)
        {
            LogCategoryCollapseSaveFailed(exception, categoryId);
        }
    }

    private static string CollapsedCategoryKey(string coordinatorInstanceId, int categoryId) =>
        $"{CollapsedCategoryKeyPrefix}{coordinatorInstanceId}:{categoryId}";

    [LoggerMessage(LogLevel.Error, "Failed to load the muted chats")]
    private partial void LogMutedChatsLoadFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to save whether chat {ChatId} is muted")]
    private partial void LogChatMuteSaveFailed(Exception exception, int chatId);

    [LoggerMessage(LogLevel.Error, "Failed to load the collapsed session categories")]
    private partial void LogCollapsedCategoriesLoadFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to save whether session category {CategoryId} is collapsed")]
    private partial void LogCategoryCollapseSaveFailed(Exception exception, int categoryId);
}

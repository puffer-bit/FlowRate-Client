using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media;
using Client.Application.Coordinator;
using Client.Core.Users;

namespace Client.Services;

/// <summary>
/// The avatars of users as the interface needs them: fetched from the coordinator once, kept in
/// the media cache and shared by every place that shows the same user.
/// </summary>
public interface IAvatarService
{
    /// <summary>
    /// The session avatars are downloaded through, or null when there is none. Avatars kept from
    /// earlier are still shown without it.
    /// </summary>
    void Attach(ICoordinatorSession? session);

    /// <summary>
    /// The picture at <see cref="UserModel.AvatarUrl"/>, or null when there is none or it cannot be had now.
    /// </summary>
    /// <param name="coordinatorInstanceId">Null means the coordinator of the attached session.</param>
    Task<IImage?> LoadAsync(string? coordinatorInstanceId, string? avatarUrl);

    /// <summary>Opens a still photo for editing; animations retain their original upload path.</summary>
    Task<AvatarEditSession?> OpenEditorAsync(Func<Task<Stream>> openContent);

    /// <summary>
    /// Cuts the middle square out of a picked image and reduces it. An animated one is kept as it
    /// is, so it still moves; the avatar shows its middle all the same.
    /// </summary>
    Task<(PreparedAvatar? Avatar, AvatarRejection Rejection)> PrepareAsync(Func<Task<Stream>> openContent);
}

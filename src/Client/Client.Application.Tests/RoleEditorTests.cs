using Client.Core.Coordinator;
using Client.ViewModels.ServerSettings;
using Shared.Enums;
using Xunit;

namespace Client.Application.Tests;

public sealed class RoleEditorTests
{
    [Fact]
    public Task EditingARole_PreservesAnExistingColorOutsideThePalette() => TestUiThread.RunAsync(() =>
    {
        var role = new RoleModel(2, "Moderators", 0x123456, 1, Permission.ManageMessages, false, 0);
        var admin = new RoleModel(3, "Admin", null, 2, Permission.Administrator, false, 0);
        var editor = new RoleEditorVM();
        editor.Load(role, new CoordinatorPermissions([role, admin], [admin.Id]));

        Assert.False(editor.IsDirty);
        Assert.Equal(role.Color, editor.SelectedColor);
        Assert.Contains(editor.Colors, color => color.IsSelected && color.Color == role.Color);

        editor.Name = "Helpers";
        Assert.True(editor.IsDirty);
        Assert.Equal(role.Color, editor.ToChange().Color);
        Assert.Equal(role.Permissions, editor.ToChange().Permissions);
    });

    [Fact]
    public Task FilteringPermissions_KeepsTheSelectedPermissions() => TestUiThread.RunAsync(() =>
    {
        var role = new RoleModel(2, "Members", null, 0, Permission.Speak | Permission.SendMessages, true, 0);
        var editor = new RoleEditorVM();
        editor.Load(role, new CoordinatorPermissions([role], []));
        editor.PermissionSearch = "nothing matches this text";
        Assert.True(editor.HasNoMatchingPermissions);
        Assert.Empty(editor.FilteredPermissions);
        Assert.Equal(role.Permissions, editor.ToChange().Permissions);
        Assert.False(editor.IsDirty);
        editor.PermissionSearch = "";
        Assert.Equal(editor.Permissions.Count, editor.FilteredPermissions.Count);
    });
}

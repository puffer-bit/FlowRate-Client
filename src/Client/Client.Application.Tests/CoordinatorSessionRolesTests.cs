using Client.Application.Coordinator;
using Client.Application.Tests.Fakes;
using Client.Core.Coordinator;
using Client.Core.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums;
using Xunit;

namespace Client.Application.Tests;

public class CoordinatorSessionRolesTests
{
    private readonly FakeGateway _gateway;
    private readonly CoordinatorSession _session;

    public CoordinatorSessionRolesTests()
    {
        (var gateway, _gateway) = FakeGateway.Create();
        _session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task Login_LoadsTheRolesAndThePermissionsOfTheUser()
    {
        _gateway.UserRoleIds = [2];

        await _session.Connect("server", 1, null, null);

        Assert.Equal(2, _session.Roles.Count);
        Assert.Equal(Permission.All, _session.Permissions.Granted);
    }

    [Fact]
    public async Task ChangedRoles_ChangeThePermissions()
    {
        await _session.Connect("server", 1, null, null);
        CoordinatorPermissions? announced = null;
        _session.PermissionsChanged += permissions => announced = permissions;

        _gateway.Raise(new RolesChangedEvent([new RoleModel(1, "@everyone", null, 0, Permission.ManageSessions, true, 1)]));

        Assert.NotNull(announced);
        Assert.True(_session.Permissions.Has(Permission.ManageSessions));
        Assert.False(_session.Permissions.Has(Permission.CreateSessions));
    }

    [Fact]
    public async Task NewRolesOfTheCurrentUser_ChangeThePermissions()
    {
        await _session.Connect("server", 1, null, null);
        Assert.False(_session.Permissions.Has(Permission.Administrator));

        _gateway.Raise(new UserUpdatedEvent(new UserModel
        {
            Id = 1, CoordinatorInstanceId = FakeGateway.CoordinatorId, Username = "alice", RoleIds = [2]
        }));

        Assert.True(_session.Permissions.Has(Permission.Administrator));
    }

    [Fact]
    public async Task AnotherUser_ChangesNothing()
    {
        await _session.Connect("server", 1, null, null);
        var announced = false;
        _session.PermissionsChanged += _ => announced = true;

        _gateway.Raise(new UserUpdatedEvent(new UserModel
        {
            Id = 7, CoordinatorInstanceId = FakeGateway.CoordinatorId, Username = "bob", RoleIds = [2]
        }));

        Assert.False(announced);
    }
}

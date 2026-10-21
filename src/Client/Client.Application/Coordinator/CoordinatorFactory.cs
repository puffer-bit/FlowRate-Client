using Client.Application.Coordinator.Users;
using Client.Application.Push;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Application.Coordinator;

public class CoordinatorFactory : ICoordinatorFactory
{
    private readonly IServiceProvider _provider;
    private readonly RememberedUsersRecorder _rememberedUsersRecorder;
    private readonly SavedAccountsRecorder _savedAccountsRecorder;
    private readonly PushRegistration _pushRegistration;

    public CoordinatorFactory(IServiceProvider provider, RememberedUsersRecorder rememberedUsersRecorder,
        SavedAccountsRecorder savedAccountsRecorder, PushRegistration pushRegistration)
    {
        _provider = provider;
        _rememberedUsersRecorder = rememberedUsersRecorder;
        _savedAccountsRecorder = savedAccountsRecorder;
        _pushRegistration = pushRegistration;
    }

    public ICoordinatorSession CreateCoordinatorSession()
    {
        var session = ActivatorUtilities.CreateInstance<CoordinatorSession>(_provider);
        _rememberedUsersRecorder.Track(session);
        _savedAccountsRecorder.Track(session);
        _pushRegistration.Track(session);

        return session;
    }
}
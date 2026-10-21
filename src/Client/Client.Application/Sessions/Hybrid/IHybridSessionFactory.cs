using Client.Core.Sessions.Hybrid;
using Client.Application.Coordinator;


namespace Client.Application.Sessions.Hybrid;

public interface IHybridSessionFactory
{
    /// <param name="isPlaybackEnabled">Whether incoming audio is played on the output device.</param>
    IHybridSession CreateHybridSession(HybridSessionModel hybridSessionModel, ICoordinatorSession coordinatorSession,
        bool isPlaybackEnabled = true);
}

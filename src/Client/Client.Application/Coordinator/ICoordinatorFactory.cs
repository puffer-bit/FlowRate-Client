namespace Client.Application.Coordinator;

public interface ICoordinatorFactory
{
    ICoordinatorSession CreateCoordinatorSession();
}

namespace Client.Application.Ports;

public interface IDatabaseInitializer
{
    Task<DatabaseState> VerifyDatabaseAsync();
}

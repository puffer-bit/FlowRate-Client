using Client.Core.Users;


namespace Client.ViewModels.Sessions;

public interface IParticipantVM
{
    UserModel UserData { get; }
}
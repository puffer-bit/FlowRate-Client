using System;

namespace Client.Services;

public partial class ApplicationLoader
{
    private bool LoadOpenAL()
    {
        try
        {
            _audioPlayerService.Init();
            
            return true;
        }
        catch (Exception exception)
        {
            LogOpenAlLoadFailed(exception);
            return false;
        }
    }
}
using System.Linq;
using Avalonia.Threading;
using Client.Core.Coordinator;
using Client.Avalonia.Controls.Media;

namespace Client.ViewModels.Sessions.Hybrid;

public partial class ActiveHybridSessionVM
{
    private void OnScreenViewerChanged(HybridSessionPeerEvent peerEvent)
    {
        if (peerEvent.SessionId != Model.Id)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (IsClosed)
                return;
            _screenAudience.Apply(peerEvent);
            UpdateScreenViewers();
        });
    }

    private void UpdateScreenViewers()
    {
        foreach (var stream in ScreenStreams)
            stream.SetViewers(_screenAudience.GetViewers(stream.UserId)
                .Select(userId => new ScreenViewerItem(userId, ResolveDisplayedName(userId), ResolveAvatarUrl(userId)))
                .OrderBy(viewer => viewer.Username, System.StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(viewer => viewer.UserId));
    }
}

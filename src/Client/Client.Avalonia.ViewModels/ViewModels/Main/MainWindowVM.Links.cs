using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Links;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Services;
using Client.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;

namespace Client.ViewModels.Main;

public partial class MainWindowVM
{
    // Links that come while the application starts wait for the start to be over
    private readonly TaskCompletionSource _startupOver = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _isListeningForLinks;

    /// <summary>
    /// Follows the links the system hands over from now on, and takes the one the application was started with.
    /// </summary>
    private FlowRateLink? ListenForLinks()
    {
        if (!_isListeningForLinks)
        {
            _isListeningForLinks = true;
            IncomingLinks.Received += link => Dispatcher.UIThread.Post(() => _ = OpenLinkWhenStartedAsync(link));
        }

        return IncomingLinks.TakePending();
    }

    /// <summary>Whether the link takes the place of the connection the start makes on its own.</summary>
    private static bool ReplacesStartupConnection(FlowRateLink? link) => link is ConnectLink or LoginLink;

    private async Task OpenLinkAfterStartupAsync(FlowRateLink link, Task connecting, System.Threading.CancellationToken skipped)
    {
        try
        {
            if (!skipped.IsCancellationRequested)
                await connecting;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The start connection failed before a link was opened");
        }
        finally
        {
            _startupOver.TrySetResult();
        }

        await OpenLinkAsync(link);
    }

    private async Task OpenLinkWhenStartedAsync(FlowRateLink link)
    {
        await _startupOver.Task;
        await OpenLinkAsync(link);
    }

    /// <summary>
    /// Does what a link or a scanned code asks, up to the point the user confirms: a server or a login opens the
    /// connect form filled in, a new device opens its approval.
    /// </summary>
    public async Task OpenLinkAsync(FlowRateLink link)
    {
        try
        {
            switch (link)
            {
                case ConnectLink or LoginLink:
                    await ConnectToCoordinator(null, window => window.OpenLink(link));
                    break;
                case DeviceLinkApproval approval:
                    await ApproveDeviceLinkAsync(approval);
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not open a link");
        }
    }

    /// <summary>Opens what the text of a scanned QR code asks for.</summary>
    /// <returns>False when the code is not one of the application.</returns>
    public bool OpenScanned(string? text)
    {
        if (FlowRateLinks.Parse(text) is not { } link)
            return false;

        _ = OpenLinkAsync(link);
        return true;
    }

    private async Task ApproveDeviceLinkAsync(DeviceLinkApproval approval)
    {
        // Only a device logged in to the account can add another one to it
        if (_coordinatorSession is not { ConnectionStatus: CoordinatorState.Connected })
        {
            await ShowMessageBox.Handle(new MessageBoxVM(Icon.Info, Buttons.Ok,
                approval.Host is { } host
                    ? Strings.Format("link.approve.notConnected.server", ("server", host))
                    : Strings.Get("link.approve.notConnected"),
                Strings.Get("accountSecurity.link.title")));
            return;
        }

        await _users.ApproveDeviceLink(approval.Code);
    }
}

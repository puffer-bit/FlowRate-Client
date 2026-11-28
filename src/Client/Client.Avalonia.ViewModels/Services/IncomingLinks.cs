using System;
using Client.Application.Links;

namespace Client.Services;

/// <summary>
/// The <c>flowrate://</c> links the system hands to the application: the one it was started with, and those that come
/// while it runs. The host posts them as they arrive, which may be before the main window exists; the main window
/// takes the link of the start before it connects anywhere, and then follows <see cref="Received"/>.
/// </summary>
public static class IncomingLinks
{
    private static readonly object Gate = new();
    private static FlowRateLink? _pending;
    private static Action<FlowRateLink>? _received;

    /// <summary>
    /// Raised for a link that comes once a handler is there; a link posted before waits in <see cref="TakePending"/>.
    /// </summary>
    public static event Action<FlowRateLink>? Received
    {
        add
        {
            lock (Gate)
                _received += value;
        }
        remove
        {
            lock (Gate)
                _received -= value;
        }
    }

    /// <summary>Hands a link over from any thread. Text that is not a link of the application is ignored.</summary>
    /// <returns>Whether the text was a link.</returns>
    public static bool Post(string? text)
    {
        if (FlowRateLinks.Parse(text) is not { } link)
            return false;

        Action<FlowRateLink>? received;
        lock (Gate)
        {
            received = _received;
            // Only the newest one is kept: the user acts on the last link opened
            if (received == null)
                _pending = link;
        }

        received?.Invoke(link);
        return true;
    }

    /// <summary>The link posted before anyone listened, taken off so it is handled once.</summary>
    public static FlowRateLink? TakePending()
    {
        lock (Gate)
        {
            var link = _pending;
            _pending = null;
            return link;
        }
    }

    /// <summary>Whether a link waits to be taken, so the start does not connect on its own meanwhile.</summary>
    public static bool HasPending
    {
        get
        {
            lock (Gate)
                return _pending != null;
        }
    }
}

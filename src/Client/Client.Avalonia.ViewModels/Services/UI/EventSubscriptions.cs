using System;
using System.Collections.Generic;

namespace Client.Services;

/// <summary>
/// Event handlers taken off together. Each handler is added with the code that removes it, so the
/// two lists of += and -= cannot drift apart.
/// </summary>
public sealed class EventSubscriptions : IDisposable
{
    private readonly List<Action> _unsubscribes = new();

    public bool IsEmpty => _unsubscribes.Count == 0;

    /// <summary>
    /// Subscribes at once and keeps the unsubscription for <see cref="Clear"/>.
    /// </summary>
    public EventSubscriptions Add(Action subscribe, Action unsubscribe)
    {
        subscribe();
        _unsubscribes.Add(unsubscribe);
        return this;
    }

    /// <summary>
    /// Takes every handler off, the last added first; the set can be filled again afterwards.
    /// </summary>
    public void Clear()
    {
        for (int i = _unsubscribes.Count - 1; i >= 0; i--)
            _unsubscribes[i]();
        _unsubscribes.Clear();
    }

    public void Dispose() => Clear();
}

namespace Client.GStreamer.Interop;

/// <summary>
/// Wrappers that have to live as long as their owner and are disposed together with it. A wrapper
/// that is merely dropped keeps its native object until it is collected and the release queue of
/// the binding is drained, see <see cref="GstBusWatcher"/>.
/// </summary>
internal sealed class WrapperScope
{
    private readonly List<IDisposable> _wrappers = [];

    public T Hold<T>(T wrapper) where T : IDisposable
    {
        lock (_wrappers)
            _wrappers.Add(wrapper);

        return wrapper;
    }

    public void Release()
    {
        IDisposable[] held;

        lock (_wrappers)
        {
            held = _wrappers.ToArray();
            _wrappers.Clear();
        }

        foreach (var wrapper in held)
            wrapper.Dispose();
    }
}

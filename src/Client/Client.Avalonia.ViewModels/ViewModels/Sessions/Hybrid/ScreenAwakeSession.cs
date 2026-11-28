using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Client.Application.Ports;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>One display lease per connected session, independent of tile and fullscreen views.</summary>
public sealed class ScreenAwakeSession : IDisposable
{
    private readonly ObservableCollection<ScreenStreamVM> _streams;
    private readonly IScreenAwakeService _service;
    private readonly HashSet<ScreenStreamVM> _observed = [];
    private IDisposable? _lease;
    private bool _connected;
    private bool _disposed;

    public ScreenAwakeSession(ObservableCollection<ScreenStreamVM> streams, IScreenAwakeService service)
    {
        _streams = streams;
        _service = service;
        streams.CollectionChanged += OnCollectionChanged;
        ObserveStreams();
    }

    public void SetConnected(bool connected)
    {
        _connected = connected;
        Update();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ObserveStreams();

    private void ObserveStreams()
    {
        foreach (var stream in _observed.Where(stream => !_streams.Contains(stream)).ToArray())
        {
            stream.PropertyChanged -= OnStreamChanged;
            _observed.Remove(stream);
        }
        foreach (var stream in _streams)
            if (_observed.Add(stream)) stream.PropertyChanged += OnStreamChanged;
        Update();
    }

    private void OnStreamChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScreenStreamVM.IsWatched) or nameof(ScreenStreamVM.IsLeaving) or null or "")
            Update();
    }

    private void Update()
    {
        var required = !_disposed && _connected && _streams.Any(stream => stream.IsWatched && !stream.IsLeaving);
        if (required) _lease ??= _service.Acquire();
        else
        {
            _lease?.Dispose();
            _lease = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _streams.CollectionChanged -= OnCollectionChanged;
        foreach (var stream in _observed) stream.PropertyChanged -= OnStreamChanged;
        _observed.Clear();
        Update();
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.Services.ScreenCast.Linux;

/// <summary>
/// Lists the applications with a playback stream in PipeWire, from what pw-dump reports. The list
/// is read again every few seconds while it is watched, and not at all otherwise.
/// </summary>
/// <remarks>
/// The tool comes with PipeWire, while the native registry would need its own main loop and
/// versioned callback tables. An application is told by its executable: a browser opens a stream
/// per tab that plays, from a process of its own, and they are all one application to the user.
/// The process data comes from the stream, or else from the client that created it, which is
/// where the nodes GStreamer creates keep it.
/// </remarks>
public sealed partial class PipeWireAudioAppProvider : IAudioAppProvider
{
    private const string Tool = "pw-dump";
    private const string PlaybackStreamClass = "Stream/Output/Audio";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<PipeWireAudioAppProvider> _logger;
    private readonly Lock _lock = new();
    private Timer? _timer;
    private int _watchers;
    private int _isRefreshing;
    private bool _isToolMissing;

    public IReadOnlyList<AudioApp> Apps { get; private set; } = [];

    public event Action? Changed;

    public PipeWireAudioAppProvider(ILogger<PipeWireAudioAppProvider> logger)
    {
        _logger = logger;
    }

    public IDisposable Watch()
    {
        lock (_lock)
        {
            if (_watchers++ == 0)
                _timer = new Timer(_ => Refresh(), null, TimeSpan.Zero, RefreshInterval);
        }

        return new Watcher(this);
    }

    private void Unwatch()
    {
        lock (_lock)
        {
            if (--_watchers > 0)
                return;

            _timer?.Dispose();
            _timer = null;
        }
    }

    // Runs on the timer; a read that takes longer than the interval is not started twice
    private void Refresh()
    {
        if (_isToolMissing || Interlocked.Exchange(ref _isRefreshing, 1) == 1)
            return;

        try
        {
            var apps = Read();

            if (apps == null || IsSame(apps, Apps))
                return;

            Apps = apps;
            Changed?.Invoke();
        }
        catch (Exception exception)
        {
            LogReadFailed(exception);
        }
        finally
        {
            Volatile.Write(ref _isRefreshing, 0);
        }
    }

    /// <returns>Null when the tool did not answer.</returns>
    private List<AudioApp>? Read()
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(Tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            _isToolMissing = true;
            LogToolMissing(exception, Tool);
            return null;
        }

        // Read before waiting: a full pipe would block the tool forever
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        var output = process.StandardOutput.ReadToEndAsync();

        if (!output.Wait(ToolTimeout) || !process.WaitForExit(ToolTimeout))
        {
            process.Kill();
            LogToolTimedOut(Tool);
            return null;
        }

        return Parse(output.Result, Environment.ProcessId);
    }

    /// <param name="ownProcessId">Our process, whose streams are never offered.</param>
    internal static List<AudioApp> Parse(string json, int ownProcessId)
    {
        var documents = ParseObjects(json);

        try
        {
            return Parse(documents.Select(document => document.RootElement), ownProcessId);
        }
        finally
        {
            foreach (var document in documents)
                document.Dispose();
        }
    }

    /// <summary>
    /// The objects of the array pw-dump writes, each read on its own. While the graph changes under it,
    /// pw-dump may write an object into the middle of another, or a second array after the first, and
    /// one broken object would cost the whole list. Its layout puts every object of the array between
    /// a line "  {" and a line "  }", so a broken one is left out instead.
    /// </summary>
    private static List<JsonDocument> ParseObjects(string json)
    {
        var documents = new List<JsonDocument>();
        var start = -1;
        var position = 0;

        while (position < json.Length)
        {
            var end = json.IndexOf('\n', position);
            if (end < 0)
                end = json.Length;

            var line = json.AsSpan(position, end - position);

            if (line is "  {")
                start = position;
            else if (start >= 0 && line is "  }" or "  },")
            {
                try
                {
                    documents.Add(JsonDocument.Parse(json.AsMemory(start, position + 3 - start)));
                }
                catch (JsonException)
                {
                    // Broken, see above
                }

                start = -1;
            }

            position = end + 1;
        }

        return documents;
    }

    private static List<AudioApp> Parse(IEnumerable<JsonElement> items, int ownProcessId)
    {
        var clients = new Dictionary<long, JsonElement>();
        var streams = new List<(JsonElement Props, uint Serial)>();

        foreach (var item in items)
        {
            if (!item.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object ||
                !info.TryGetProperty("props", out var props) || props.ValueKind != JsonValueKind.Object)
                continue;

            var type = item.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;

            if (type == "PipeWire:Interface:Client" && item.TryGetProperty("id", out var id) && id.TryGetInt64(out var clientId))
                clients[clientId] = props;
            else if (type == "PipeWire:Interface:Node" && String(props, "media.class") == PlaybackStreamClass &&
                     Number(props, "object.serial") is { } serial)
                streams.Add((props, (uint)serial));
        }

        var found = new List<(string Key, string Name, uint Serial)>();

        foreach (var (props, serial) in streams)
        {
            JsonElement? client = Number(props, "client.id") is { } clientId && clients.TryGetValue(clientId, out var value)
                ? value
                : null;

            string? Read(string name) => String(props, name) ?? (client is { } owner ? String(owner, name) : null);

            if (Number(props, "application.process.id") is { } pid && pid == ownProcessId ||
                client is { } creator && Number(creator, "application.process.id") is { } clientPid && clientPid == ownProcessId)
                continue;

            var binary = Read("application.process.binary");
            var name = Read("application.name") ?? binary ?? String(props, "node.name");
            var key = binary ?? name;

            if (key == null || name == null)
                continue;

            found.Add((key, name, serial));
        }

        return found
            .GroupBy(stream => stream.Key)
            .Select(group => new AudioApp(group.Key, group.First().Name,
                group.Select(stream => stream.Serial).Order().ToList()))
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // pw-dump writes some numbers as strings, depending on the version
    private static long? Number(JsonElement props, string name) =>
        !props.TryGetProperty(name, out var value) ? null
        : value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number
        : value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed) ? parsed
        : null;

    private static string? String(JsonElement props, string name) =>
        props.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static bool IsSame(IReadOnlyList<AudioApp> left, IReadOnlyList<AudioApp> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.Key == pair.Second.Key && pair.First.Name == pair.Second.Name &&
            pair.First.StreamSerials.SequenceEqual(pair.Second.StreamSerials));

    private sealed class Watcher(PipeWireAudioAppProvider owner) : IDisposable
    {
        private int _isDisposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
                owner.Unwatch();
        }
    }
}

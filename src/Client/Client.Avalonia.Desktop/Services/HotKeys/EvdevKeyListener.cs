using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Client.Core.Settings;

namespace Client.Services.HotKeys;

/// <summary>
/// Reads the keyboards from /dev/input, which works under X11 and Wayland alike, but only for a user
/// of the input group. The devices are only read and never grabbed, so every key still reaches the
/// application it is pressed in. Keyboards plugged in later are picked up.
/// </summary>
internal sealed class EvdevKeyListener : IGlobalKeyListener
{
    private const ushort KeyEventType = 1;
    private const int RescanMilliseconds = 3000;
    private const int PollMilliseconds = 500;

    // struct input_event: a timeval of two longs, then type, code and value
    private static readonly int EventSize = IntPtr.Size * 2 + 8;

    private readonly GlobalKeyHandler _onKey;
    private readonly Dictionary<string, int> _devices = [];
    private readonly Thread _thread;
    private volatile bool _isStopping;

    public GlobalHotKeysBackend Kind => GlobalHotKeysBackend.Evdev;

    private EvdevKeyListener(GlobalKeyHandler onKey)
    {
        _onKey = onKey;
        _thread = new Thread(Run) { IsBackground = true, Name = "Hot keys (evdev)" };
    }

    /// <summary>Null when no keyboard can be read.</summary>
    public static EvdevKeyListener? TryStart(GlobalKeyHandler onKey)
    {
        var listener = new EvdevKeyListener(onKey);
        listener.OpenNewDevices();
        if (listener._devices.Count == 0)
            return null;

        listener._thread.Start();
        return listener;
    }

    private void OpenNewDevices()
    {
        foreach (var path in KeyboardPaths())
        {
            if (_devices.ContainsKey(path))
                continue;

            var fd = Libc.Open(path, Libc.ReadOnly | Libc.NonBlocking | Libc.CloseOnExec);
            if (fd >= 0)
                _devices[path] = fd;
        }
    }

    /// <summary>The devices the kernel gives a keyboard handler: keyboards and whatever has keys.</summary>
    private static IEnumerable<string> KeyboardPaths()
    {
        string text;
        try
        {
            text = File.ReadAllText("/proc/bus/input/devices");
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var line in text.Split('\n'))
        {
            if (!line.StartsWith("H: Handlers=", StringComparison.Ordinal))
                continue;

            var handlers = line["H: Handlers=".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!handlers.Contains("kbd"))
                continue;

            var device = handlers.FirstOrDefault(handler => handler.StartsWith("event", StringComparison.Ordinal));
            if (device != null)
                yield return "/dev/input/" + device;
        }
    }

    private unsafe void Run()
    {
        var buffer = new byte[EventSize * 64];
        var lastScan = Environment.TickCount64;

        while (!_isStopping)
        {
            if (Environment.TickCount64 - lastScan > RescanMilliseconds)
            {
                OpenNewDevices();
                lastScan = Environment.TickCount64;
            }

            var devices = _devices.ToArray();
            var fds = new Libc.PollFd[devices.Length];
            for (var i = 0; i < devices.Length; i++)
                fds[i] = new Libc.PollFd { Fd = devices[i].Value, Events = Libc.PollIn };

            int ready;
            fixed (Libc.PollFd* pointer = fds)
                ready = Libc.Poll(pointer, (nuint)fds.Length, PollMilliseconds);
            if (ready <= 0)
                continue;

            for (var i = 0; i < fds.Length; i++)
            {
                var events = fds[i].ReturnedEvents;
                if ((events & (Libc.PollError | Libc.PollHangUp | Libc.PollInvalid)) != 0)
                {
                    // Unplugged
                    Libc.Close(devices[i].Value);
                    _devices.Remove(devices[i].Key);
                    continue;
                }

                if ((events & Libc.PollIn) != 0)
                    ReadEvents(devices[i].Value, buffer);
            }
        }

        foreach (var fd in _devices.Values)
            Libc.Close(fd);
        _devices.Clear();
    }

    private unsafe void ReadEvents(int fd, byte[] buffer)
    {
        nint count;
        fixed (byte* pointer = buffer)
            count = Libc.Read(fd, pointer, buffer.Length);

        for (var offset = 0; offset + EventSize <= count; offset += EventSize)
        {
            var header = offset + IntPtr.Size * 2;
            var type = BitConverter.ToUInt16(buffer, header);
            if (type != KeyEventType)
                continue;

            var code = BitConverter.ToUInt16(buffer, header + 2);
            var value = BitConverter.ToInt32(buffer, header + 4);

            // 0 is a release, 1 a press, 2 a repeat of a held key
            _onKey(LinuxKeyCodes.ToPhysicalKey(code), value != 0, value == 2);
        }
    }

    public void Dispose()
    {
        _isStopping = true;
        _thread.Join();
    }
}

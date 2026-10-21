using System.Runtime.InteropServices;

namespace Client.Services.HotKeys;

internal static partial class Libc
{
    private const string Library = "libc";

    public const int ReadOnly = 0;
    public const int NonBlocking = 0x800;
    public const int CloseOnExec = 0x80000;

    public const short PollIn = 0x1;
    public const short PollError = 0x8;
    public const short PollHangUp = 0x10;
    public const short PollInvalid = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short ReturnedEvents;
    }

    [LibraryImport(Library, EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial int Open(string path, int flags);

    [LibraryImport(Library, EntryPoint = "close")]
    public static partial int Close(int fd);

    [LibraryImport(Library, EntryPoint = "read", SetLastError = true)]
    public static unsafe partial nint Read(int fd, byte* buffer, nint count);

    [LibraryImport(Library, EntryPoint = "poll", SetLastError = true)]
    public static unsafe partial int Poll(PollFd* fds, nuint count, int timeoutMilliseconds);
}

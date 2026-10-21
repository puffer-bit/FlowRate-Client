using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;

namespace Client.OpenAL.Services.AudioPlayerService;

/// <summary>
/// The OpenAL library, loaded by the runtime itself. The loader of Silk.NET looks for the file through
/// the paths of managed assemblies, which a single-file or NativeAOT build does not have.
/// </summary>
internal sealed class OpenALLibrary : INativeContext
{
    // The installed OpenAL first, as Silk.NET does, then OpenAL Soft that ships next to the executable
    private static readonly string[] Names = OperatingSystem.IsWindows()
        ? ["openal32.dll", "soft_oal.dll"]
        : OperatingSystem.IsMacOS()
            ? ["/System/Library/Frameworks/OpenAL.framework/OpenAL", "libopenal.dylib"]
            : ["libopenal.so.1", "libopenal.so"];

    private nint _handle;

    public OpenALLibrary()
    {
        foreach (var name in Names)
        {
            if (NativeLibrary.TryLoad(name, typeof(OpenALLibrary).Assembly, null, out _handle))
                return;
        }

        throw new DllNotFoundException($"OpenAL is not installed and is not next to the application ({string.Join(", ", Names)})");
    }

    public nint GetProcAddress(string proc, int? slot = default)
    {
        return NativeLibrary.GetExport(_handle, proc);
    }

    public bool TryGetProcAddress(string proc, out nint addr, int? slot = default)
    {
        return NativeLibrary.TryGetExport(_handle, proc, out addr);
    }

    public void Dispose()
    {
        if (_handle == 0)
            return;

        NativeLibrary.Free(_handle);
        _handle = 0;
    }
}

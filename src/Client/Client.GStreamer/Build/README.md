# Building the GStreamer runtime shipped with the client

The client does not use a GStreamer installed on the system: it loads its own from the `gstreamer/`
folder next to the executable (`Loader/GstNativeLibraryResolver.cs`, `Loader/GstLoader.cs`).
The scripts here produce that folder. Nothing is installed on the build machine.

The build contains no GPL components (no x264/x265, no libav/ffmpeg, `-Dgpl=disabled`). The LGPL
terms are met: the libraries stay separate, replaceable files, the license texts are in `licenses/`
and the component list is in `THIRD-PARTY-NOTICES.txt`.

## Linux (linux-x64)

Only Docker is required (the user must be in the `docker` group, no sudo needed).

```bash
cd src/Client/Client.GStreamer/Build/linux
./build.sh            # build, collect and verify; repeated runs reuse the Docker layer cache
./build.sh --clean    # remove the build image and the Docker build cache (5-10 GB)
```

The first run takes 30-60 minutes. Output:

| Path | Contents |
|---|---|
| `artifacts/linux-x64/gstreamer/` | the folder that ships with the client |
| `artifacts/linux-x64/licenses/`, `THIRD-PARTY-NOTICES.txt` | licenses |
| `artifacts/gstreamer-<version>-linux-x64.tar.zst` | the same as one archive |

`artifacts/` is not tracked by git.

### Files

| File | Purpose |
|---|---|
| `Dockerfile` | builds on Ubuntu 22.04: dav1d, SVT-AV1, GStreamer, the PipeWire plugin, gst-plugins-rs. Versions are the `ARG`s at the top |
| `meson-options.txt` | the GStreamer plugin set. Everything not listed is disabled |
| `required-elements.txt` | elements the bundle must contain (`?` marks optional ones). Keep in sync with `Loader/GstElementCheck.cs` |
| `collect.sh` | copies the installed build into the `gstreamer/` layout, adds non-system dependencies, sets RPATH to `$ORIGIN`, collects licenses |
| `verify.sh` | checks the bundle in a clean Ubuntu 22.04 without GStreamer: dependencies, elements, plugin licenses, and that everything loads from the bundle only |

### Compatibility

Building on Ubuntu 22.04 means glibc 2.35: the bundle runs on Ubuntu 22.04+, Debian 12+,
Fedora 36+ and distributions of the same age.

These stay system libraries: glibc, libstdc++, zlib, the PulseAudio/ALSA/PipeWire client
libraries, VA-API/DRM and the graphics stack. They are tied to the daemons and drivers of the
particular system. Without libpipewire the `pipewiresrc` plugin does not load; everything else works.

The VA-API plugin (hardware H.264/AV1/VP9 encoding on AMD and Intel) ships in two builds, and
`Loader/GstLoader.cs` puts one of them on the plugin path at startup:

| Folder | Built against | Picked when | Notes |
|---|---|---|---|
| `lib/gstreamer-1.0-va` | libva 2.20 headers | the system libva lacks `vaMapBuffer2` (< 2.21) | has `vaav1enc`, calls nothing newer than libva 2.14 |
| `lib/gstreamer-1.0-va-modern` | libva 2.22 headers | the system libva has `vaMapBuffer2` (2.21+) | also queries the encoder surface alignment AV1 on AMD needs |

libva itself is never bundled: it must match the VA drivers installed on the system.
`build.sh` verifies the bundle on Ubuntu 22.04 (base build) and Debian 13 (modern build).

### Adding a plugin

1. Enable it in `meson-options.txt` (`-Dgst-plugins-bad:<plugin>=enabled`).
2. If the client relies on it, add its elements to `required-elements.txt` and `GstElementCheck.cs`.
3. Check the license: `verify.sh` fails on a GPL plugin, `collect.sh` on a GPL library.
4. Run `./build.sh`.

## Windows (win-x64)

Run on Windows, no administrator rights needed:

```powershell
cd src\Client\Client.GStreamer\Build\windows
powershell -ExecutionPolicy Bypass -File collect.ps1
```

The runtime is taken unmodified from the official GStreamer MSVC build.

| File | Purpose |
|---|---|
| `collect.ps1` | the official installer (Inno Setup) cannot be unpacked without running it, so it installs it for the current user into a temporary folder, copies the plugins listed in `$plugins` together with the DLLs they import, collects licenses, uninstalls again, runs `verify.ps1` and packs the result |
| `verify.ps1` | checks the bundle with the environment the client sets and a PATH holding only the bundle and Windows: elements from `required-elements.txt`, every plugin loads from the bundle, no GPL |
| `required-elements.txt` | elements the bundle must contain (`?` marks optional ones). Keep in sync with `Loader/GstElementCheck.cs` |

Output: `artifacts/win-x64/` and `artifacts/gstreamer-<version>-win-x64.zip`. To add a plugin, add its
name to `$plugins` in `collect.ps1` (file `gst<name>.dll`) and its elements to `required-elements.txt`.

`collect.ps1` verifies on the build machine. For a check on a clean system, copy `artifacts\win-x64`
and the `windows` folder into Windows Sandbox and run there:

```powershell
powershell -ExecutionPolicy Bypass -File verify.ps1 -Bundle <path to win-x64>
```

## Publishing the client

`Build/GStreamerRuntime.targets` is imported by `Client.Avalonia.Desktop.csproj` and copies
`artifacts/<rid>/` next to the executable on every build and publish (`gstreamer/`, `licenses/`,
`THIRD-PARTY-NOTICES.txt`). It lives in the executable, not in Client.GStreamer, because a library does
not receive the RID of the application and would ship the runtime of the build machine. A Release build
fails when the runtime for its RID is missing; a Debug build without it falls back to an installed
GStreamer.

There are two publish profiles per platform, `<rid>-jit` and `<rid>-aot`
(`Client.Avalonia.Desktop/Properties/PublishProfiles`).

A JIT publish is a single executable (the profile sets `PublishSingleFile`) with the .NET runtime
and the managed assemblies inside. These files are never bundled and stay next to it: the native
libraries (Skia, HarfBuzz, ANGLE, SQLite; extracting them would leave a copy per version in a cache
folder), the GStreamer runtime (marked `ExcludeFromSingleFile` here, GStreamer loads plugins by path)
and the LGPL libraries the user must be able to replace (the GstSharp.Net assemblies and OpenAL
Soft; see `LgplLibraries` in the project).

An AOT publish (NativeAOT) is one native executable with no .NET runtime and no IL inside; it starts
at once. The native libraries and the GStreamer runtime stay next to it in the same way, but the
GStreamer binding is compiled in: it is no longer a file to replace, the client is rebuilt from its
source instead. NativeAOT does not compile for another system, so the Linux build is made on Linux
(clang or gcc) and the Windows build on Windows (the C++ build tools of Visual Studio).

The `publish` scripts in `src/Scripts` do everything in one go and put one archive per platform in `dist/`
(a single `FlowRateClient-<version>` folder inside). The result is self-contained: it needs neither .NET
nor GStreamer on the target machine.

```bash
./src/Scripts/bash/publish.sh                          # on Linux: linux-x64
./src/Scripts/bash/publish.sh --target win-x64         # on Linux: Windows build
./src/Scripts/bash/publish.sh --target all             # on Linux: both and the Android .apk
./src/Scripts/bash/publish.sh --aot                    # on Linux: linux-x64 compiled ahead of time
```

```powershell
.\src\Scripts\powershell\publish.ps1                       # on Windows: win-x64
.\src\Scripts\powershell\publish.ps1 -Target linux-x64     # on Windows: Linux build
.\src\Scripts\powershell\publish.ps1 -Target all           # on Windows: both and the Android .apk
.\src\Scripts\powershell\publish.ps1 -Aot                  # on Windows: win-x64 compiled ahead of time
```

### Building for the other platform

The client itself cross-publishes from either system. The GStreamer runtime does not: the Linux one is
built in Docker, the Windows one from the official installer, which only runs on Windows. The runtime
depends on the GStreamer version, not on the client code, so it has to be made again only when the
version or the plugin set changes:

- Windows build on Linux: run `windows/collect.ps1` once on any Windows machine and copy its
  `artifacts/win-x64` folder to `artifacts/` here.
- Linux build on Windows: run `linux/build.sh` on Linux (or in WSL with Docker) and copy its
  `artifacts/linux-x64` folder to `artifacts\` there.

NTFS has no executable bit, so `publish.ps1` packs the Linux archive through `tar.exe` from an mtree
file list that sets mode 0755 on every ELF file (the executable, createdump, gst-plugin-scanner, shared
objects). Unpacked on Linux, the client starts as is.

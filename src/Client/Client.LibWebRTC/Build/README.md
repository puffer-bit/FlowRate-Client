# Building the libwebrtc runtime shipped with the client

The libwebrtc voice (see `../README.md`) needs one native library next to the executable:
`libflowrate_webrtc.so` on Linux, `flowrate_webrtc.dll` on Windows. It is libwebrtc, the C++ wrapper of
[webrtc-sdk/libwebrtc](https://github.com/webrtc-sdk/libwebrtc) and the C interface of
`Native/flowrate_webrtc.h`, built from source and linked into one file. GStreamer owns desktop
audio capture and playback; `Native/flowrate_audio.cc` supplies the clock-driven PCM boundary.

The library is built by hand on a machine of its platform and **committed** into `prebuilt/<rid>/`.
Neither CI nor the publish scripts build it, they only copy it. Each folder also holds `BUILD-INFO.txt`
(what the library was built from, its SHA-256) and `LICENSE.libwebrtc`. Rebuild and commit it when a
change of the C interface or of the pinned sources is done, not after every edit: each version stays in
the history of the repository. Without the library of a platform the client still runs, and the voice
there goes over GStreamer.

What goes in, pinned by both scripts:

| Source | Version |
|---|---|
| WebRTC, the fork of webrtc-sdk | `m150_release`, commit `a545de4` |
| webrtc-sdk/libwebrtc, the C++ wrapper | tag `libwebrtc.m150.7871.06` |
| patches of the wrapper to WebRTC | `custom_audio_source_m150`, `add_libwebrtc_build_target`, and `fix_desktop_capture_compile` (Linux) |
| `patches/flowrate_target.patch` | names the library `flowrate_webrtc`, adds the FlowRate native sources, replaces the platform ADM with the external PCM device and declares the test target of the wrapper only with the tests of WebRTC |
| `patches/flowrate_keep_sps.patch` | leaves the SPS of an H.264 screen as the encoder wrote it, sent and received: the picture buffer of one frame WebRTC writes into it made the decoders of GStreamer hold every picture until the next one came, a frame of latency at either end that went through libwebrtc |

Left out: the desktop capture of the wrapper, H.264 of WebRTC, the audio devices of the platform
(`rtc_include_internal_audio_device=false`: PulseAudio and ALSA, WASAPI and the Voice Capture DMO)
and, on Windows, the H.265 coder of Media Foundation. Screens are captured and encoded by GStreamer,
and the sound is played and recorded by it. The C++ runtime is linked in
statically, so the library needs only the system: glibc on Linux, Windows itself on Windows (no
Visual C++ redistributable).

Both scripts keep the checkout outside the repository in `FLOWRATE_WEBRTC_DIR`, about 30 GB. The first
run downloads it with depot_tools (Chromium's gclient, gn, ninja, clang) and builds about 4000 files.
Later runs reuse it and rebuild only what changed: a few seconds after an edit of
`flowrate_peer.cc`. Each script checks that every `FR_API` function of the header is exported and
that no library beyond the system is needed.

## Linux (linux-x64)

Needs git, curl and python3. depot_tools brings the rest, including a Debian sysroot, so the result
does not depend on the distribution of the build machine.

```bash
src/Client/Client.LibWebRTC/Build/linux/build.sh
```

The checkout goes into `~/.cache/flowrate-libwebrtc` by default. The first run takes about an hour on
16 cores. The output is `prebuilt/linux-x64/libflowrate_webrtc.so` (about 16 MB, stripped).

## Windows (win-x64)

Run from 64-bit PowerShell, no administrator rights needed:

```powershell
cd src\Client\Client.LibWebRTC\Build\windows
powershell -ExecutionPolicy Bypass -File build.ps1
```

Install first. The script checks every item before it downloads anything and says what is missing:

| What | Where |
|---|---|
| Visual Studio 2022 or 2026, workload **Desktop development with C++** | Build Tools for Visual Studio are enough. Chromium compiles with its own clang-cl but takes the headers, libraries and environment of Visual Studio |
| **Windows 11 SDK 10.0.26100** | Visual Studio Installer → Individual components. It must be this exact version: Chromium asks for it by number (`SDK_VERSION` in `src\build\vs_toolchain.py`) |
| **Debugging Tools for Windows** of that SDK | Settings → Apps → *Windows Software Development Kit - Windows 10.0.26100* → Modify → Change → tick *Debugging Tools for Windows*. The Visual Studio Installer does not install them |
| Git for Windows | `git config --global core.longpaths true` (the Chromium tree has paths over 260 characters) and `git config --global core.autocrlf false` (the patches expect LF) |
| about 40 GB on an NTFS drive | |

The checkout goes into `C:\src\flowrate-libwebrtc` by default. Chromium breaks on long paths, so
another folder must stay short too (`FLOWRATE_WEBRTC_DIR`, at most 40 characters). The first run takes
from one to a few hours. Windows Defender scanning every file of the tree is a large part of that: an
exclusion for the folder makes it much faster (Windows Security → Virus & threat protection →
Manage settings → Exclusions, needs administrator rights).

The output is `prebuilt/win-x64/flowrate_webrtc.dll`. The script then runs `windows/verify.ps1` on it.

### Files

| File | Purpose |
|---|---|
| `linux/build.sh` | fetches and syncs the pinned sources, applies the patches, builds with gn and ninja, checks the exports and the needed libraries, strips the library into `prebuilt/linux-x64/` |
| `windows/build.ps1` | the same for Windows, after it has checked the prerequisites above |
| `windows/verify.ps1` | loads `flowrate_webrtc.dll` from a folder with Windows alone on the search path and looks up every function of the header; opens no audio device. CI runs it on the published client |
| `patches/flowrate_target.patch` | our change to the `BUILD.gn` of the wrapper, used by both scripts |
| `patches/flowrate_keep_sps.patch` | our change to WebRTC, applied once like the patches of the wrapper |
| `LibWebRTCRuntime.targets` | copies `prebuilt/<rid>/` next to the executable on every build and publish |

### Moving to a newer WebRTC

Change the pinned commit, tag and patch names together in both scripts. The wrapper makes its patches
for each release, e.g. `custom_audio_source_m<N>.patch`, and its own `build/` scripts show which ones
a platform takes. On Windows the script stops when the new WebRTC wants another SDK, and `$sdkVersion`
and the table above follow it. A sync of the new commit resets the tree, so the patches go on again
by themselves; ours to WebRTC may need to be made again for the new sources.

## Publishing the client

`LibWebRTCRuntime.targets` is imported by `Client.Avalonia.Desktop.csproj` and `Client.TestBot.csproj`,
like `GStreamerRuntime.targets`: only the executable knows the RID. The library is marked
`ExcludeFromSingleFile`, so a single-file JIT build keeps it next to the executable, and so does an
AOT build. `publish.sh` and `publish.ps1` only note a platform whose library is missing.

The client cross-publishes from either system, and since the libraries of both platforms are in the
repository, a Windows build made on Linux carries `flowrate_webrtc.dll` too.

## External audio ABI

After changing the PCM device boundary, rebuild both platform prebuilts before publishing. A matching
library exports `fr_audio_bridge_version` (currently 1) and `fr_pull_playout` in addition to the peer
interface. The managed loader rejects an older binary before it initializes any audio hardware.
The desktop then falls back to the GStreamer transport. `windows/verify.ps1` deliberately rejects
an older DLL, so a Windows publish cannot silently ship the fallback as the new native implementation.

Run the headless `Client.Audio.SmokeChecks` executable after building; see `../README.md`.

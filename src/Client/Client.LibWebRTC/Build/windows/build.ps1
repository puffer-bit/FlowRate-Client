# Builds flowrate_webrtc.dll for win-x64 from the sources of WebRTC: libwebrtc (the fork of webrtc-sdk),
# its C++ wrapper (webrtc-sdk/libwebrtc) and the C interface of flowrate_webrtc.h, linked into one
# library. The result goes into ..\prebuilt\win-x64\, which is committed: neither CI nor publish.ps1
# builds it, they only copy it. The Linux counterpart is ../linux/build.sh.
#
# Usage, from 64-bit PowerShell, no administrator rights needed:
#   powershell -ExecutionPolicy Bypass -File build.ps1
# The checkout lives in $env:FLOWRATE_WEBRTC_DIR (default C:\src\flowrate-libwebrtc: Chromium wants a
# short path) and takes about 30 GB. The first run downloads it and builds everything (one to a few
# hours); the next ones only rebuild what changed. What has to be installed first is in ..\README.md;
# the script checks it all before it downloads anything.
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($env:OS -ne "Windows_NT" -or [IntPtr]::Size -ne 8) {
    throw "Run this script in 64-bit PowerShell on Windows"
}

# The sources, pinned as in ../linux/build.sh. The patches differ: the Linux one fixes the desktop
# capture of Wayland, the Windows one lets the WASAPI devices of 176.4 and 192 kHz open in shared mode
$wrapperTag = "libwebrtc.m150.7871.06"
$webrtcCommit = "a545de45259b47dfac5f3635863e47cb601f1bb2" # webrtc-sdk/webrtc, m150_release
$webrtcPatches = @("custom_audio_source_m150", "add_libwebrtc_build_target", "allow_176k4_192k_shared_mode_formats")
# Our patches to WebRTC, in ..\patches
$flowratePatches = @("flowrate_audio_processing", "flowrate_wasapi_autoconvert", "flowrate_no_builtin_aec")
# The Windows SDK this WebRTC builds with: SDK_VERSION of src\build\vs_toolchain.py, checked after the sync
$sdkVersion = "10.0.26100.0"

$native = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\Native"))
$prebuilt = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\prebuilt\win-x64"))
$work = if ($env:FLOWRATE_WEBRTC_DIR) { $env:FLOWRATE_WEBRTC_DIR } else { "$env:SystemDrive\src\flowrate-libwebrtc" }
$work = [IO.Path]::GetFullPath($work)
$outBuild = Join-Path $work "out\win-x64"

# Release, without the tests and the parts we do not use, as on Linux: the screens are captured and
# encoded by GStreamer, so neither the desktop capture of the wrapper nor the H.265 coder of Media
# Foundation is built (the latter would also tie the library to mfplat.dll, missing on the N editions
# of Windows). The rest follows the releases of webrtc-sdk: clang-cl with the C++ library of MSVC.
# A release build links the C runtime statically, so no Visual C++ redistributable is needed. The tests
# stay declared, as the wrapper has a test target that needs them, but only the library is built.
$gnArgs = @'
target_os = "win"
target_cpu = "x64"
is_debug = false
is_clang = true
is_component_build = false
symbol_level = 0
enable_iterator_debugging = false
treat_warnings_as_errors = false
use_custom_libcxx = false
rtc_include_tests = true
rtc_build_tools = false
rtc_build_examples = false
rtc_enable_protobuf = false
rtc_use_h265 = false
libwebrtc_desktop_capture = false
'@

function Write-Step([string]$text) { Write-Host "######## $text" }

# Runs a native command and fails on its exit code. Windows PowerShell turns any stderr line of a
# native command into a terminating error under "Stop" once the output is redirected, and git and
# gclient report their progress there, hence "Continue" around it.
function Invoke-Native([string]$command, [string[]]$arguments) {
    $ErrorActionPreference = "Continue"
    & $command @arguments 2>&1 | ForEach-Object { Write-Host "$_" }
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($exitCode -ne 0) { throw "$command $($arguments -join ' ') failed with exit code $exitCode" }
}

# Runs a native command for its exit code alone
function Test-Native([string]$command, [string[]]$arguments) {
    $ErrorActionPreference = "Continue"
    & $command @arguments 2>&1 | Out-Null
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    return $exitCode -eq 0
}

# The output of a native command, or nothing when it failed
function Read-Native([string]$command, [string[]]$arguments) {
    $ErrorActionPreference = "Continue"
    $output = & $command @arguments 2>$null
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($exitCode -eq 0) { return $output }
}

# ---- What has to be installed: everything is checked before the hours of downloading and building

$problems = New-Object Collections.Generic.List[string]

if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
    $problems.Add("Git for Windows is not on PATH: https://git-scm.com/download/win")
} else {
    # The paths of the Chromium tree are longer than 260 characters, and its patches expect LF
    if ((& git.exe config --global --get core.longpaths) -ne "true") {
        $problems.Add("Git does not allow long paths: run  git config --global core.longpaths true")
    }
    if ((& git.exe config --get core.autocrlf) -eq "true") {
        $problems.Add("Git converts line endings: run  git config --global core.autocrlf false")
    }
}

# Visual Studio 2022 or 2026 with the C++ build tools. Chromium compiles with its own clang-cl, but
# takes the headers, libraries and linker environment of Visual Studio and the Windows SDK
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath = $null
$vsYear = $null
if (Test-Path $vswhere -PathType Leaf) {
    $vs = & $vswhere -latest -products '*' -version '[17.0,19.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json | ConvertFrom-Json
    if ($vs) {
        $vsPath = @($vs)[0].installationPath
        $vsYear = if (@($vs)[0].installationVersion -like "18.*") { "2026" } else { "2022" }
    }
}
if (-not $vsPath) {
    $problems.Add("No Visual Studio 2022 or 2026 with the C++ build tools: install the workload " +
        "'Desktop development with C++' (Build Tools for Visual Studio are enough)")
}

# The exact Windows SDK version Chromium asks for, with the Debugging Tools, whose dbghelp.dll it copies
$kits = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots" -ErrorAction SilentlyContinue).KitsRoot10
if (-not $kits) { $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10" }
if (-not (Test-Path (Join-Path $kits "Include\$sdkVersion\um\windows.h") -PathType Leaf)) {
    $problems.Add("No Windows SDK $sdkVersion in $kits`: add 'Windows 11 SDK ($sdkVersion)' in the " +
        "Visual Studio Installer (Individual components)")
} elseif (-not (Test-Path (Join-Path $kits "Debuggers\x64\dbghelp.dll") -PathType Leaf)) {
    $problems.Add("The Windows SDK has no Debugging Tools for Windows: Settings > Apps > " +
        "'Windows Software Development Kit - Windows $sdkVersion' > Modify > Change, tick " +
        "'Debugging Tools for Windows'")
}

$drive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($work))
if ($drive.DriveFormat -ne "NTFS" -and $drive.DriveFormat -ne "ReFS") {
    $problems.Add("$work is on $($drive.DriveFormat): the checkout needs NTFS (symbolic links, long paths)")
}
if (-not (Test-Path (Join-Path $work ".synced")) -and $drive.AvailableFreeSpace -lt 40GB) {
    $problems.Add("$([math]::Round($drive.AvailableFreeSpace / 1GB)) GB free on $($drive.Name), the checkout and the build need about 40 GB")
}
if ($work.Length -gt 40) {
    $problems.Add("$work is too long a path for the Chromium tree: set FLOWRATE_WEBRTC_DIR to a short one such as C:\src\flowrate-libwebrtc")
}

if ($problems.Count -gt 0) {
    Write-Host "The build cannot start:" -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "Visual Studio $vsYear`: $vsPath"
Write-Host "Windows SDK $sdkVersion`: $kits"

# Chromium takes the Visual Studio installed here instead of the toolchain package of Google
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
Set-Item "env:vs$($vsYear)_install" $vsPath
$env:GCLIENT_SUPPRESS_GIT_VERSION_WARNING = "1"

New-Item -ItemType Directory -Force -Path $work | Out-Null
Set-Location $work

if (-not (Test-Path "depot_tools")) {
    Write-Step "Downloading depot_tools"
    Invoke-Native git.exe @("clone", "--depth", "1", "https://chromium.googlesource.com/chromium/tools/depot_tools.git")
}
$env:PATH = "$work\depot_tools;$env:PATH"
$gclient = Join-Path $work "depot_tools\gclient.bat"
$gn = Join-Path $work "depot_tools\gn.bat"
$ninja = Join-Path $work "depot_tools\ninja.bat"

function Read-Marker([string]$name) {
    $path = Join-Path $work $name
    if (Test-Path $path -PathType Leaf) { return (Get-Content $path -Raw).Trim() }
    return ""
}

# WebRTC itself: fetched at the pinned commit alone, then its dependencies by gclient, which also
# installs the Python and the tools of depot_tools on its first run. A sync is needed only when the
# commit changed; it resets the tree, so the patches go on again after it
if ((Read-Marker ".synced") -ne $webrtcCommit) {
    Write-Step "Fetching WebRTC $webrtcCommit"
    if (-not (Test-Path "src\.git")) {
        Invoke-Native git.exe @("init", "-q", "src")
        Invoke-Native git.exe @("-C", "src", "remote", "add", "origin", "https://github.com/webrtc-sdk/webrtc.git")
    }
    Invoke-Native git.exe @("-C", "src", "fetch", "--depth", "1", "origin", $webrtcCommit)
    Invoke-Native git.exe @("-C", "src", "checkout", "-q", "-f", "FETCH_HEAD")
    Invoke-Native git.exe @("-C", "src", "clean", "-q", "-fd")

    Set-Content -Path ".gclient" -Encoding Ascii -Value @"
solutions = [
  {
    "name": "src",
    "url": "https://github.com/webrtc-sdk/webrtc.git@$webrtcCommit",
    "deps_file": "DEPS",
    "managed": False,
    "custom_deps": {},
  },
]
target_os = ["win"]
"@

    Write-Step "Syncing the dependencies of WebRTC"
    Invoke-Native $gclient @("sync", "--no-history", "--jobs", "$([Environment]::ProcessorCount)", "-D")
    Set-Content -Path ".synced" -Encoding Ascii -Value $webrtcCommit
    Remove-Item ".wrapper" -ErrorAction SilentlyContinue
}

# The date the linker stamps: the hook of gclient takes it from the last commit of src\build with a
# Change-Id, finds none in the shallow history and writes 0, a date before 1970 lld-link refuses.
# Any commit does, and the hook writes 0 again on the next sync
if ((Read-Marker "src\build\util\LASTCHANGE.committime") -in @("", "0")) {
    Write-Step "Dating the build"
    Invoke-Native (Join-Path $work "depot_tools\python3.bat") @("src\build\util\lastchange.py", "--filter", ".", "-o", "src\build\util\LASTCHANGE")
}

$toolchain = Get-Content "src\build\vs_toolchain.py" -Raw
if ($toolchain -notmatch "SDK_VERSION = '$([regex]::Escape($sdkVersion))'") {
    throw "This WebRTC wants another Windows SDK than $sdkVersion (see SDK_VERSION in src\build\vs_toolchain.py): update `$sdkVersion here"
}

# The wrapper, copied into the tree as src\libwebrtc like its own build does, with our patch that
# links our sources into it. Copied again when the tag or the patch changed
$flowratePatch = Join-Path $PSScriptRoot "..\patches\flowrate_target.patch"
$wrapper = "$wrapperTag $((Get-FileHash $flowratePatch -Algorithm SHA256).Hash.ToLowerInvariant())"
if ((Read-Marker ".wrapper") -ne $wrapper) {
    Write-Step "Fetching the wrapper $wrapperTag"
    foreach ($path in @("wrapper", "src\libwebrtc")) {
        if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    }
    Invoke-Native git.exe @("clone", "-q", "--depth", "1", "--branch", $wrapperTag, "https://github.com/webrtc-sdk/libwebrtc.git", "wrapper")
    New-Item -ItemType Directory -Path "src\libwebrtc" | Out-Null
    foreach ($item in @("include", "src", "patches", "BUILD.gn", "LICENSE")) {
        Copy-Item (Join-Path "wrapper" $item) "src\libwebrtc\" -Recurse
    }
    Invoke-Native git.exe @("-C", "src", "apply", "--ignore-whitespace", $flowratePatch)
    Set-Content -Path ".wrapper" -Encoding Ascii -Value $wrapper
}

# The patches of the wrapper to WebRTC, each applied once: applying them again would touch the files
# and rebuild half of WebRTC
foreach ($patch in $webrtcPatches) {
    $file = Join-Path $work "src\libwebrtc\patches\$patch.patch"
    if (-not (Test-Native git.exe @("-C", "src", "apply", "--reverse", "--check", "--ignore-whitespace", $file))) {
        Write-Step "Applying $patch"
        Invoke-Native git.exe @("-C", "src", "apply", "--ignore-whitespace", "--whitespace=nowarn", $file)
    }
}

foreach ($patch in $flowratePatches) {
    $file = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\patches\$patch.patch"))
    if (-not (Test-Native git.exe @("-C", "src", "apply", "--reverse", "--check", "--ignore-whitespace", $file))) {
        Write-Step "Applying $patch"
        Invoke-Native git.exe @("-C", "src", "apply", "--ignore-whitespace", "--whitespace=nowarn", $file)
    }
}

# Our sources, copied only when they changed so that ninja rebuilds nothing else
New-Item -ItemType Directory -Force -Path "src\libwebrtc\flowrate" | Out-Null
foreach ($source in Get-ChildItem (Join-Path $native "flowrate_*") -Include "*.cc", "*.h") {
    $target = Join-Path "src\libwebrtc\flowrate" $source.Name
    if (-not (Test-Path $target) -or (Get-FileHash $source.FullName).Hash -ne (Get-FileHash $target).Hash) {
        Copy-Item $source.FullName $target -Force
    }
}

Write-Step "Building"
# The arguments go through args.gn: passing quotes through gn.bat and cmd.exe on a command line is fragile
New-Item -ItemType Directory -Force -Path $outBuild | Out-Null
Set-Content -Path (Join-Path $outBuild "args.gn") -Encoding Ascii -Value $gnArgs
Invoke-Native $gn @("gen", $outBuild, "--root=src")
Invoke-Native $ninja @("-C", $outBuild, "libwebrtc")

$library = Join-Path $outBuild "flowrate_webrtc.dll"

# Only Windows itself may be needed: no Visual C++ runtime, no Media Foundation
$dumpbin = Get-ChildItem (Join-Path $vsPath "VC\Tools\MSVC\*\bin\Hostx64\x64\dumpbin.exe") -ErrorAction SilentlyContinue |
    Sort-Object FullName | Select-Object -Last 1
if ($dumpbin) {
    $needs = & $dumpbin.FullName /nologo /dependents $library | Where-Object { $_ -match '^\s+(\S+\.dll)\s*$' } | ForEach-Object { $_.Trim() }
    Write-Host "Needs: $($needs -join ' ')"
    $runtime = $needs | Where-Object { $_ -match '^(vcruntime|msvcp|concrt|ucrtbase|api-ms-win-crt-|mfplat)' }
    if ($runtime) { throw "flowrate_webrtc.dll needs $($runtime -join ', '), which Windows does not always have" }
}

Write-Step "Copying into $prebuilt"
New-Item -ItemType Directory -Force -Path $prebuilt | Out-Null
Copy-Item $library $prebuilt -Force
$licenses = @(
    "webrtc-sdk/libwebrtc (the C++ wrapper):", "", (Get-Content "src\libwebrtc\LICENSE" -Raw),
    "WebRTC:", "", (Get-Content "src\LICENSE" -Raw))
Set-Content -Path (Join-Path $prebuilt "LICENSE.libwebrtc") -Encoding Ascii -Value $licenses

# Every function of the header must be exported, and the library must load with Windows alone
& (Join-Path $PSScriptRoot "verify.ps1") -RuntimeDirectory $prebuilt

$size = (Get-Item (Join-Path $prebuilt "flowrate_webrtc.dll")).Length
if ($size -ge 100MB) { throw "flowrate_webrtc.dll takes $([math]::Round($size / 1MB)) MB, GitHub takes files up to 100 MB" }

# A copy of the sources without git has no commit to tell
$flowrateCommit = Read-Native git.exe @("-C", $PSScriptRoot, "rev-parse", "--short", "HEAD")
if (-not $flowrateCommit) { $flowrateCommit = "unknown" }
$dirty = Read-Native git.exe @("-C", $PSScriptRoot, "status", "--porcelain", "--", "..\..\Native", "..\patches", ".")
if ($dirty) { $flowrateCommit += " (with uncommitted changes)" }
$date = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$hash = (Get-FileHash (Join-Path $prebuilt "flowrate_webrtc.dll") -Algorithm SHA256).Hash.ToLowerInvariant()
$argsLine = ($gnArgs -split "`r?`n" | ForEach-Object { $_ -replace '\s', '' }) -join ' '
Set-Content -Path (Join-Path $prebuilt "BUILD-INFO.txt") -Encoding Ascii -Value @"
flowrate_webrtc.dll, built by Build/windows/build.ps1
Date:      $date
WebRTC:    webrtc-sdk/webrtc $webrtcCommit
Wrapper:   webrtc-sdk/libwebrtc $wrapperTag
Patches:   $($webrtcPatches -join ' ') $($flowratePatches -join ' ') flowrate_target
FlowRate:  $flowrateCommit
SHA-256:   $hash
Toolchain: Visual Studio $vsYear, Windows SDK $sdkVersion
GN args:   $argsLine
"@

Write-Step "Done"
Get-ChildItem $prebuilt

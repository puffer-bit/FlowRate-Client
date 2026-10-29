<#
.SYNOPSIS
Builds the GStreamer runtime shipped with the client for win-x64 from the official MSVC installer.

.DESCRIPTION
The official installer (Inno Setup) cannot be unpacked without running it, so the script installs it
for the current user only into a temporary folder, copies the plugins the client needs together with
the DLLs they import, removes the installation again and checks the result with verify.ps1. No administrator rights are
needed and nothing stays installed.

Output in ..\artifacts:
  win-x64\gstreamer\{bin, lib\gstreamer-1.0, libexec\gstreamer-1.0}   the folder shipped with the client
  win-x64\licenses, win-x64\THIRD-PARTY-NOTICES.txt
  win-x64\tools\gst-inspect-1.0.exe                                    for the check only, not shipped
  gstreamer-<version>-win-x64.zip

.EXAMPLE
powershell -ExecutionPolicy Bypass -File collect.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = "1.28.7"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptDir = $PSScriptRoot
$artifacts = Join-Path (Split-Path $scriptDir -Parent) "artifacts"
$out = Join-Path $artifacts "win-x64"
$dest = Join-Path $out "gstreamer"
$work = Join-Path $env:TEMP "flowrate-gstreamer-$Version"
$installDir = Join-Path $work "install"
$installer = Join-Path $work "gstreamer-1.0-msvc-x86_64-$Version.exe"
$installerUrl = "https://gstreamer.freedesktop.org/data/pkg/windows/$Version/msvc/gstreamer-1.0-msvc-x86_64-$Version.exe"

# Plugins the client builds its pipelines from (file gst<name>.dll). Hardware plugins register their
# elements only when the device is present. No GPL plugin is on the list (x264, x265, libav, ...).
$plugins = @(
    "coreelements", "app", "audioconvert", "audioresample", "audiomixer", "audiofx", "audiotestsrc", "volume", "level", "rawparse",
    "audiobuffersplit", "webrtcdsp", "autodetect", "videoconvertscale", "videorate",
    "opus", "rtp", "rtpmanager", "udp", "nice", "webrtc", "dtls", "srtp", "sctp", "sdpelem",
    "videoparsersbad", "openh264", "vpx", "svtav1", "dav1d", "rsrtp", "rsaudiofx",
    "wasapi2", "d3d11", "nvcodec", "qsv", "amfcodec", "mediafoundation"
)

# License folders (share\licenses\<name>) of the components in the bundle. The installer carries the
# licenses of everything it contains, x264 and ffmpeg included; only these are shipped.
$licensePackages = @(
    "gstreamer-1.0", "gst-plugins-base-1.0", "gst-plugins-bad-1.0", "gst-plugins-rs",
    "glib", "libffi", "proxy-libintl", "pcre2", "zlib", "orc",
    "opus", "webrtc-audio-processing", "abseil-cpp", "openh264", "libvpx", "dav1d", "svt-av1", "libnice", "libsrtp", "openssl"
)

# Never allowed in the bundle
$forbidden = '^(lib)?(x264|x265|avcodec|avformat|avfilter|avutil|swscale|swresample|postproc|a52|mpeg2|dvdread|faad|mad)'

# --- PE import table ---------------------------------------------------------------------------

function Get-PeImports([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    $sectionCount = [BitConverter]::ToUInt16($bytes, $peOffset + 6)
    $optionalHeaderSize = [BitConverter]::ToUInt16($bytes, $peOffset + 20)
    $optionalHeader = $peOffset + 24
    $isPe32Plus = [BitConverter]::ToUInt16($bytes, $optionalHeader) -eq 0x20B
    $dataDirectories = $optionalHeader + $(if ($isPe32Plus) { 112 } else { 96 })
    $sections = $optionalHeader + $optionalHeaderSize

    $toOffset = {
        param([uint32]$rva)
        for ($i = 0; $i -lt $sectionCount; $i++) {
            $section = $sections + 40 * $i
            $virtualAddress = [BitConverter]::ToUInt32($bytes, $section + 12)
            $size = [Math]::Max([BitConverter]::ToUInt32($bytes, $section + 8), [BitConverter]::ToUInt32($bytes, $section + 16))
            if ($rva -ge $virtualAddress -and $rva -lt $virtualAddress + $size) {
                return $rva - $virtualAddress + [BitConverter]::ToUInt32($bytes, $section + 20)
            }
        }
        return $null
    }

    $readName = {
        param([uint32]$rva)
        $offset = & $toOffset $rva
        $end = $offset
        while ($bytes[$end] -ne 0) { $end++ }
        return [System.Text.Encoding]::ASCII.GetString($bytes, $offset, $end - $offset)
    }

    $names = New-Object System.Collections.Generic.List[string]

    # Directory 1: import table, 20-byte descriptors; directory 13: delay imports, 32-byte descriptors
    foreach ($entry in @(@{ Index = 1; Size = 20; NameField = 12 }, @{ Index = 13; Size = 32; NameField = 4 })) {
        $tableRva = [BitConverter]::ToUInt32($bytes, $dataDirectories + 8 * $entry.Index)
        if ($tableRva -eq 0) { continue }

        $descriptor = & $toOffset $tableRva
        while ($null -ne $descriptor) {
            $nameRva = [BitConverter]::ToUInt32($bytes, $descriptor + $entry.NameField)
            if ($nameRva -eq 0) { break }
            $names.Add((& $readName $nameRva))
            $descriptor += $entry.Size
        }
    }

    return $names
}

# --- Install into a temporary folder -----------------------------------------------------------

New-Item -ItemType Directory -Force -Path $work | Out-Null

if (-not (Test-Path $installer)) {
    Write-Host "== Downloading $installerUrl"
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest -Uri $installerUrl -OutFile $installer
}

if (Test-Path $installDir) { Remove-Item -Recurse -Force $installDir }

Write-Host "== Installing for the current user into $installDir (removed again at the end)"
$arguments = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/NOICONS", "/CURRENTUSER",
    "/TYPE=complete", "/DIR=`"$installDir`"", "/LOG=`"$(Join-Path $work 'install.log')`"")
$process = Start-Process -FilePath $installer -ArgumentList $arguments -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "The installer failed with exit code $($process.ExitCode), see $work\install.log" }

try {
    # The installer may nest the runtime (1.0\msvc_x86_64) under the chosen folder
    $core = Get-ChildItem -Path $installDir -Recurse -Filter "gstreamer-1.0-0.dll" | Select-Object -First 1
    if ($null -eq $core) { throw "gstreamer-1.0-0.dll not found under $installDir" }
    $root = Split-Path (Split-Path $core.FullName -Parent) -Parent
    $bin = Join-Path $root "bin"
    Write-Host "== Runtime found in $root"

    # --- Collect ----------------------------------------------------------------------------------

    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    $destBin = Join-Path $dest "bin"
    $destPlugins = Join-Path $dest "lib\gstreamer-1.0"
    $destLibexec = Join-Path $dest "libexec\gstreamer-1.0"
    $tools = Join-Path $out "tools"
    New-Item -ItemType Directory -Force -Path $destBin, $destPlugins, $destLibexec, $tools | Out-Null

    Write-Host "== Copying plugins"
    $missingPlugins = @()
    foreach ($plugin in $plugins) {
        $file = Join-Path $root "lib\gstreamer-1.0\gst$plugin.dll"
        if (Test-Path $file) { Copy-Item $file $destPlugins } else { $missingPlugins += $plugin }
    }
    # Without it the voice goes out unprocessed, far quieter than in other applications
    if ($missingPlugins -contains "webrtcdsp") { throw "webrtcdsp is not in the installed runtime, see $work\install.log" }
    if ($missingPlugins.Count -gt 0) {
        Write-Warning "Plugins not in the installed runtime: $($missingPlugins -join ', ')"
    }

    Copy-Item (Join-Path $root "libexec\gstreamer-1.0\gst-plugin-scanner.exe") $destLibexec
    Copy-Item (Join-Path $bin "gst-inspect-1.0.exe") $tools
    # The binding imports these by name, even when no plugin needs them
    foreach ($library in @("gstreamer-1.0-0.dll", "gstbase-1.0-0.dll", "gstapp-1.0-0.dll", "gstvideo-1.0-0.dll",
            "gstaudio-1.0-0.dll", "gstwebrtc-1.0-0.dll", "gstsdp-1.0-0.dll", "gstrtp-1.0-0.dll",
            "gstpbutils-1.0-0.dll", "gstcontroller-1.0-0.dll", "gstnet-1.0-0.dll", "gsttag-1.0-0.dll",
            "glib-2.0-0.dll", "gobject-2.0-0.dll", "gmodule-2.0-0.dll", "gio-2.0-0.dll", "nice-10.dll")) {
        Copy-Item (Join-Path $bin $library) $destBin
    }

    Write-Host "== Resolving dependencies"
    # Anything the runtime does not have in its bin folder is a Windows system DLL
    do {
        $added = 0
        $files = Get-ChildItem -Path $dest, $tools -Recurse -Include *.dll, *.exe
        foreach ($file in $files) {
            foreach ($import in (Get-PeImports $file.FullName)) {
                $target = Join-Path $destBin $import
                $source = Join-Path $bin $import
                if ((Test-Path $target) -or -not (Test-Path $source)) { continue }
                Copy-Item $source $target
                Write-Host "  + $import"
                $added++
            }
        }
    } while ($added -gt 0)

    $bad = Get-ChildItem -Path $dest -Recurse -File | Where-Object { $_.Name -match $forbidden }
    if ($bad) { throw "GPL or libav libraries in the bundle: $($bad.Name -join ', ')" }

    Write-Host "== Collecting licenses"
    $licenses = Join-Path $out "licenses"
    New-Item -ItemType Directory -Force -Path $licenses | Out-Null
    $shareLicenses = Join-Path $root "share\licenses"
    $missingLicenses = @()
    foreach ($package in $licensePackages) {
        $folder = Join-Path $shareLicenses $package
        if (Test-Path $folder) { Copy-Item -Recurse $folder $licenses } else { $missingLicenses += $package }
    }
    # Abseil is linked statically into webrtcdsp, and the installer carries no license folder for it
    if ($missingLicenses -contains "abseil-cpp") {
        $abseil = Join-Path $licenses "abseil-cpp"
        New-Item -ItemType Directory -Force -Path $abseil | Out-Null
        $ProgressPreference = "SilentlyContinue"
        Invoke-WebRequest -Uri "https://raw.githubusercontent.com/abseil/abseil-cpp/master/LICENSE" -OutFile (Join-Path $abseil "LICENSE")
        $missingLicenses = @($missingLicenses | Where-Object { $_ -ne "abseil-cpp" })
    }
    if ($missingLicenses.Count -gt 0) {
        Write-Warning "License folders not in the installed runtime: $($missingLicenses -join ', ')"
    }

    $notices = @(
        "Third-party components of the FlowRate client media runtime",
        "===========================================================",
        "",
        "GStreamer $Version and its plugins (LGPL-2.1-or-later), taken unmodified from the official",
        "MSVC x86_64 build: $installerUrl",
        "Sources: https://gitlab.freedesktop.org/gstreamer/gstreamer, build recipes:",
        "https://gitlab.freedesktop.org/gstreamer/cerbero (tag $Version)",
        "",
        "The libraries are unmodified and loaded dynamically; any of them can be replaced by the user.",
        "License texts are in the 'licenses' folder.",
        "",
        "Bundled files:"
    )
    $notices += Get-ChildItem -Path $dest -Recurse -File | Sort-Object FullName |
        ForEach-Object { "  " + $_.FullName.Substring($dest.Length + 1) }
    Set-Content -Path (Join-Path $out "THIRD-PARTY-NOTICES.txt") -Value $notices -Encoding UTF8
}
finally {
    Write-Host "== Removing the temporary installation"
    $uninstaller = Get-ChildItem -Path $installDir -Recurse -Filter "unins*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $uninstaller) {
        Start-Process -FilePath $uninstaller.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait | Out-Null
    }
    Remove-Item -Recurse -Force $installDir -ErrorAction SilentlyContinue
}

# --- Verify in isolation -------------------------------------------------------------------------

Write-Host "== Verifying"
& (Join-Path $scriptDir "verify.ps1") -Bundle $out

$archive = Join-Path $artifacts "gstreamer-$Version-win-x64.zip"
if (Test-Path $archive) { Remove-Item $archive }
Compress-Archive -Path $dest, (Join-Path $out "licenses"), (Join-Path $out "THIRD-PARTY-NOTICES.txt") -DestinationPath $archive
Write-Host "== Done: $out, $archive"

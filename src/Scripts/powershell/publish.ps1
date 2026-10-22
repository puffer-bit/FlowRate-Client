<#
.SYNOPSIS
Builds the self-contained FlowRate client on Windows, for Windows, Linux and/or Android.

.DESCRIPTION
Builds the GStreamer runtime, then the client with its own .NET runtime, then everything to publish in
dist\<version>\<platform>\ (platform win-x64, win-x64-aot, linux-x64 or android). The result needs
neither .NET nor GStreamer on the target machine. Needs the .NET 10 SDK; no administrator rights. See
src\Client\Client.GStreamer\Build\README.md for how the runtime is built.

A desktop platform folder holds two forms of the client:
 - the installed one, made by vpk (Velopack, a local dotnet tool of the repository), which updates
   itself: FlowRateClient-win-Setup.exe or FlowRateClient-linux.AppImage, plus the packages and the
   releases.<channel>.json its updater takes;
 - the standalone one, FlowRateClient-<version>-<platform>-standalone.zip or .tar.gz: unpacked and run
   as is, it does not update itself.
Every file of the folder goes to the GitHub release v<version> as it is; a dev build is published as a
pre-release, which only clients set to the dev builds take.

Every variant has an update channel of its own (win, win-aot, linux, linux-aot), so a client updates to
the same variant it is. The last build of a channel is kept in dist\.feed\<channel>[-dev] and vpk makes
a delta package from it; with none there, the latest release of the channel is downloaded from GitHub
(GITHUB_TOKEN, if set, lets it read a private repository).

The win-x64 runtime is built here when missing (Build\windows\collect.ps1). The linux-x64 runtime is
built with Docker on Linux or in WSL (Build/linux/build.sh): copy its artifacts\linux-x64 folder here
first. The Linux release is packed here by vpk for Linux; publish.sh on Linux is the tried way.

The Android client is one .apk (arm64-v8a and x86_64) with no GStreamer runtime. It needs the android
workload of dotnet (dotnet workload install android), the Android SDK and a JDK. With no keystore given
the .apk is signed with the debug key: it installs, but a store does not take it. A release key goes in
the variables ANDROID_KEYSTORE (the file), ANDROID_KEY_ALIAS, ANDROID_KEYSTORE_PASSWORD and
ANDROID_KEY_PASSWORD.

.PARAMETER Target
win-x64 (the default), linux-x64, android or all. Also accepted as the first argument without the name.

.PARAMETER Aot
Compile ahead of time (NativeAOT) instead of the JIT build. NativeAOT does not compile for another
system, so this builds win-x64 only; it needs the C++ build tools of Visual Studio. The Android build
has no NativeAOT variant.

.PARAMETER GStreamer
Rebuild the win-x64 GStreamer runtime even when one already exists.

.PARAMETER Notes
A Markdown file with what is new in this version, shown by the updater of the client.

.PARAMETER Dev
A development build: the version gets "-dev.<UTC yyMMddHHmm>"; publish it as a GitHub pre-release. Its version is below the release of the same number, so
raise <Version> in the project after a release, or the dev builds of the next one are not newer.

.PARAMETER Stamp
The stamp of a -Dev build, yyMMddHHmm, instead of the current moment: the builds of one release on
Windows and Linux share it.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File publish.ps1
powershell -ExecutionPolicy Bypass -File publish.ps1 -Target all
powershell -ExecutionPolicy Bypass -File publish.ps1 win-x64
powershell -ExecutionPolicy Bypass -File publish.ps1 android
powershell -ExecutionPolicy Bypass -File publish.ps1 -Aot
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("win-x64", "linux-x64", "android", "all")]
    [string]$Target = "win-x64",
    [switch]$Aot,
    [switch]$GStreamer,
    [string]$Notes,
    [switch]$Dev,
    [ValidatePattern('^\d{10}$')]
    [string]$Stamp
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Set-Location (Resolve-Path (Join-Path $PSScriptRoot "..\..\.."))

$project = "src\Client\Client.Avalonia.Desktop\Client.Avalonia.Desktop.csproj"
$gstreamerDir = "src\Client\Client.GStreamer\Build"
$publishRoot = "src\Client\Client.Avalonia.Desktop\bin\publish"
$androidProject = "src\Client\Client.Avalonia.Android\Client.Avalonia.Android.csproj"
$androidPublishDir = "src\Client\Client.Avalonia.Android\bin\publish\android"
$icon = "src\Client\Client.Avalonia.Desktop\Assets\avalonia-logo.ico"
$feedDir = "dist\.feed"
$repoUrl = "https://github.com/puffer-bit/FlowRate-Client"
# The installed application goes to %LocalAppData%\FlowRateClient on Windows; the data of the user
# are in the FlowRate folder apart from it (AppDirectories), so an update or uninstall leaves them
$packId = "FlowRateClient"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "The .NET SDK (dotnet) is not installed" }
if ($Notes -and -not (Test-Path $Notes -PathType Leaf)) { throw "No release notes in $Notes" }
if ($Stamp -and -not $Dev) { throw "-Stamp is for a -Dev build" }
if (-not (Get-Command tar.exe -ErrorAction SilentlyContinue)) { throw "tar.exe (part of Windows 10 1803+) is not available" }

$version = ([xml](Get-Content $project)).Project.PropertyGroup |
    ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
# The moment in UTC, as in the build number: a later dev build has the greater version
if ($Dev) {
    if (-not $Stamp) { $Stamp = [DateTime]::UtcNow.ToString('yyMMddHHmm') }
    $version = "$version-dev.$Stamp"
}
$versionDir = Join-Path "dist" $version
$targets = if ($Target -eq "all") { @("win-x64", "linux-x64", "android") } else { @($Target) }

# The publish profile is <rid>-jit or <rid>-aot; the channel of the release ends in -aot for the latter
$mode = if ($Aot) { "aot" } else { "jit" }
$suffix = if ($Aot) { "-aot" } else { "" }
if ($Aot -and $targets -contains "linux-x64") {
    throw "A NativeAOT build for linux-x64 can only be made on Linux (publish.sh --aot)"
}
if ($Aot -and $targets -contains "android") {
    throw "-Aot is for the desktop client, the Android build has no NativeAOT variant"
}
if ($targets | Where-Object { $_ -ne "android" }) {
    # vpk, of the version in dotnet-tools.json: the same as the Velopack library of the client
    dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE" }
}

# Makes sure artifacts\<rid> holds a GStreamer runtime, building the Windows one when needed
function Initialize-GStreamer([string]$rid) {
    $runtime = Join-Path $gstreamerDir "artifacts\$rid\gstreamer"

    if ($rid -eq "linux-x64") {
        if (-not (Test-Path $runtime)) {
            throw ("No Linux GStreamer runtime in $runtime. It is built with Docker: run " +
                "$gstreamerDir/linux/build.sh on Linux or in WSL and copy its artifacts/linux-x64 folder here.")
        }
        if ($GStreamer) { Write-Host "Note: -GStreamer only rebuilds the win-x64 runtime, linux-x64 is used as is" }
        Write-Host "######## Using the Linux GStreamer runtime ($runtime)"
        return
    }

    if ($GStreamer -or -not (Test-Path $runtime)) {
        Write-Host "######## Building the Windows GStreamer runtime"
        & (Join-Path $gstreamerDir "windows\collect.ps1")
    } else {
        Write-Host "######## Using the existing Windows GStreamer runtime ($runtime), -GStreamer rebuilds it"
    }
}

# Runs vpk and fails on its exit code. Windows PowerShell turns any stderr line of a native command
# into a terminating error under "Stop" once the output is redirected, hence "Continue" around it.
function Invoke-Vpk([string[]]$arguments, [switch]$AllowFailure, [switch]$Quiet) {
    $ErrorActionPreference = "Continue"
    # To the host: anything written to the pipeline here would become part of the return value
    & dotnet vpk @arguments 2>&1 | ForEach-Object { if (-not $Quiet) { Write-Host "$_" } }
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($exitCode -ne 0 -and -not $AllowFailure) { throw "vpk $($arguments[0]) failed with exit code $exitCode" }
    return $exitCode -eq 0
}

# The AppImage takes a PNG: the 256 px image of the icon is one already
function Export-IconPng([string]$destination) {
    $data = [System.IO.File]::ReadAllBytes((Resolve-Path $icon).Path)
    $count = [BitConverter]::ToUInt16($data, 4)
    $best = $null
    for ($i = 0; $i -lt $count; $i++) {
        $entry = 6 + 16 * $i
        $width = if ($data[$entry] -eq 0) { 256 } else { $data[$entry] }
        if (-not $best -or $width -gt $best.Width) {
            $best = @{ Width = $width; Size = [BitConverter]::ToUInt32($data, $entry + 8); Offset = [BitConverter]::ToUInt32($data, $entry + 12) }
        }
    }
    if ($data[$best.Offset] -ne 0x89 -or $data[$best.Offset + 1] -ne 0x50) { throw "The largest image of $icon is not a PNG" }
    $png = New-Object byte[] $best.Size
    [Array]::Copy($data, $best.Offset, $png, 0, $best.Size)
    [System.IO.File]::WriteAllBytes($destination, $png)
}

# mtree escapes every character outside a safe set as a backslash and three octal digits
function ConvertTo-MtreeName([string]$value) {
    $builder = New-Object System.Text.StringBuilder
    foreach ($byte in [System.Text.Encoding]::UTF8.GetBytes($value)) {
        $char = [char]$byte
        if ($byte -lt 128 -and ($char -match '[A-Za-z0-9._/:+,@~-]')) { [void]$builder.Append($char) }
        else { [void]$builder.Append('\' + [Convert]::ToString($byte, 8).PadLeft(3, '0')) }
    }
    return $builder.ToString()
}

# Linux binaries (the executable, createdump, gst-plugin-scanner, shared objects) start with the ELF
# magic; the file extension says nothing, "Client.Avalonia.Desktop" has ".Desktop" as one
function Test-ElfFile([string]$path) {
    $stream = [System.IO.File]::OpenRead($path)
    try {
        $magic = New-Object byte[] 4
        return $stream.Read($magic, 0, 4) -eq 4 -and $magic[0] -eq 0x7F -and $magic[1] -eq 0x45 -and
            $magic[2] -eq 0x4C -and $magic[3] -eq 0x46
    }
    finally { $stream.Dispose() }
}

# The standalone form: the published folder in an archive with a single FlowRateClient-<version> folder.
# The file list goes to tar.exe as an mtree spec, which also carries the Unix modes: Linux binaries get 0755.
function New-StandaloneArchive([string]$rid, [string]$publishDir, [string]$archive) {
    $name = "FlowRateClient-$version"
    $source = (Resolve-Path $publishDir).Path
    $archive = Join-Path (Resolve-Path (Split-Path $archive)).Path (Split-Path $archive -Leaf)
    if (Test-Path $archive) { Remove-Item $archive }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("#mtree")
    $lines.Add("$(ConvertTo-MtreeName $name) type=dir mode=0755")
    foreach ($item in Get-ChildItem -Path $source -Recurse | Sort-Object FullName) {
        $relative = "$name/" + $item.FullName.Substring($source.Length + 1).Replace('\', '/')
        if ($item.PSIsContainer) {
            $lines.Add("$(ConvertTo-MtreeName $relative) type=dir mode=0755")
            continue
        }
        $fileMode = if (Test-ElfFile $item.FullName) { "0755" } else { "0644" }
        $contents = ConvertTo-MtreeName $item.FullName.Replace('\', '/')
        $lines.Add("$(ConvertTo-MtreeName $relative) type=file mode=$fileMode contents=$contents")
    }

    $spec = Join-Path $env:TEMP "flowrate-package.mtree"
    # LF only: bsdtar cannot parse an mtree spec with CRLF line endings
    [System.IO.File]::WriteAllText($spec, ($lines -join "`n") + "`n")
    # At least two elements each: PowerShell unrolls a one-element array into a plain string
    $tarArguments = if ($rid -eq "linux-x64") { @("-czf", $archive) } else { @("--format", "zip", "-cf", $archive) }

    $ErrorActionPreference = "Continue"
    # To the host: anything written to the pipeline here would become part of the return value
    & tar.exe @tarArguments "@$spec" 2>&1 | ForEach-Object { Write-Host "$_" }
    $tarExitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($tarExitCode -ne 0) { throw "tar.exe failed with exit code $tarExitCode, file list: $spec" }
    Remove-Item $spec -ErrorAction SilentlyContinue
}

# SemVer precedence of two versions: a pre-release is below its release, numeric identifiers compare as numbers
function Compare-SemVer([string]$a, [string]$b) {
    $aMain, $aPre = $a.Split("-", 2)
    $bMain, $bPre = $b.Split("-", 2)
    $aNumbers = @($aMain.Split(".") | ForEach-Object { [long]$_ })
    $bNumbers = @($bMain.Split(".") | ForEach-Object { [long]$_ })
    for ($i = 0; $i -lt [Math]::Max($aNumbers.Count, $bNumbers.Count); $i++) {
        $x = if ($i -lt $aNumbers.Count) { $aNumbers[$i] } else { 0 }
        $y = if ($i -lt $bNumbers.Count) { $bNumbers[$i] } else { 0 }
        if ($x -ne $y) { return [Math]::Sign($x - $y) }
    }
    if (-not $aPre -and -not $bPre) { return 0 }
    if (-not $aPre) { return 1 }
    if (-not $bPre) { return -1 }
    $aParts = $aPre.Split(".")
    $bParts = $bPre.Split(".")
    for ($i = 0; $i -lt [Math]::Min($aParts.Count, $bParts.Count); $i++) {
        $aNumeric = $aParts[$i] -match '^\d+$'
        $bNumeric = $bParts[$i] -match '^\d+$'
        if ($aNumeric -and $bNumeric) { $order = [Math]::Sign([long]$aParts[$i] - [long]$bParts[$i]) }
        elseif ($aNumeric) { $order = -1 }
        elseif ($bNumeric) { $order = 1 }
        else { $order = [Math]::Sign([string]::CompareOrdinal($aParts[$i], $bParts[$i])) }
        if ($order -ne 0) { return $order }
    }
    return [Math]::Sign($aParts.Count - $bParts.Count)
}

# The assets of the update feed of a channel in a folder: releases.<channel>.json and the packages it lists
function Get-FeedAssets([string]$folder, [string]$channel) {
    $path = Join-Path $folder "releases.$channel.json"
    if (-not (Test-Path $path)) { return @() }
    return @((Get-Content $path -Raw | ConvertFrom-Json).Assets)
}

# Copies the packages of $version to $target, with a feed of that version alone: the release on GitHub
# lists only its own files
function Copy-FeedVersion([string]$folder, [string]$channel, [string]$target) {
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $own = @(Get-FeedAssets $folder $channel | Where-Object { $_.Version -eq $version })
    foreach ($asset in $own) { Copy-Item (Join-Path $folder $asset.FileName) $target }
    $json = @{ Assets = $own } | ConvertTo-Json -Depth 5 -Compress
    [System.IO.File]::WriteAllText((Join-Path (Resolve-Path $target).Path "releases.$channel.json"), $json)
}

# Both forms of a published folder in dist\<version>\<platform>\
function New-Release([string]$rid, [string]$publishDir) {
    $os = $rid.Split("-")[0]
    $channel = "$os$suffix"
    $platform = "$rid$suffix"
    $output = Join-Path $versionDir $platform
    # Dev builds keep a feed of their own: a release makes its delta from the previous release
    $last = Join-Path $feedDir $(if ($Dev) { "$channel-dev" } else { $channel })
    if (Test-Path $output) { Remove-Item -Recurse -Force $output }
    New-Item -ItemType Directory -Force -Path $output, $last | Out-Null
    # Packed here for Linux: vpk builds the AppImage on Windows as well
    $directive = if ($os -eq "linux") { @("[linux]") } else { @() }
    $exe = if ($os -eq "linux") { "Client.Avalonia.Desktop" } else { "Client.Avalonia.Desktop.exe" }
    $packIcon = $icon
    if ($os -eq "linux") {
        $packIcon = Join-Path $publishRoot "icon.png"
        Export-IconPng $packIcon
    }
    $token = if ($env:GITHUB_TOKEN) { @("--token", $env:GITHUB_TOKEN) } else { @() }
    $releaseNotes = if ($Notes) { @("--releaseNotes", $Notes) } else { @() }
    $pre = if ($Dev) { @("--pre") } else { @() }

    Write-Host "######## Packing $platform in the update channel $channel"
    # vpk works in a folder of its own with the previous build alone, for the delta
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("flowrate-vpk-" + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $work | Out-Null
    # Removed also when a step fails
    try {
        Copy-Item (Join-Path $last "*") $work -ErrorAction SilentlyContinue
        if (-not (Test-Path (Join-Path $work "releases.$channel.json"))) {
            Invoke-Vpk ($directive + @("download", "github", "--repoUrl", $repoUrl, "--channel", $channel, "-o", $work) + $token + $pre) -AllowFailure -Quiet | Out-Null
        }
        # A rebuild of the same version, or of an older one, has nothing to make a delta from
        if (Get-FeedAssets $work $channel | Where-Object { (Compare-SemVer $_.Version $version) -ge 0 }) {
            Get-ChildItem $work | Remove-Item -Force
        }
        if (-not (Test-Path (Join-Path $work "releases.$channel.json"))) {
            Write-Host "Note: no earlier build of $channel, the release has no delta package"
        }

        # No portable bundle of vpk on Windows: the standalone archive is the form that is not installed;
        # on Linux the AppImage is the only bundle
        $portable = if ($os -eq "win") { @("--noPortable") } else { @() }
        Invoke-Vpk ($directive + @("pack", "--packId", $packId, "--packVersion", $version, "--packDir", $publishDir,
            "--mainExe", $exe, "--packTitle", "FlowRate", "--packAuthors", "puffer-bit", "--icon", $packIcon,
            "--channel", $channel, "-o", $work) + $portable + $releaseNotes) | Out-Null

        Copy-FeedVersion $work $channel $output
        # FlowRateClient-<channel>-Setup.exe or FlowRateClient-<channel>.AppImage
        $installers = @(Get-ChildItem $work | Where-Object { $_.Name -like "*-Setup.exe" -or $_.Name -like "*.AppImage" })
        if ($installers.Count -eq 0) { throw "vpk made no installer in $work" }
        $installers | Copy-Item -Destination $output
        # The next build makes its delta from this one
        Remove-Item -Recurse -Force $last
        Copy-FeedVersion $work $channel $last
    }
    finally {
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }

    Write-Host "######## Packing the standalone $platform"
    $extension = if ($os -eq "linux") { "tar.gz" } else { "zip" }
    New-StandaloneArchive $rid $publishDir (Join-Path $output "FlowRateClient-$version-$platform-standalone.$extension")

    return $output
}

# The Android client: a Release .apk for every ABI of the project, copied to dist\<version>\android\
function Publish-Android {
    $androidVersion = ([xml](Get-Content $androidProject)).Project.PropertyGroup |
        ForEach-Object { $_.ApplicationDisplayVersion } | Where-Object { $_ } | Select-Object -First 1

    Write-Host "######## FlowRate client $androidVersion for android"
    if (-not (dotnet workload list | Select-String -Quiet '^android ')) {
        throw "The android workload is missing, run: dotnet workload install android"
    }

    $signing = @()
    if ($env:ANDROID_KEYSTORE) {
        if (-not (Test-Path $env:ANDROID_KEYSTORE -PathType Leaf)) { throw "No keystore in $env:ANDROID_KEYSTORE" }
        if (-not $env:ANDROID_KEY_ALIAS) { throw "ANDROID_KEY_ALIAS is not set" }
        if (-not $env:ANDROID_KEYSTORE_PASSWORD) { throw "ANDROID_KEYSTORE_PASSWORD is not set" }
        if (-not $env:ANDROID_KEY_PASSWORD) { $env:ANDROID_KEY_PASSWORD = $env:ANDROID_KEYSTORE_PASSWORD }
        # env: the passwords stay out of the command line
        $signing = @(
            "-p:AndroidKeyStore=true",
            "-p:AndroidSigningKeyStore=$((Resolve-Path $env:ANDROID_KEYSTORE).Path)",
            "-p:AndroidSigningKeyAlias=$env:ANDROID_KEY_ALIAS",
            "-p:AndroidSigningStorePass=env:ANDROID_KEYSTORE_PASSWORD",
            "-p:AndroidSigningKeyPass=env:ANDROID_KEY_PASSWORD")
    } else {
        Write-Host "Note: ANDROID_KEYSTORE is not set, the .apk is signed with the debug key"
    }

    Write-Host "######## Publishing the client for android"
    if (Test-Path $androidPublishDir) { Remove-Item -Recurse -Force $androidPublishDir }
    # The signing step is skipped while its output is there, a change of the key alone does not rerun it
    Remove-Item (Join-Path (Split-Path $androidProject) "bin\Release\*\*-Signed.apk") -ErrorAction SilentlyContinue

    $ErrorActionPreference = "Continue"
    dotnet publish $androidProject -c Release -o $androidPublishDir -nologo -nodeReuse:false `
        -p:UseSharedCompilation=false @signing 2>&1 | ForEach-Object { "$_" }
    $publishExitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($publishExitCode -ne 0) { throw "dotnet publish for android failed with exit code $publishExitCode" }

    Write-Host "######## Packing android"
    $androidDir = Join-Path $versionDir "android"
    New-Item -ItemType Directory -Force -Path $androidDir | Out-Null
    $archive = Join-Path (Resolve-Path $androidDir).Path "FlowRateClient-$androidVersion-android.apk"
    Copy-Item (Join-Path $androidPublishDir "*-Signed.apk") $archive -Force
    Write-Host "Folder:  $androidPublishDir"
    Write-Host ("Archive: {0} ({1:N0} MB)" -f $archive, ((Get-Item $archive).Length / 1MB))
}

foreach ($rid in $targets) {
    if ($rid -eq "android") {
        Publish-Android
        continue
    }

    Write-Host "######## FlowRate client $version for $rid ($mode)"
    Initialize-GStreamer $rid

    $publishDir = Join-Path $publishRoot "$rid$suffix"
    # dotnet publish ignores a missing profile and publishes elsewhere
    $publishProfile = Join-Path (Split-Path $project) "Properties\PublishProfiles\$rid-$mode.pubxml"
    if (-not (Test-Path $publishProfile -PathType Leaf)) { throw "No publish profile $publishProfile" }

    Write-Host "######## Publishing the client for $rid"
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

    # Windows PowerShell turns any stderr line of a native command into a terminating error under "Stop"
    # once the output is redirected; the exit code is what tells whether the publish failed.
    # -r as well: the RID of the profile alone does not reach the restore.
    $ErrorActionPreference = "Continue"
    dotnet publish $project -p:PublishProfile="$rid-$mode" -r $rid -nologo 2>&1 | ForEach-Object { "$_" }
    $publishExitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($publishExitCode -ne 0) { throw "dotnet publish for $rid failed with exit code $publishExitCode" }

    $release = New-Release $rid $publishDir
    Write-Host "Published: $publishDir"
    Write-Host "Release:   $release"
}

Write-Host "######## Done: $versionDir"

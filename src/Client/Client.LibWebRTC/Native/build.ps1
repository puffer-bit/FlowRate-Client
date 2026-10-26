# Builds the win-x64 C interface over the pinned prebuilt of webrtc-sdk/libwebrtc.
# Needs Visual Studio 2022 or later with the x64 C++ build tools and a Windows SDK.
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($env:OS -ne "Windows_NT" -or [IntPtr]::Size -ne 8) {
    throw "Run this script in 64-bit PowerShell on Windows"
}

$version = "m150.7871.06"
$sha256 = "2a761095adaab51415d7c669e85df8e71d9aeb0d8199475fb8a88444af994179"
$build = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\Build"))
$downloads = Join-Path $build "downloads"
$out = Join-Path $build "artifacts\win-x64\libwebrtc"
$archive = Join-Path $downloads "libwebrtc-$version-win-x64-release.zip"
$sdk = Join-Path $downloads "libwebrtc-$version-win-x64"

# Import the x64 compiler environment even when invoked from ordinary PowerShell or GitHub Actions.
if ($env:VSCMD_ARG_TGT_ARCH -ne "x64" -or -not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere -PathType Leaf)) {
        throw "Install Visual Studio with Desktop development with C++ (including the Windows SDK)"
    }
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($LASTEXITCODE -ne 0 -or -not $installation) { throw "No Visual Studio x64 C++ build tools found" }
    $devCmd = Join-Path $installation "Common7\Tools\VsDevCmd.bat"
    $environment = & $env:ComSpec /d /s /c "`"`"$devCmd`" -no_logo -arch=x64 -host_arch=x64 >nul && set`""
    if ($LASTEXITCODE -ne 0) { throw "Could not initialize the Visual Studio x64 environment" }
    foreach ($line in $environment) {
        if ($line -match '^([^=]+)=(.*)$') {
            [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process')
        }
    }
}
if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) { throw "cl.exe is not available" }

New-Item -ItemType Directory -Force -Path $downloads, $out | Out-Null
if (-not (Test-Path $archive -PathType Leaf) -or (Get-FileHash $archive -Algorithm SHA256).Hash -ne $sha256) {
    Write-Host "######## Downloading libwebrtc $version for Windows x64"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/webrtc-sdk/libwebrtc/releases/download/libwebrtc.$version/libwebrtc-win-x64-release.zip" -OutFile "$archive.part"
    if ((Get-FileHash "$archive.part" -Algorithm SHA256).Hash -ne $sha256) {
        throw "SHA-256 mismatch for $archive.part"
    }
    Move-Item "$archive.part" $archive -Force
}

# Each extraction has its own empty directory; no tools are run from the downloaded SDK.
$extract = Join-Path $downloads ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $extract | Out-Null
try {
    Expand-Archive -LiteralPath $archive -DestinationPath $extract
    $unpacked = Join-Path $extract "libwebrtc-x64-release"
    foreach ($file in @('include\libwebrtc.h', 'lib\libwebrtc.dll', 'lib\libwebrtc.dll.lib', 'LICENSE')) {
        if (-not (Test-Path (Join-Path $unpacked $file) -PathType Leaf)) { throw "No $file in the Windows SDK" }
    }
    # Use a fresh verified SDK for each build rather than headers left over from an earlier extraction.
    if (Test-Path $sdk) { Remove-Item $sdk -Recurse -Force }
    Move-Item $unpacked $sdk
} finally {
    Remove-Item $extract -Recurse -Force
}

Write-Host "######## Building flowrate_webrtc.dll"
$arguments = @('/nologo', '/std:c++17', '/O2', '/EHsc', '/MT', '/LD', '/W3',
    '/DWIN32', '/DNOMINMAX', '/DLIB_WEBRTC_API_DLL',
    "/I$sdk\include", (Join-Path $PSScriptRoot 'flowrate_webrtc.cc'),
    "/Fo$out\flowrate_webrtc.obj", "/Fe$out\flowrate_webrtc.dll",
    '/link', '/MACHINE:X64', (Join-Path $sdk 'lib\libwebrtc.dll.lib'),
    "/IMPLIB:$out\flowrate_webrtc.lib")
# /MT keeps the shim independent of a Visual C++ redistributable, like the prebuilt DLL.
$ErrorActionPreference = "Continue"
& cl.exe @arguments 2>&1 | ForEach-Object { Write-Host "$_" }
$compileExitCode = $LASTEXITCODE
$ErrorActionPreference = "Stop"
if ($compileExitCode -ne 0) { throw "Building flowrate_webrtc.dll failed with exit code $compileExitCode" }

Copy-Item (Join-Path $sdk 'lib\libwebrtc.dll') $out -Force
Copy-Item (Join-Path $sdk 'LICENSE') (Join-Path $out 'LICENSE.libwebrtc') -Force
& (Join-Path $PSScriptRoot 'test-runtime.ps1') -RuntimeDirectory $out
Write-Host "######## Done: $out"
Get-ChildItem $out

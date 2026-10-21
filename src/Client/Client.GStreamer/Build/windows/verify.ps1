<#
.SYNOPSIS
Checks a collected win-x64 GStreamer bundle the way the client uses it.

.DESCRIPTION
Runs the bundled gst-inspect with the environment Loader/GstLoader.cs sets and a PATH holding nothing
but the bundle and Windows itself, so an installed GStreamer cannot take part. Checks that every
element of required-elements.txt exists, and that every plugin loads from the bundle and is not GPL.

collect.ps1 runs it on the build machine. For a check on a clean system, copy the bundle folder and
this folder (verify.ps1, required-elements.txt) into Windows Sandbox and run it there.

.PARAMETER Bundle
The folder collect.ps1 produced: it holds gstreamer\ and tools\gst-inspect-1.0.exe.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File verify.ps1
powershell -ExecutionPolicy Bypass -File verify.ps1 -Bundle C:\Users\WDAGUtilityAccount\Desktop\win-x64
#>
[CmdletBinding()]
param(
    [string]$Bundle = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\win-x64")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Bundle = (Resolve-Path $Bundle).Path
$dest = Join-Path $Bundle "gstreamer"
$inspect = Join-Path $Bundle "tools\gst-inspect-1.0.exe"
$work = Join-Path $env:TEMP "flowrate-gstreamer-verify"

foreach ($required in @($dest, $inspect)) {
    if (-not (Test-Path $required)) { throw "$required not found, run collect.ps1 first" }
}

if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force -Path (Join-Path $work "gio-modules") | Out-Null

# The caller's environment comes back at the end, collect.ps1 continues in the same session
$variables = "PATH", "GST_PLUGIN_SYSTEM_PATH_1_0", "GST_PLUGIN_PATH_1_0", "GST_PLUGIN_SCANNER_1_0",
    "GST_REGISTRY_1_0", "GIO_MODULE_DIR"
$saved = @{}
foreach ($name in $variables) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }

$failed = $false
function Fail([string]$message) {
    Write-Host "FAIL: $message" -ForegroundColor Red
    $script:failed = $true
}

try {
    $env:PATH = "$(Join-Path $dest 'bin');$env:SystemRoot\System32;$env:SystemRoot"
    $env:GST_PLUGIN_SYSTEM_PATH_1_0 = Join-Path $dest "lib\gstreamer-1.0"
    $env:GST_PLUGIN_PATH_1_0 = ""
    $env:GST_PLUGIN_SCANNER_1_0 = Join-Path $dest "libexec\gstreamer-1.0\gst-plugin-scanner.exe"
    $env:GST_REGISTRY_1_0 = Join-Path $work "registry.bin"
    $env:GIO_MODULE_DIR = Join-Path $work "gio-modules"

    Write-Host "== Version"
    & $inspect --version | Select-Object -First 2 | Write-Host

    Write-Host "== Elements"
    foreach ($line in Get-Content (Join-Path $PSScriptRoot "required-elements.txt")) {
        $element = ($line -replace '#.*$', '').Trim()
        if ($element -eq "") { continue }

        $optional = $element.StartsWith("?")
        $element = $element.TrimStart("?")
        & $inspect --exists $element
        if ($LASTEXITCODE -ne 0) {
            if ($optional) { Write-Host "  optional element missing: $element" }
            else { Fail "required element missing: $element" }
        }
    }

    Write-Host "== Plugins: load from the bundle, licenses"
    # By file rather than by registry name: hardware plugins without their device register no elements
    foreach ($file in Get-ChildItem -Path (Join-Path $dest "lib\gstreamer-1.0") -Filter *.dll) {
        $details = & $inspect $file.FullName 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { Fail "plugin $($file.Name) does not load (a DLL missing from bin?)"; continue }

        $license = if ($details -match '(?m)^\s*License\s+(.+)$') { $Matches[1].Trim() } else { "?" }
        $filename = if ($details -match '(?m)^\s*Filename\s+(.+)$') { $Matches[1].Trim() } else { "" }

        if (-not $filename.StartsWith($dest, [StringComparison]::OrdinalIgnoreCase)) {
            Fail "plugin $($file.Name) loaded from $filename"
        }
        if ($license -match 'GPL' -and $license -notmatch 'LGPL') { Fail "plugin $($file.Name) has license $license" }
        Write-Host ("  {0,-28} {1}" -f $file.Name, $license)
    }
}
finally {
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

if ($failed) { throw "Verification failed" }
Write-Host "== Verification passed"

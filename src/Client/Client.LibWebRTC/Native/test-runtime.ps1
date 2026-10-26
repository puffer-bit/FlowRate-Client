# Checks the DLLs and their C exports without opening audio devices (also works on CI runners).
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if ($env:OS -ne "Windows_NT" -or [IntPtr]::Size -ne 8) {
    throw "Run this check in 64-bit PowerShell on Windows"
}
$runtime = (Resolve-Path $RuntimeDirectory).Path
foreach ($file in @('libwebrtc.dll', 'flowrate_webrtc.dll')) {
    if (-not (Test-Path (Join-Path $runtime $file) -PathType Leaf)) { throw "No $file in $runtime" }
}

if (-not ('FlowRate.NativeLibraryCheck' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace FlowRate {
    public static class NativeLibraryCheck {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr LoadLibraryExW(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")]
        public static extern bool FreeLibrary(IntPtr module);
    }
}
'@
}

# Resolve dependencies next to the shim, just as in the installed client, not from the SDK or PATH.
$module = [FlowRate.NativeLibraryCheck]::LoadLibraryExW((Join-Path $runtime 'flowrate_webrtc.dll'), [IntPtr]::Zero, 0x1100)
if ($module -eq [IntPtr]::Zero) {
    $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "Cannot load flowrate_webrtc.dll or its dependencies (Win32 error $code)"
}
try {
    $header = Get-Content (Join-Path $PSScriptRoot 'flowrate_webrtc.h') -Raw
    $exports = [regex]::Matches($header, '\b(fr_\w+)\(') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    foreach ($name in $exports) {
        if ([FlowRate.NativeLibraryCheck]::GetProcAddress($module, $name) -eq [IntPtr]::Zero) {
            throw "Missing C export $name in flowrate_webrtc.dll"
        }
    }
    Write-Host "libwebrtc runtime loads; all $($exports.Count) C exports found in $runtime"
} finally {
    [void][FlowRate.NativeLibraryCheck]::FreeLibrary($module)
}

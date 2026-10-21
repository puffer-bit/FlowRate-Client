<#
.SYNOPSIS
Stops the build servers dotnet leaves behind (MSBuild nodes, the compiler server).

.DESCRIPTION
Shows how much memory the ones still alive hold. Those belong to a running IDE, which starts its own.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File stop-servers.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Prints the number of build server processes and their memory
function Write-Report([string]$label) {
    $servers = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match 'MSBuild\.dll|VBCSCompiler' })
    $memory = ($servers | Measure-Object WorkingSetSize -Sum).Sum / 1MB
    Write-Host ("{0} {1} processes, {2:N0} MiB" -f $label, $servers.Count, $memory)
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "The .NET SDK (dotnet) is not installed" }

Write-Report "Before:"
$ErrorActionPreference = "Continue"
dotnet build-server shutdown 2>&1 | Out-Null
$ErrorActionPreference = "Stop"
Write-Report "After: "

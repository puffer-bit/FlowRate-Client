<#
.SYNOPSIS
Removes everything dotnet builds: the bin\ and obj\ folders of every project in src\.

.DESCRIPTION
Stops the build servers first, they keep files of obj\ open. The next build starts with a full restore.

Only folders next to a .csproj are removed: the GStreamer runtime in Client.GStreamer\Build\artifacts
has a bin\ folder of its own (the Windows DLLs) and stays as it is, and so does dist\.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File clean.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Set-Location (Resolve-Path (Join-Path $PSScriptRoot "..\.."))

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $ErrorActionPreference = "Continue"
    dotnet build-server shutdown 2>&1 | Out-Null
    $ErrorActionPreference = "Stop"
}

$count = 0
$projects = Get-ChildItem -Recurse -Filter *.csproj | Where-Object { $_.FullName -notmatch '[\\/]Build[\\/]artifacts[\\/]' }
foreach ($project in $projects) {
    foreach ($name in "bin", "obj") {
        $folder = Join-Path $project.DirectoryName $name
        if (-not (Test-Path $folder)) { continue }
        Remove-Item -Recurse -Force $folder
        $count++
    }
}

Write-Host "Removed $count folders"

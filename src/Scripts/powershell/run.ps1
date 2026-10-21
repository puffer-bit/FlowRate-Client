<#
.SYNOPSIS
Builds the desktop client and starts it.

.DESCRIPTION
A Debug build runs on the installed GStreamer when the bundled runtime for this platform is missing.

.PARAMETER Configuration
Debug (the default) or Release.

.PARAMETER Arguments
Anything else goes to the client.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File run.ps1
powershell -ExecutionPolicy Bypass -File run.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Set-Location (Resolve-Path (Join-Path $PSScriptRoot "..\.."))

$project = "Client\Client.Avalonia.Desktop"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "The .NET SDK (dotnet) is not installed" }

# Windows PowerShell turns any stderr line of a native command into a terminating error under "Stop";
# the exit code is what tells whether the build failed
$ErrorActionPreference = "Continue"
dotnet build $project -c $Configuration -nologo -v quiet -nodeReuse:false -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet run --project $project -c $Configuration --no-build -- @Arguments
exit $LASTEXITCODE

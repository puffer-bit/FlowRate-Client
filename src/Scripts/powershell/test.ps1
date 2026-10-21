<#
.SYNOPSIS
Runs the tests of the whole solution.

.DESCRIPTION
No MSBuild node and no compiler server outlives the run.

.PARAMETER Arguments
Anything goes to dotnet test as is.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File test.ps1
powershell -ExecutionPolicy Bypass -File test.ps1 --filter Name~Gate
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Set-Location (Resolve-Path (Join-Path $PSScriptRoot "..\.."))

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "The .NET SDK (dotnet) is not installed" }

# Windows PowerShell turns any stderr line of a native command into a terminating error under "Stop";
# the exit code is what tells whether the tests failed
$ErrorActionPreference = "Continue"
dotnet test FlowRateClient.sln -nologo -nodeReuse:false -p:UseSharedCompilation=false @Arguments
exit $LASTEXITCODE

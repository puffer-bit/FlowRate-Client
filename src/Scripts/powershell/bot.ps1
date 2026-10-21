<#
.SYNOPSIS
Builds the test bot and starts it.

.DESCRIPTION
The bot is a headless client that joins a voice session as a regular user and plays a looped file or
a tone. The proxy variables are dropped for the bot: a local server is not reachable through a proxy.

.PARAMETER Arguments
All arguments go to the bot, --help lists its options.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File bot.ps1 --server 127.0.0.1:26666
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Set-Location (Resolve-Path (Join-Path $PSScriptRoot "..\.."))

$project = "Client\Client.TestBot"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "The .NET SDK (dotnet) is not installed" }

# Windows PowerShell turns any stderr line of a native command into a terminating error under "Stop";
# the exit code is what tells whether the build failed
$ErrorActionPreference = "Continue"
dotnet build $project -nologo -v quiet -nodeReuse:false -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($name in "HTTP_PROXY", "HTTPS_PROXY") { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
dotnet run --project $project --no-build -- @Arguments
exit $LASTEXITCODE

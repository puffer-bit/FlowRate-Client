#!/usr/bin/env bash
# Builds the test bot and starts it: a headless client that joins a voice session as a regular user
# and plays a looped file or a tone. All arguments go to the bot.
#
# Usage: ./bot.sh --server 127.0.0.1:26666
#        ./bot.sh --help                  the options of the bot itself
#
# The proxy variables are dropped for the bot: a local server is not reachable through a proxy.
set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")/../.."

PROJECT=Client/Client.TestBot

command -v dotnet >/dev/null || { echo "error: dotnet is not installed" >&2; exit 1; }
dotnet build "$PROJECT" -nologo -v quiet -nodeReuse:false -p:UseSharedCompilation=false
exec env -u HTTP_PROXY -u HTTPS_PROXY -u http_proxy -u https_proxy \
    dotnet run --project "$PROJECT" --no-build -- "$@"

#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet restore src/ZenithClipper.App --configfile NuGet.Config -r linux-x64
dotnet publish src/ZenithClipper.App -c Release -r linux-x64 --self-contained false --no-restore -o build/linux
chmod +x build/linux/zenithclipper linux/*.sh
printf 'Built build/linux/zenithclipper (requires .NET 10 runtime).\n'

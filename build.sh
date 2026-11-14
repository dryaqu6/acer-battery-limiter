#!/bin/bash
# Build the app and the installer on macOS / Linux.
# Needs: .NET SDK (dotnet), NSIS (makensis). Output: dist/AcerBatteryLimiter-Setup-<version>.exe
set -euo pipefail
cd "$(dirname "$0")"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/AcerBatteryLimiter/AcerBatteryLimiter.csproj)
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

"$DOTNET" build src/AcerBatteryLimiter/AcerBatteryLimiter.csproj -c Release -nologo -v quiet
mkdir -p dist
makensis -V2 -DVERSION="$VERSION" installer/installer.nsi
ls -l dist/

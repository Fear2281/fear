#!/usr/bin/env bash
set -euo pipefail
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES=/workspace/.nuget/packages
if [ ! -x /workspace/.dotnet/dotnet ]; then
  curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh --output /tmp/encore-dotnet-install.sh
  bash /tmp/encore-dotnet-install.sh --version 8.0.425 --install-dir /workspace/.dotnet --no-path
fi
export PATH="/workspace/.dotnet:$PATH"
cd /workspace/fear
dotnet restore --locked-mode
dotnet build --no-restore --nologo
python scripts/smoke.py

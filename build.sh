#!/usr/bin/env bash
# Builds NavalPower.dll and, with --install, copies it into BepInEx/plugins.
set -euo pipefail
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
: "${NUCLEAR_OPTION_GAME:=/run/media/caleb/NVME500/SteamLibrary/steamapps/common/Nuclear Option}"
export NUCLEAR_OPTION_GAME

cd "$(dirname "$0")"
dotnet build -c Release --nologo "$@"

if [[ " $* " == *" --install "* ]]; then
    dest="$NUCLEAR_OPTION_GAME/BepInEx/plugins/NavalPower"
    mkdir -p "$dest"
    cp bin/Release/NavalPower.dll "$dest/"
    echo "installed -> $dest/NavalPower.dll"
fi

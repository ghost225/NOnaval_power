#!/usr/bin/env bash
# Builds NavalPower.dll and, with --install, copies it into BepInEx/plugins.
# With --when-closed as well, an install refused because the game is running
# waits for the game to exit and then installs the newest build. One waiter at
# a time: a later build while one is waiting needs nothing more, since the
# waiter copies whatever the newest DLL is when the game closes.
set -euo pipefail
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
: "${NUCLEAR_OPTION_GAME:=/run/media/caleb/NVME500/SteamLibrary/steamapps/common/Nuclear Option}"
export NUCLEAR_OPTION_GAME

cd "$(dirname "$0")"

install=0
wait_for_close=0
args=()
for arg in "$@"; do
    case "$arg" in
        --install) install=1 ;;
        --when-closed) install=1; wait_for_close=1 ;;
        *) args+=("$arg") ;;
    esac
done

dotnet build -c Release --nologo ${args[@]+"${args[@]}"}

if (( install )); then
    # Overwriting the DLL while Mono has it mapped corrupts the loaded image and
    # takes the game down with "BadImageFormatException: Method has zero rva".
    # Match the game's executable only. A bare "NuclearOption" also matches any
    # shell whose command line merely mentions it -- including the one running
    # this -- and refused installs with the game closed. The bracket keeps this
    # pattern from matching its own text.
    if pgrep -f 'NuclearOption[.]exe' >/dev/null 2>&1; then
        if (( ! wait_for_close )); then
            echo "REFUSING TO INSTALL: Nuclear Option is running." >&2
            echo "Close the game first, then re-run with --install (or use --when-closed)." >&2
            exit 1
        fi
        lock="${TMPDIR:-/tmp}/navalpower-install.lock"
        exec 9>"$lock"
        if ! flock -n 9; then
            echo "install already queued for when the game closes; it will take this build"
            exit 0
        fi
        echo "game running: install queued for when it closes"
        while pgrep -f 'NuclearOption[.]exe' >/dev/null 2>&1; do sleep 5; done
        sleep 3          # let the process let go of its files
    fi
    dest="$NUCLEAR_OPTION_GAME/BepInEx/plugins/NavalPower"
    mkdir -p "$dest"
    cp bin/Release/NavalPower.dll "$dest/"
    echo "installed -> $dest/NavalPower.dll"
fi

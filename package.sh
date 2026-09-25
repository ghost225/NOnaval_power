#!/usr/bin/env bash
# Builds the release zip for GitHub and NOMNOM: build/NavalPower_<version>.zip.
#
# NOMM unpacks a mod's zip straight into BepInEx/plugins/<mod id>/, so the
# DLL sits at the root of the archive, not under BepInEx/plugins.
set -euo pipefail
cd "$(dirname "$0")"

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' NavalPower.csproj)
plugin=$(sed -n 's:.*const string Version = "\(.*\)";.*:\1:p' src/NavalPower/Plugin.cs)
if [[ -z "$version" || "$version" != "$plugin" ]]; then
    echo "Version mismatch: NavalPower.csproj says '$version', Plugin.cs says '$plugin'." >&2
    exit 1
fi

./build.sh

stage=build/stage
zip="build/NavalPower_${version}.zip"
rm -rf "$stage" "$zip"
mkdir -p "$stage"
cp bin/Release/NavalPower.dll README.md LICENSE THIRD_PARTY_NOTICES.md "$stage/"
(cd "$stage" && zip -q -X "../$(basename "$zip")" ./*)
rm -rf "$stage"

echo
echo "  $zip"
echo "  version  $version  (tag the release v$version)"
echo "  sha256:$(sha256sum "$zip" | cut -d' ' -f1)"

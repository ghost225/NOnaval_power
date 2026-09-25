#!/usr/bin/env bash
# Builds the release file for GitHub and NOMNOM: build/NavalPower.dll.
#
# A single DLL ships bare, as most single-DLL mods on NOMNOM do: NOMM writes a
# non-archive download straight into BepInEx/plugins/<mod id>/. The licence
# and third-party notices live in the repository and are linked from the
# release notes.
set -euo pipefail
cd "$(dirname "$0")"

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' NavalPower.csproj)
plugin=$(sed -n 's:.*const string Version = "\(.*\)";.*:\1:p' src/NavalPower/Plugin.cs)
if [[ -z "$version" || "$version" != "$plugin" ]]; then
    echo "Version mismatch: NavalPower.csproj says '$version', Plugin.cs says '$plugin'." >&2
    exit 1
fi

# The SDK stamps the current commit into the DLL's version, and NOMNOM checks
# releases against the published source, so build only a commit that is
# committed and on GitHub -- the one the release tag will point at.
if [[ -n "$(git status --porcelain)" ]]; then
    echo "Uncommitted changes. Commit and push first, then package." >&2
    exit 1
fi
git fetch -q origin
if [[ "$(git rev-parse HEAD)" != "$(git rev-parse '@{u}')" ]]; then
    echo "HEAD is not what GitHub has. Pull or push first, then package." >&2
    exit 1
fi

./build.sh

mkdir -p build
out=build/NavalPower.dll
cp bin/Release/NavalPower.dll "$out"

echo
echo "  $out"
echo "  version  $version  (tag the release v$version)"
echo "  sha256:$(sha256sum "$out" | cut -d' ' -f1)"

# Releasing, and listing on NOMNOM

NOMNOM (<https://github.com/KopterBuzz/NOMNOM>) is the registry NOMM installs
from. The first listing is a pull request; after that, new GitHub releases are
picked up automatically.

## Every release

1. Bump the version in **both** `NavalPower.csproj` and `src/NavalPower/Plugin.cs`.
   NOMNOM requires the manifest version to match the DLL's.
2. `./package.sh` builds `build/NavalPower_<version>.zip` and prints its SHA-256.
3. Create a GitHub release on `ghost225/NOnaval_power` tagged `v<version>`, with
   that zip as the **first** (or only) asset.

## First listing only

1. Fork `KopterBuzz/NOMNOM`.
2. Copy `docs/nomnom/NavalPower.json` to `modManifests/NavalPower.json` in the fork.
3. Fill in `hash` from the release page (the `sha256:…` value; the copy button
   beside the asset gives it in the right form) and check `downloadUrl` opens.
4. Open a pull request against `main`. A workflow validates the schema; a person
   then reviews it.

## Their acceptance policy, and where we stand

- **Source must be open, unobfuscated, and match the DLL.** The repository is
  public; release builds come straight from `./package.sh`.
- **Third-party work credited with its licence.** See `THIRD_PARTY_NOTICES.md`.
- **No game assets.** None are included.
- **One mod per repository**, releases on GitHub, a parseable tag (`v0.1.0`),
  BepInEx 5. All met.

After listing, the image and client/server status are changed through issues on
NOMNOM (`HOWTO_UPDATE_MODIMAGE.md`, `HOWTO_UPDATE_ISCLIENTORSERVER.md`); an
image must be at most 512×512.

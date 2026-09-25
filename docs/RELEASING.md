# Releasing, and listing on NOMNOM

NOMNOM (<https://github.com/KopterBuzz/NOMNOM>) is the registry NOMM installs
from. The first listing is a pull request; after that, new GitHub releases are
picked up automatically.

## Every release

1. Bump the version in **both** `NavalPower.csproj` and `src/NavalPower/Plugin.cs`.
   NOMNOM requires the manifest version to match the DLL's.
2. Pull (the README may have been edited on GitHub), commit, and push.
3. `./package.sh` builds `build/NavalPower.dll` and prints its SHA-256. It
   refuses to run on uncommitted or unpushed work: the DLL carries the commit
   it was built from, and must match the tagged source.
4. Create a GitHub release on `ghost225/NOnaval_power` tagged `v<version>`, with
   that DLL as the only asset, and link LICENSE and THIRD_PARTY_NOTICES.md in
   the notes, since the DLL ships without them.

## First listing only

1. Fork `KopterBuzz/NOMNOM`.
2. Copy `docs/nomnom/NavalPower.json` to `modManifests/NavalPower.json` in the fork.
3. Fill in `hash` from the release page (the `sha256:…` value; the copy button
   beside the asset gives it in the right form) and check `downloadUrl` opens.
4. Open a pull request against `main`.

The incompatibility with Resolute Command (`com.resolute.command`) needs no
upkeep. NOMNOM's schema insists on a version there, but NOMM only shows the
entry and links to that mod; it never compares the number. A workflow validates the schema; a person
   then reviews it.

## Their acceptance policy, and where we stand

- **Source must be open, unobfuscated, and match the DLL.** The repository is
  public; release builds come straight from `./package.sh`.
- **Third-party work credited with its licence.** See `THIRD_PARTY_NOTICES.md`.
- **No game assets.** None are included.
- **One mod per repository**, releases on GitHub, a parseable tag (`v0.1.0`),
  BepInEx 5. All met.

The mod image is `assets/icon.png` (512×333; NOMNOM's limit is 512×512, and
PNG because their size check reads it reliably). The draft manifest points at it
through a link pinned to a commit, so its `imageHash` stays valid. If the
reviewers want images set their way instead, remove `imageUrl`/`imageHash` from
the pull request and use the *Update Mod Image URL* issue after listing, with
the same link (`HOWTO_UPDATE_MODIMAGE.md`). Client/server status is changed the
same way (`HOWTO_UPDATE_ISCLIENTORSERVER.md`).

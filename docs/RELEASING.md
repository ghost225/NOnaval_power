# Releasing, and listing on NOMNOM

NOMNOM (<https://github.com/KopterBuzz/NOMNOM>) is the registry NOMM installs
from. Naval Power is listed: the first listing (pull request #389, with 1.0.5
to 1.0.7) was merged on 2026-10-03. From now on new GitHub releases are picked
up automatically; no pull request per release.

## How the pick-up works

The manifest has `autoUpdateArtifacts: "True"`, so NOMNOM's hourly workflow
(`Update-ModArtifact.ps1`) reads our GitHub releases and adds any whose tag
(with `v` and `-pre` stripped) is newer than the newest version listed:

- **Download:** the release's *first* asset, so the DLL must be the only (or
  first) asset. The hash is GitHub's own digest of it.
- **Copied from the previous entry:** `gameVersion`, `type` and
  `incompatibilities`.
- **Drafts** are skipped. A GitHub **pre-release** is listed as `preRelease`.
- **Timing:** a published release reaches NOMM users within the hour. Publish
  only what should ship.

A new **game version** (say 0.34.2) is not picked up that way: `gameVersion`
is copied forward. Changing it takes a pull request to `modManifests/NavalPower.json`
on NOMNOM (or one of their update issues), with the new value on the newest
entry. `docs/nomnom/NavalPower.json` is our copy of what was listed; it no
longer needs updating per release.

## Every release

The shared execution code lives in the NOrders submodule (`norders/`,
github.com/ghost225/NOrders); clone with `--recurse-submodules`, or run
`git submodule update --init`. Commit and push any NOrders change there
first, then commit Naval Power with the submodule pointer. After the
release, tag the pinned NOrders commit with the same version
(`git -C norders tag v<version> && git -C norders push origin v<version>`)
so each mod pins a known commit.

1. Bump the version in **both** `NavalPower.csproj` and `src/NavalPower/Plugin.cs`.
   NOMNOM requires the manifest version to match the DLL's.
2. Pull (the README may have been edited on GitHub), commit, and push.
3. `./package.sh` builds `build/NavalPower.dll` and prints its SHA-256. It
   refuses to run on uncommitted or unpushed work: the DLL carries the commit
   it was built from, and must match the tagged source.
4. Create a GitHub release on `ghost225/NOnaval_power` tagged `v<version>`, with
   that DLL as the only asset, and link LICENSE and THIRD_PARTY_NOTICES.md in
   the notes, since the DLL ships without them.

## First listing (done, kept for reference)

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

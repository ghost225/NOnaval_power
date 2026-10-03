# Naval Power

Command ships, airfields and their air wings in Nuclear Option. A BepInEx 5
plugin; no other mods required.

naval Command
<img width="1920" height="1080" alt="Screenshot From 2026-09-25 11-52-40" src="https://github.com/user-attachments/assets/8866b025-8754-4924-9f01-36cff1c732d3" />

Airbase command:
<img width="1920" height="1080" alt="Screenshot From 2026-09-25 11-40-23" src="https://github.com/user-attachments/assets/9a786a29-eff3-4899-8199-e1057dee145a" />


## What it does

- **Any ship, not one hull.** Course and speed, waypoint routes, manual weapon
  orders with salvo size, rules of engagement, EMCON and passive ESM, damage
  control and replenishment.
- **Air operations.** Launch from carriers, helicopter decks and land airfields
  with a loadout per station, fuel, callsign and livery. Send flights to work an
  area, fly a route, strike a target, jam, deliver cargo or keep station.
  Every flight you launch shows in one list, whichever deck or field it flew from.
- **Take the controls.** Fly any of your flights yourself, then hand it back to
  its task or send it home.
- **A Sea Power–style interface.** A status strip and a row of tools that open
  windows you can stack and drag, a map you can dock beside the world view,
  pinned camera feeds, and a compass tape.
- **An economy that means something (optional).** Launches that aren't drawn
  from the reserve cost your own allocation, and bringing a flight home pays
  the sortie bonus.

## Install

With [NOMM](https://github.com/Combat787/NuclearOptionModManager): search for
*Naval Power*.

By hand: install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases), then
put `NavalPower.dll` from the latest release in `BepInEx/plugins/NavalPower/`.

Commanding requires single-player or being the mission host.

Not compatible with [Resolute Command](https://github.com/RValeWorks/Resolute-Command):
both take over the same map controls.

Mod aircraft: Naval Power flies whatever is in the hangar, but it is tuned and
tested on the game's own aircraft. Mod aircraft may not handle well under AI
control (the F-16M King Viper especially: hard manoeuvres while evading, and
carrier take-offs). No promises they behave.

## Use

- **Ships:** follow a friendly ship with the spectator camera.
- **Airfields:** shift-click a friendly airbase on the map, or pick one under
  Air Operations → *Command an airfield*.
- **Orders:** right-click the map to set a waypoint (shift appends a leg), or
  right-click a contact for its menu. With a flight's window open, right-clicks
  task that flight instead.
- **Getting back in:** **F10** re-enters command, and hands back an aircraft
  you're flying.

Settings are in BepInEx ConfigurationManager (F1) under *Naval Power*. Tuning
knobs and the diagnostic logs (under *Diagnostics*, all off by default) show
when *Advanced settings* is ticked. Turn the relevant trace on when reporting a
bug.

## Build

Needs the .NET SDK and a Nuclear Option install with BepInEx 5.

```bash
NUCLEAR_OPTION_GAME="/path/to/Nuclear Option" ./build.sh --install
# or, with the game running: build now, install when it closes
NUCLEAR_OPTION_GAME="/path/to/Nuclear Option" ./build.sh --when-closed
```

`./package.sh` builds the release DLL. [RELEASING.md](docs/RELEASING.md) covers
publishing.

## Credits

Naval Power builds on the work of other Nuclear Option modders. Thank you to:

- **[Resolute Command](https://github.com/RValeWorks/Resolute-Command)** by
  RValeWorks. Naval Power began from its code: the map command layer, input
  handling and map overlay are adapted from it. MIT licence.
- **[NO Commander](https://github.com/DontKnowWhatImDoingHere/NOCommander)** by
  rosa.clara. Its approach to AI cargo and helicopter transport missions
  informed ours. Public domain (Unlicense).
- **[NOAutopilot](https://github.com/qwerty1423/no-autopilot-mod)** by qwerty1423
  and contributors. Its carrier auto-land and patch safety net informed ours.
  MIT licence.

Licence texts are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Disclaimer

This project is an unofficial community modification and is not affiliated with, sponsored by, or endorsed by Shockfront Studios Pty Ltd. Original Nuclear Option assets, vehicle designs, audio, and code are Copyright (c) 2026 Shockfront Studios Pty Ltd. All rights reserved. Nuclear Option and Shockfront Studios are trademarks or registered trademarks of Shockfront Studios Pty Ltd. Original mod content and all other trademarks belong to their respective owners.

## Licence

[MIT](LICENSE)

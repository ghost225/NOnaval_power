# Naval Power

Command ships, airfields and their air wings in Nuclear Option. A BepInEx 5
plugin; no other mods required.

<!-- Screenshots go here. -->

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

## Use

- **Ships:** follow a friendly ship with the spectator camera.
- **Airfields:** shift-click a friendly airbase on the map, or pick one under
  Air Operations → *Command an airfield*.
- **Orders:** right-click the map to set a waypoint (shift appends a leg), or
  right-click a contact for its menu. With a flight's window open, right-clicks
  task that flight instead.
- **Getting back in:** **F10** re-enters command, and hands back an aircraft
  you're flying.

Settings are in BepInEx ConfigurationManager (F1) under *Naval Power*.

## Build

Needs the .NET SDK and a Nuclear Option install with BepInEx 5.

```bash
NUCLEAR_OPTION_GAME="/path/to/Nuclear Option" ./build.sh --install
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

Not affiliated with Shockfront Studios.

## Licence

[MIT](LICENSE)

# Naval Power

Command any ship in Nuclear Option — course, speed, and manual weapon orders —
rather than one specific hull. A BepInEx 5 plugin, no dependency on other mods.

## Status

Early but usable. Course, speed and manual weapon orders work on stock hulls.
A screen-space command bar appears whenever the spectator camera follows a
commandable ship; the keybind harness below is kept for diagnostics.

## Design

Ships are gated by capability, not by identity: any `Ship` with a `ShipAI` and a
`UnitCommand` can be commanded. That covers `ShipAI` subclasses such as
`AssaultCarrierAI`, `LandingCraftAI` and third-party ship AI.

Navigation orders go through `UnitCommand.SetDestination`, the public native
order path, rather than a Harmony patch on `ShipAI.Steer`. `Steer` is `protected
virtual` and several hulls override it, so a patch on the base method silently
does nothing on those ships. Route legs are advanced by this mod so the ship does
not sit through the native multi-minute arrival hold between waypoints, and the
speed governor writes `ShipInputs.throttle` from `LateUpdate`, after `ShipAI.Steer`
has run inside `Update`.

## Building

Needs a .NET SDK and a local Nuclear Option install with BepInEx 5.

```bash
NUCLEAR_OPTION_GAME="/path/to/Nuclear Option" ./build.sh --install
```

## Command bar

Follow a friendly ship with the spectator camera and the bar appears.

- **Speed** — slider, or All stop / 1/3 / 2/3 / Full / Flank. "Release speed"
  hands the throttle back to the native controller.
- **Route** — "Waypoint ahead" sets a leg 5 km off the bow; "Clear route"
  returns the ship to autonomous navigation.
- **Engaging** — click a weapon, pick a salvo size, then click a contact on the
  right. Contacts are ranked by what the selected weapon can actually hurt, and
  ones it cannot are greyed out.

Targets are chosen from the contact list rather than the map: native map and
world input arbitration comes later.

## Test harness keys

Follow a friendly ship with the spectator camera, then:

| Key | Action |
| --- | --- |
| `F6` | Report ship state: AI type, speed, throttle, weapons, turret targets |
| `F7` | Order the first ready weapon at the nearest hostile |
| `F8` | Cease fire |
| `F9` | Waypoint 5 km off the bow |
| `Home` / `End` | All stop / ahead flank |

Output goes to `BepInEx/LogOutput.log`.

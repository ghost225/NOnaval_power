# Naval Power

Command any ship in Nuclear Option — course, speed, and manual weapon orders —
rather than one specific hull. A BepInEx 5 plugin, no dependency on other mods.

## Status

Early. Steps 1 and 2 of the plan are implemented; the UI does not exist yet and
orders are issued through a keybind test harness while the core is validated.

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

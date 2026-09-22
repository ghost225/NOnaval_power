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

## Using it

Follow a friendly ship with the spectator camera; the command bar appears and
orders are given on the native map.

- **Right-click the map** — set a course waypoint. Hold shift to append a leg.
- **Right-click a contact** — opens a context menu: engage with, navigate,
  engagement permissions, cease fire. With a weapon already selected, a
  right-click on a contact orders the attack directly.
- **Hover a contact** — bearing, range, altitude, speed, how stale the track is,
  and whether the selected weapon can reach it.
- **Bar** — speed slider and telegraph presets, clear route, rules of
  engagement, cease fire, weapon selection and salvo size.

Weapons a contact is immune to are shown greyed as "ineffective", from the
game's own RoleIdentity/TypeIdentity scoring rather than a table of our own.

### Sensors and EMCON

`Radar` derives from `TargetDetector`, so a hull carries a mix of emitters and
passive sensors. EMCON silences only the emitters: switching off a passive
sensor would not reduce the ship's signature, it would just blind it.

The sensor menu lists each sensor with its state, range and track count, and
toggles emitters individually or all at once. Active emitter coverage is drawn
on the map, so going silent visibly shuts the picture down.

Component names are made readable by convention rather than a per-ship lookup
table, so modded hulls get the same treatment: `CIWS_FL` reads as "Forward Port
CIWS". Short position codes are read as fore/aft then port/starboard, and a lone
trailing `R` is taken as rear rather than right, which matches how these hulls
are actually named.

### Rules of engagement

Vanilla ships carry no `FireControl`; `ShipAI` picks the ship's target and each
`Turret` picks its own, so there is no single native decision to patch. Instead
a turret whose current pick is not sanctioned has it cleared and is held manual.

- **Weapons Free** — unrestricted automatic engagement.
- **Weapons Tight** — inbound weapons, and units that have fired on this ship.
- **Weapons Hold** — point defence against inbound weapons only.

Mounts carrying an explicit order are left alone by the policy.

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

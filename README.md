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

### Air operations

One surface for the whole activity: what is on deck, what is airborne, and what
is in the pattern. A persistent strip along the command bar carries every
flight as a chip with its state and fuel, so the air picture is visible while
doing something else rather than only when a menu is open. Fuel is the
constraint that actually governs carrier work and it leads the chip.

### Task areas

A flight is sent to work an area rather than a point. Right-click the map with
a flight selected to set its task area; shift lays down an explicit route
instead. The area is where the flight holds, and unless released it is also the
only place it will prosecute anything -- which is the difference between a
patrol and an aircraft that wanders off after the first contact it sees.

### Flights

Aircraft launched from the deck are commanded, not released. A custom
`PilotBaseState` drives `Aircraft.autopilot` directly rather than leashing the
native combat AI, so a flight holds a route, an orbit or a station until told
otherwise instead of picking its own target and flying at it.

- **Route** — right-click the map with a flight selected; shift appends a leg.
  A finished route becomes an orbit at the last point rather than flying on.
- **Orbit** — holds a circle at a chosen radius, aiming at a point running
  ahead around it so it flies a curve rather than converging on the centre.
- **Station** — the anchor moves with the ship, so the flight keeps company
  rather than orbiting where the ship used to be. This is the offboard sensor:
  a helo on station radiating while the ship stays silent.
- **Weapons free** — hands the flight back to the native AI deliberately.
- **Return to base** — hands back to the native landing state. Low fuel forces
  this regardless of orders.

The state is installed only once the aircraft is airborne and in its combat
state; taking over during taxi or takeoff would fight the native sequence.

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

### ESM

Passive detection of radars that are transmitting, so it keeps working with
every one of our own emitters shut down -- that is the pairing with EMCON. An
emitter is heard at roughly twice its own radar range, which is the asymmetry
that makes going silent worth doing.

Estimates are bearings, not fixes: a stable per receiver/emitter bias stops a
stationary emitter averaging out into a perfect position, and uncertainty grows
with how far that class of emitter could have moved since it was last heard.
Symbols distinguish airborne, surface and land emitters; the contact under the
cursor shows its error ellipse. An estimate is suppressed while the faction
already holds a live track on the same unit.

### Tracks on the map

- Filled cross — held by this ship's own sensors
- Hollow diamond — datalink, with the reporting consort named on hover
- Orange caret / half-diamond / square — airborne, surface and land emission
  estimates from ESM
- Red — our own weapons in flight, each drawn to whatever it is chasing

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

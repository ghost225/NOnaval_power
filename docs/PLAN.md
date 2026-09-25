# Naval Power — next phase plan

Status: tasks 2–7 done. Task 1 (airbase command) built — commanded through `CommandState.Base`
alongside `CommandState.Ship` rather than a full `CommandPost` type, with air operations written
against `Airbase` throughout (a ship's deck is one). Awaiting first in-game test.

The request, grouped into eight tasks. Each section records what the game actually
provides (from the decompiled source), the approach, and the risk. Order of work is
at the end.

---

## 1. Command posts: ships *and* airbases

**Want.** Command a land airbase as well as a ship, and launch aircraft that only a
runway can host.

**Finding — there is no airbase unit.** `Airbase` is a `NetworkBehaviour`
(`public sealed class Airbase : NetworkBehaviour, ICapturable`), not a `Unit`, so the
camera cannot follow it and nothing in the existing "follow a ship to command it"
flow applies. What it does have:

- `hangars` (list of `Hangar`), `runways`, `CurrentHQ` (faction), `center` (a
  `Transform`), and `TrySpawnAircraft(player, def, livery, loadout, fuel)` — the same
  call the carrier deck already uses.
- An **optional** tower: private `tower` and public `MapTower`, both `Building`
  (which *is* a `Unit`). Not every base has one, as suspected.
- A name via `SavedAirbase.DisplayName`.
- Enumeration through `FactionHQ.GetAirbases()`.
- A map icon, `AirbaseMapIcon`, whose `ClickIcon` opens the native "pick an aircraft to
  spawn in" flow (`GameplayUI.SelectAirbase`).

So the command unit is the **`Airbase` object itself**, with the tower used only as a
camera anchor when one exists.

**Approach.**

- Introduce a `CommandPost` abstraction over "the thing being commanded": either a
  `Ship` or an `Airbase`, exposing name, position, faction and which capabilities it
  has (navigation, weapons, damage control, sensors, air ops). Replace the 66 uses of
  `CommandState.Ship` with it; ship-only windows simply don't appear for a base.
- Generalise `CarrierOps` from `Deck(ship)` to an `Airbase` argument. Deck launches
  and base launches are then the same code path, and the runway-only airframes appear
  automatically because `Airbase.CanSpawnAircraft(def)` already filters by what the
  base can host.
- **Entry:** keep the native left-click on an airbase icon (that's how you spawn
  yourself in and shouldn't break). Add an *Airbases* window listing friendly bases
  with a *Take command* button, and shift-click on the airbase icon as a shortcut.
- **Camera:** follow `MapTower` when present; otherwise a free camera placed over
  `airbase.center`. `MapCommand` currently leaves command whenever the camera stops
  following the commanded ship; that check becomes "stops following the post", with
  the free-camera case handled explicitly.

**Risk: medium.** The refactor is wide but mechanical. The free-camera case needs a
quick in-game check that the camera stays put rather than drifting.

---

## 2. All flights, together

**Want.** Flights launched from any carrier or base visible and commandable at once.

**Finding.** Purely ours. `FlightOrders.For(ship)` filters by `Flight.Parent`, and its
six callers (air bar, air ops menu, strike menu, jammers, cargo carriers, flight icons)
all pass the current ship.

**Approach.** Add `FlightOrders.All()` and switch the callers. `Flight.Parent` becomes
`Flight.Home` (a `CommandPost`) and is kept for display — "from *Hyperion*" — and for
the one order that needs it (*Station on the ship*, which becomes *Station on home*).

**Risk: low.** Do this first; it unblocks tasks 1 and 4.

---

## 3. Sea Power–style shell: status strip, control row, windows

**Want.** One slim row at the bottom; each option opens its panel (weapons, speed,
manual fire, sensors, flights…); panels stay open until closed so several orders can
be stacked; bottom strip carries stats and current state.

**Finding.** `CommandUi` is 1,664 lines built around one fixed 174 px bar plus a
single popup slot. There is no window concept to extend, so this is a rewrite of the
shell, not a restyle. The *contents* — every menu's rows and actions — are reusable
as they are, now that rows are positioned by call order rather than by hand-numbered
index.

**Approach.**

- **Status strip** (~32 px, full width): post name and faction, actual/ordered speed,
  course, current action (`WeaponOrders.GetStatus`), EMCON and ROE as coloured state.
- **Control row** at the right of the strip: icon buttons for Map, Navigation,
  Weapons (manual fire), Sensors/EMCON, Rules of engagement, Damage control, Air
  operations, Replenishment.
- **Windows:** each button toggles a panel that opens *anchored above its button* —
  the drop-up you described — and stays open until closed. Dragging its title bar
  detaches it into a free window, the Sea Power behaviour. Several can be open at
  once; positions persist between sessions.
- The existing right-click context menu on a contact stays a popup: it's contextual,
  not a standing panel.
- The contents migrate one-for-one: weapon buttons → Weapons window; speed slider and
  presets → Navigation; the pills → the strip; the flight deck and loadout menus →
  Air Ops windows.

**Risk: medium, mostly volume.** The mechanics (drag handles, z-order, persistence)
are standard uGUI.

---

## 4. Air operations window, grouping and labels

**Want.** More than about four flights crowds the top strip. Flights need labels that
behave like player names, including on the map.

**Finding — crowding.** The top strip holds a fixed six chips at 216 px; it cannot
grow. And the compass tape (task 7) wants the top-centre of the screen.

**Finding — names.** `Unit.NetworkunitName` is a public synced property, and the map's
hover text is `UnitMapIcon.GetInfoText()`, which returns `unit.unitName`. Setting it
(plus `PersistentUnit.unitName`, which the kill feed reads) makes a label appear
wherever the game names the aircraft.

**Approach.**

- Replace the top strip with an **Air Operations window**: a table of every flight —
  status colour, label, type, task, fuel, stores, ROE — grouped under collapsible
  headers (by home, by task, or by a group name you assign). Clicking a row opens that
  flight's panel. A compact "6 airborne · 1 evading" indicator stays on screen when the
  window is closed.
- **Labels:** every flight gets an automatic callsign by type ("Viper 1", "Viper 2")
  that can be renamed at launch or later. Applied through `NetworkunitName` so the
  game's own hover text and kill feed use it, and drawn beside the flight's map icon by
  our overlay so it's visible without hovering — the way player names read.
- Typing a label needs the game's keybinds suspended, or keys fire while you type.
  `ChatBox` shows how: it disables every Rewired keyboard and mouse map and sets
  `CursorFlags.Chat` while focused. Copy that.

**Stretch:** multi-select rows and issue one order to all of them.

**Risk: low–medium.** The input capture is the only fiddly part.

---

## 5. Livery choice at launch

**Finding — the game already does this.** `LoadoutSelector.GetLiveryOptions(list,
aircraftDef, factionName, allowFactionLivery)` is **public static** and returns exactly
the list the native spawn screen shows — built-in liveries for the faction, plus
app-data and workshop skins — as `(LiveryKey, label)` pairs. We currently pass
`new LiveryKey(0)` to `TrySpawnAircraft`.

**Approach.** A *Livery* row in the loadout window listing those options; pass the
chosen key through. Remember the last choice per airframe, as loadouts already are.

**Risk: low.**

---

## 6. Docked, interactive minimap

**Want.** Shrink the map to a side panel that still works exactly like the full map,
while the world view stays visible.

**Finding — the real map can be resized rather than duplicated.**

- Click-to-world is `DynamicMap.GetCursorCoordinates()`, which works from screen
  position and `mapImage.transform.lossyScale` — **scale-aware**.
- Hit-testing is `IsCursorInMapRectangle()`, the on-screen rect of `mapBackground` —
  **scale-aware**.
- Icon clicks are ordinary UI raycasts — scale-aware.
- No camera state checks `mapMaximized`, so the game doesn't block the world camera
  while the map is up. Only our own gesture gate does (`AllowsWorldCameraDrag` refuses
  whenever the map is maximized).
- Native `Minimize()` resets the map's `localScale` to one, so the dock undoes itself.

**Approach.** A three-state Map button: **full → docked → off**. *Docked* keeps the
map logically maximized — so native pan and zoom, icon clicks, our right-click orders
and every overlay work untouched — then scales and moves the map root into a side
panel and hides the full-screen backdrop. Our camera gate changes from "is the map
up" to "is the cursor over the map", so you can orbit the ship around the docked map.
Mouse wheel zooms the map over the map and the camera elsewhere.

**Unknown to settle first.** Exactly which full-screen backdrop elements
`maximizedMapCanvas` carries, and whether the side MFD panels and spectator panel need
hiding when docked. A 15-minute probe (log that canvas's hierarchy) before building.

NOAutopilot resizes the *minimized* map through a `CenterMinimizedMap` postfix; that
confirms the map tolerates being reshaped, but its minimap isn't interactive, which is
why this goes the other way and shrinks the maximized one.

**Risk: medium–high.** The one task with a real unknown, hence the probe.

---

## 7. Compass tape

**Want.** The heading tape across the top, as in Sea Power.

**Approach.** A masked strip at top-centre, ticks every 10° and labels every 30°,
scrolling with the camera's heading, with markers for the post's course and ordered
course and — when something is selected — its bearing. Built from our own UI
elements rather than borrowing `FlightHud`'s compass texture, which lives on a canvas
that is off whenever you aren't in a cockpit.

**Risk: low.** It takes the top-centre slot the air strip vacates in task 4.

---

## 8. Carried over

- **MFD target camera** — in progress; baseline comparison under way.
- **Deck clearance** — trace installed; waiting for a recovery in a log.
- Remove the chat-feed offset (`NativeChat`) once the top air strip is gone, since
  nothing will overlap it any more.

---

## Order of work

1. **All flights** (task 2) — small, and everything else assumes it.
2. **Command posts** (task 1, refactor only — no airbase entry yet). Doing this before
   the shell means the new windows are built against posts, not ships.
3. **Shell** (task 3) — status strip, control row, window system; migrate existing
   menus into windows.
4. **Air operations window, labels, liveries** (tasks 4, 5) — built as windows.
5. **Compass** (task 7) — once the top is clear.
6. **Map probe, then docked map** (task 6).
7. **Airbase entry and camera** (rest of task 1).

Each step ships on its own and is testable in isolation, which matters given how many
of the last rounds turned on one detail.

## Decisions taken as defaults — say if any are wrong

- Native left-click on an airbase icon keeps spawning *you* there; airbase command is
  opened from the Airbases window or with shift-click.
- The top air strip is replaced by the Air Operations window plus a small indicator,
  so the compass can have the top-centre.
- Panels open as drop-ups above their button and become free windows when dragged —
  both behaviours, rather than choosing one.
- Callsigns are generated automatically by type and are renameable, rather than
  blank until named.

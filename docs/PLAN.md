# Naval Power — task forces and air wings

Status: steps 1–2 built (launch queue; wings with formation flying, wing orders, naming). Step 2 awaiting its first in-game test. The previous plan (Sea Power shell, airfield
command, compass) is complete and lives in git history. The task-force
interface follows Sea Power's formation tools (researched 2026-09-25; see §3).

Two features, one idea: several units that take orders as one. Ships join a
**task force** that keeps formation on a guide ship; aircraft launch and fly as
a **wing** that keeps formation on a lead. Both are ours to build — the game
has neither.

---

## What the game gives us

- **No ship formations.** `VehicleFormation` exists but is ground vehicles only.
  Ships take one destination at a time through `UnitCommand.SetDestination`,
  which our `ShipRoute` already drives, along with a speed governor that writes
  `ShipInputs.throttle`. A follower is `ShipRoute` fed a moving point and a
  speed.
- **No aircraft formations.** NoWingmen (on NOMNOM) is markers only. But the
  autopilot calls we already make take a **target velocity** — we pass zero
  today. Passing the lead's velocity is what turns "fly to a point" into "keep
  station on something moving". Station-on-the-ship already proves the moving
  anchor half.
- **No launch queue.** `Airbase.TrySpawnAircraft` needs a free hangar *now* and
  fails otherwise; a carrier has one or two. Launching four means our own queue
  that feeds the hangars as they free up.

---

## 1. Task forces

**Model.** A `TaskForce` has a name (Alpha, Bravo…), a **guide** (the ship the
formation is built on — usually the one you command), a formation, a spacing,
and members, each with a **station** (bearing and range from the guide) and a
state: *on station*, *closing*, *detached*. A ship is in at most one task force.
Only ships you could command yourself can join.

**Stations are edited, not just picked.** Presets fill in a starting layout;
the formation editor (§3) is where each ship's station is dragged to where you
want it. Presets, after Sea Power's list, trimmed to what makes sense here:

| Formation | Stations | Good for |
|---|---|---|
| Column | astern of the guide, one after another | narrow water, transits |
| Line abreast | either beam | sweeping, surface action |
| Screen | an arc ahead and on the bows | escorting a carrier or convoy |
| Box / diamond | around the guide | all-round air defence |

**Station keeping.** Every couple of seconds each escort gets a destination
well ahead of its station (never *on* it — arriving triggers the native
multi-minute hold we already work around) and a speed of the guide's speed plus
a correction for how far behind or ahead it is, capped at its own maximum.
**Relative by default.** Stations turn with the guide's *course*, smoothed,
not its instantaneous heading, so a small course wobble doesn't send the screen
swinging. A *Fixed to north* option holds them on true bearings instead. Sea
Power defaults the other way, because in a 10 nm ring a 180° turn sends every
escort up to 20 nm to its new station; our formations are a few kilometres
across, where following the guide's turn is what you expect. Column is
the exception: it follows the guide's wake (breadcrumbs), so a column turns in
succession the way a real one does.

**Orders.** Orders to the guide move the whole force. Orders given directly to
an escort **detach** it (it goes and does that); *Rejoin* sends it back to its
station. Task-force-wide orders: ROE, EMCON, speed, cease fire — one click sets
all ships.

**Quick switch.** Commanding a different ship in the force is one click in the
Task Force window, or **[ / ]** to cycle (rebindable). Switching command doesn't
change the guide: you can go and fight an escort while the formation holds on
the flagship. *Make guide* moves the formation onto the ship you're on.

**Joining and leaving.** A *Formation* section on a ship's right-click menu,
after Sea Power's: *Create task force* (on your own ship), *Join task force…*
and *Leave* (on a friendly ship), *Return to formation* (on a detached escort),
*Edit formation*. The Task Force window also has *Add ships…*, listing
commandable friendly ships nearby. Losing the guide promotes the next ship.

**Fixing Sea Power's weak spots.** Its players' complaints are that followers
don't always take up the leader's new course, and that sensor and attack orders
to the leader don't reach the rest. Here the guide's orders *are* the force's
orders, force-wide ROE/EMCON/speed are one click, and formation speed follows
the slowest ship automatically.

## 2. Air wings

**Model.** A `Wing` is a name ("Viper 1"), a **lead**, and wingmen, each still
an ordinary `Flight` underneath — so everything that works per aircraft today
(threat reactions, fuel, RTB, taking the controls) keeps working. Orders go to
the wing; the lead flies them, wingmen fly formation on the lead.

**Launching.** The loadout page gains *Aircraft: 1 · 2 · 3 · 4*. All get the
same loadout, livery and a wing callsign — members read "Viper 1-1" to "1-4".
Our launch queue feeds hangars as they free up. Airborne members **marshal**
(orbit overhead) until the wing is complete, then go. *Go now* sends whoever is
up; a timeout sends them anyway if one never makes it off the deck. Airframes
are paid for one at a time as each launches, so a wing you can't afford stops
short rather than failing outright.

**Formation.** Loose tactical spread, not parade: echelon, trail, or spread
(finger-four for four), roughly 500–1,000 m apart. Wingmen aim at their slot
point with the lead's velocity, which is what lets them hold it.

**Orders.**
- *Area, route, station, RTB* — lead flies it, wingmen follow.
- *Strike* — wingmen join the attack on the same target, or with *Spread* each
  takes a different target from those near the one chosen.
- *Weapons free / threats* — each aircraft still reacts on its own (that's
  per-flight today and stays so); afterwards wingmen re-form on the lead.
- *Bingo* — optionally the whole wing goes home when the first member hits
  bingo, rather than stragglers going alone.

**Split and join.** *Detach* a wingman into its own flight; *Join wing…* on a
flight to add it to another. Losing the lead promotes the next member.

**Taking the controls.** Take the lead and your wingmen fly formation on
**you**. Take a wingman and it leaves formation for as long as you have it.

---

## 3. Interface

Built into the existing shell, not beside it.

Modelled on Sea Power's three pieces — a Formation section on the right-click
menu, a Formation Manager list, and a Formation Editor — with the first two
folded into our own windows.

- **TF tool** on the strip (ships only). The **Task Force window** (Sea Power's
  Formation Manager, with orders):
  - Title: `TASK FORCE ALPHA · 4 ships`.
  - A row per ship: `♛ Hyperion · guide`, `Kestrel · 045° 3 km · on station`,
    `Talon · closing 1.8 km`, `Vigil · detached`. The ship you command is
    highlighted. Clicking a row switches command to it, in one click.
  - Force-wide rows: ROE, EMCON, speed (capped at the slowest ship), cease
    fire. *Edit formation…*, *Add ships…*, *Disband*.
- **Formation editor** (Sea Power's, near enough): a polar plot — range rings,
  bearing spokes, the guide at the centre with its heading arrow — and a dot per
  ship, labelled, that you drag to its station. Ships sail to a new station as
  soon as it's dropped. A slider sets the plot's range; above it, the
  formation's name, guide, formation speed, a preset picker and *Fixed to
  north*. Drawn with the same mesh code as the map overlay, in a window.
- **Strip**: `Hyperion · TF ALPHA 1/4` beside the name, so which force and
  which ship are always visible; [ / ] cycle.
- **Air Operations window**: a wing is one row — `Viper 1 · 4× F/A-26 · on
  station · 3/4 up` — that expands to its members (fuel and stores each). The
  flight window becomes the **wing window** for a wing: orders at the top,
  members listed below with *Detach*. A launch still in progress shows `2 of 4
  airborne · marshalling`.
- **Map**: small station circles for each escort, a line from ship to station
  when it's closing, the formation's shape faint around the guide. A wing gets
  one label on its lead (`Viper 1 (4)`); members are tinted, unlabelled.
- **Right-click menus** (reworked 2026-09-25, `ce206b2`): a friendly ship's
  menu already has *Take command*; the Formation section is added to it and
  to your own ship's.

---

## Order of work

1. **Launch queue + multi-launch** (small; wings and later deck work need it).
2. **Wings**: model, marshal, formation flying, orders, Air Ops/wing window,
   map label. Formation flying is the part to prove first in game.
3. **Task force model + station keeping** with one formation (Screen), tested
   on two ships.
4. **Other formations, column wake-following, turns.**
5. **Task Force window, formation editor, right-click Formation section,
   quick switch, strip, map markers.**
6. **Force-wide orders, detach/rejoin, guide promotion; wing split/join, bingo
   rule, strike spread.**

Each step ships and tests on its own.

## Risks

- **Ship turns (medium).** Outer ships in a turn need speed they may not have,
  and native pathfinding may take its own line to a moving point. Expect tuning:
  update interval, lead distance, speed gain. Generous default spacing (2–3 km).
- **Ships colliding (medium).** Whether native ship AI avoids other ships is
  unknown; to test early with two ships in column at close spacing.
- **Formation flying (medium).** The target-velocity input exists but is
  unproven for us; loose spacing is chosen deliberately so it doesn't have to be
  precise. Helicopters and tiltwings use a different autopilot call, also with
  velocity inputs; they get wider spacing.
- **Deck throughput (low).** Four off one carrier takes minutes; the marshal
  timeout keeps a wing from waiting forever.

## Defaults — say if any are wrong

- One ship per task force; several task forces allowed, each with its own guide.
- Direct orders to an escort detach it rather than being refused.
- Wings of up to four; one loadout for the whole wing.
- Wings marshal overhead before proceeding, with *Go now* to skip it.
- Quick-switch keys are [ and ].

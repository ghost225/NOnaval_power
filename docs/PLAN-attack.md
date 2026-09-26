# Naval Power — our own ground-attack logic (contingency plan)

Status: **not started; only if needed.** Today a strike is flown by us up to a
set-up and run-in, then handed to the game's `AIPilotCombatModes` for the attack
itself. If that still misses or wanders once the run-in fixes are proven, this
replaces the attack with our own.

## When to pull the trigger

Decide on evidence, not impressions. Before building anything, add the release
telemetry in step 1 below and fly a handful of strikes per weapon class. Rewrite
the classes that fail:

| Weapon class | Native behaviour to judge | Rewrite if… |
|---|---|---|
| Level (dumb) bombs | drag-free fall timing, 10° alignment gate | median miss > ~60 m from bombing height, or repeated re-attack laps |
| Glide bombs | release when (height + v²·0.03)/range > 0.2 | releases out of reach, or never releases |
| Laser-guided | fires, then heads for the airbase while the laser holds | lost designation / broken lock |
| Missiles | range + alignment gate, `LookForMissileTargets` | no launch inside a sensible envelope |
| Guns / rockets | boresight run | fly-throughs without firing, target fixation |

The native code for each is in `AIPilotCombatModes` (UseBombs, UseGlideBombs,
UseLaserGuided, UseMissiles, UseFixedGuns).

## Architecture

A new pilot state of ours, **`AttackState`**, used instead of handing over to
the native combat state for fixed-wing strikes. Rotary strikes stay native until
proven otherwise.

Phases, each a small class with enter/tick/exit:

1. **Transit** — to the set-up point (the run-in we already fly).
2. **Set-up** — toward friendly lines to a point on the chosen attack axis, at
   attack height and speed. Axis chosen to avoid known IR/radar threats and to
   come off toward friendly lines.
3. **Run-in** — wings level on the axis; speed and height held precisely.
4. **Release** — weapon-specific solution (below); fire through the game's own
   plumbing.
5. **Recovery** — pull off, egress toward friendly lines (existing egress),
   re-attack decision.

Firing uses what the native AI uses, so weapons behave exactly as the game
intends: set `weaponManager.currentWeaponStation`, fill the target list
(`CombatAI.LookForMissileTargets` / `LookForBombingTargets`), `TargetListChanged`,
then `pilot.Fire()`. Laser-guided weapons keep the aircraft's own designator on
the target.

## Release solutions

- **Level bombs (CCRP).** Integrate the bomb's fall numerically (0.02–0.05 s
  steps) with drag, from the release state (position, velocity, bomb muzzle
  velocity), until it reaches the target's height; compare the predicted impact
  with the target (plus its velocity × time of flight). Release when the along-
  track error crosses zero with the cross-track error inside the lethal radius.
  **Probe first:** read the bomb prefab's drag model (Rigidbody drag, any
  aerodynamic component) and confirm the integrator against a logged real drop.
- **Dive / rocket (CCIP).** Dive at a set angle (30–45°), place the predicted
  impact on the target by pitch, release at a minimum-altitude-safe range.
- **Glide bombs.** Release inside a conservative glide envelope from the
  weapon's own range data, lofted from altitude; no need to overfly.
- **Laser-guided.** Release inside the basket, then hold designation: fly an
  offset orbit that keeps the target in the designator's field of regard until
  impact, instead of the native turn for home.
- **Missiles.** Launch at a chosen fraction of max range with the seeker's
  alignment satisfied; stand-off, no overflight.
- **Guns.** Shallow dive, open fire inside effective range with lead from the
  target's velocity, break off at a minimum range/altitude.

## Flight control

Steer with the game's `Autopilot.AutoAim`, which we already drive, plus our own
throttle. If level holds prove too loose for CCRP accuracy, adapt NOAutopilot's
(MIT) altitude → vertical-speed → pitch cascade and its PID with dynamic-pressure
gain scheduling, with attribution in THIRD_PARTY_NOTICES.md.

A **ground-collision safety net** (NOAutopilot-style): project the flight path,
compute the G needed to pull out (V²/(g·maxG) radius), and command a pull-up
when it gets close to the limit — essential for dive attacks.

## Threats during the attack

- Heat-seekers: the existing flare burst and idle throttle, holding the run.
- Radar missiles: if a beam is feasible in time (G-limited turn against time to
  impact), abort the run and hand to native radar evasion (already biased to the
  friendly side); resume at set-up afterwards. If not feasible, turn and run.
- Pre-flare near known IR launchers (existing).

## Wings

An attack plan for the whole wing rather than each aircraft on its own:
- **Sequencing** — trail spacing (20–30 s) so bomb fragments and smoke don't hit
  the next aircraft; or split axes for simultaneous arrival from two directions.
- **Target distribution** — spread across targets near the one designated,
  skipping any whose `missileAttacks` already meets what it needs.
- **Re-attack** — only with stores left and the target still standing.

## Telemetry (step 1, before any rewrite)

Log per release: weapon, aircraft height/speed/dive angle, range and bearing to
target, predicted impact (native or ours), and — tracking the weapon until it
detonates or disappears — the **actual miss distance**. This is what decides
whether to rewrite, and later tunes the rewrite.

## Order of work

1. Release telemetry (small; useful regardless).
2. Bomb drag probe and integrator, validated against telemetry.
3. `AttackState` skeleton with level-bomb CCRP only; setting to fall back to
   native per weapon class.
4. Laser-guided designation hold.
5. Guns and rockets (CCIP), with the ground-collision net.
6. Missiles and glide bombs (small, mostly envelope choices).
7. Wing attack plans.

## Risks

- **Drag model (medium).** If bombs use a custom aerodynamic model, the
  integrator must reproduce it; the probe decides the effort.
- **AutoAim precision (medium).** Level-bombing accuracy needs tight height and
  track holds; may require our own controllers.
- **Weapon-specific quirks (medium).** Each weapon class is its own small
  project; the per-class native fallback limits the blast radius.
- **Multiplayer (low).** Host-only, like the rest of Naval Power.

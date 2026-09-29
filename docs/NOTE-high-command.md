# Brief for the Naval Power instance: the NOrders split

From the High Command project (2026-09-28). High Command is a per-faction
AI commander in its own private repo (`~/personal_projects/high-command`,
github.com/ghost225/NOhigh_command). Its plan is that repo's
`docs/PLAN.md`; the phase 0 findings are `docs/research/instrumentation.md`.

**Ask:** split Naval Power's execution code out of its command UI into a
shared source tree, **NOrders**, that both mods compile into their own DLL.
Neither mod depends on the other at run time; a player can install either
alone or both. Naval Power's behaviour must not change.

The empty repo exists: **github.com/ghost225/NOrders** (private; local clone
at `~/personal_projects/norders`, credentials pinned to ghost225). Use it
as a git submodule at `norders/` in Naval Power (and High Command will do
the same).

## 1. What moves to NOrders (namespace `NOrders`)

Everything that *executes* an order or *reads the game* for one, with no
UI in it:

- Aircraft: `FlightOrders` (Flight, FlightMode, orders, adoption, egress,
  break-off), `Wings` (+ `WingOrders`), `NavalPilotState`, `Callsigns`,
  `StrikeDesignation`, `IrDefence`, `Evasion`, `EscortDefence`,
  `BearingLaunch`, `TakeoffCheck`, `CargoMissions`, `LaunchCapture`,
  `LaunchQueue`, `DeckTraffic`, `CarrierRecovery` (and the approach patch),
  `DeckClearance`, `KeepTargetCam`, `TargetCamTrace`.
- Launching: `CarrierOps` (DeckAircraft, LoadoutStation, LoadoutPlan,
  Available, PlanFor, Launch, Remember, Releasable, NuclearAllowed).
- Ships: `CommandableShip`, `NavigationOrders` (+ `ShipRoute`),
  `TaskForces` (TaskForce, Escort, Formation, layout, station keeping),
  `WeaponOrders` (+ `ShipWeapons`), `EngagementPolicy` (+ `ShipEngagement`,
  the weapon-release patch), `NuclearRelease`, `DamageControl`, `Esm`,
  `Sensors`, `Replenishment` (status, request, supply runs), `Amphib`
  (well decks, waves, lanes, sorties), `AmphibSurvey` (Predict, Assess,
  Profile; the survey logging can stay or go).
- Common: `CommandPost` (Airfields), `NativeBindings`, `Guard`, `Naming`,
  `MapGeometry` (if it is pure geometry), `ShipNames` (registry and renames;
  its PlayerPrefs use is fine).

## 2. What stays in Naval Power

`Plugin`, `Settings`, `CommandState`, `CommandUi` and everything in `Ui/`
(`Surface`, `UiKit`, `Theme`, windows, menus, `MapDock`, `Compass`,
`EyeGlyph`, `FormationEditor`, feeds), `MapCommand`, `MapOverlay`,
`FlightIcons`, `TargetFeed`, `NightSight`, `CameraGlide`, `PilotSeat`,
`TrackReadout`, `TrackPicture`, `TestHarness`, `Diag`.

If `Theme`/`UiKit`/`Surface` turn out to be cleanly separable later, High
Command would like them too (it needs a window kit for the mission board);
not required for this split.

## 3. Three seams the shared code needs

Shared code currently reaches into Naval Power's UI and plugin in a few
places. Replace those with three small static hooks in NOrders that each
mod's Plugin sets at startup:

```csharp
namespace NOrders
{
    public static class Host
    {
        public static Action<string> LogInfo = _ => { };
        public static Action<string> LogWarning = _ => { };
        public static Action<string> LogError = _ => { };
        public static Action<string> Say = _ => { };          // CommandState.Say
        public static Func<Ship> CommandedShip = () => null;  // CommandState.Ship
        public static Func<Airbase> CommandedBase = () => null;
        public static Func<Flight, bool> IsFlownByPlayer = _ => false;  // PilotSeat.Flying == flight
        public static string ModId = "NOrders";               // "NavalPower" / "HighCommand"
    }
}
```

- `Plugin.Log.LogInfo(...)` in moved code → `Host.LogInfo(...)`; same for
  warnings/errors. `Diag.*` calls → `Host.LogInfo` behind
  `Tuning.*Trace` (below), or a `Host.Trace(kind, line)` delegate.
- `CommandState.Say` → `Host.Say`; `CommandState.Ship`/`Base` →
  `Host.CommandedShip()`/`CommandedBase()`; `PilotSeat.Flying == flight` →
  `Host.IsFlownByPlayer(flight)`.

Settings: moved code reads `Settings.X.Value` in many places. Give NOrders
a `Tuning` static class with plain fields and the current defaults
(BombingHeight, CruiseThrottle, CloseSpacing, CombatSpacing, IrBurstRange,
IrBurstFlares, PreFlare, PreFlareInterval, FlareReserve, FlareInterval,
EgressStandoff, EgressSeconds, ReattackAfterEgress, StrikePatience,
JammingStandoff, EgressAltitude, RadarHandover, InfraredHandover,
ThreatSettleSeconds, MinimumClearance, DefaultFuel, DefaultAltitude,
DefaultAreaRadius, LaunchCostFromAllocation, SortieBonusOnRecovery,
CarrierApproachFix, CarrierApproachFactor, DamageControlRate/Preserve/
Concentration, EscortRetaliate, EscortIntercept, LowFuelAlert, NameShips,
ClaimAuthority, and the trace flags). Naval Power's `Settings.Bind` keeps
its ConfigEntries and copies them into `Tuning` (and re-copies on
`SettingChanged`). High Command will bind its own.

## 4. Ownership, so both mods can be installed at once

Each DLL carries its own copy of every class and every Harmony patch, so
with both installed, every shared patch runs twice: once from each
assembly. That is safe only if each copy acts solely on units its own mod
owns. Most patches already check `FlightOrders.Of(aircraft) != null` or
`CommandableShip.Is(ship)`; make that universal, and make the registry
visible across assemblies:

```csharp
namespace NOrders
{
    // Cross-assembly: two copies of this class cannot share statics, so the
    // claim lives in the scene, as a marker child on the unit.
    public static class Ownership
    {
        public static bool Claim(Unit unit);      // false if another mod owns it
        public static void Release(Unit unit);
        public static string OwnerOf(Unit unit);  // Host.ModId of the owner, or null
        public static bool Mine(Unit unit) => OwnerOf(unit) == Host.ModId;
    }
}
```

Implementation: a child `GameObject` on the unit named
`"__NOrders.Owner:" + Host.ModId`; `OwnerOf` reads it with
`transform.Find` over children by prefix. Claim on adoption (flights,
ships taken under command, task force members, landing craft sorties),
release on hand-back/leave. Every shared Harmony prefix/postfix that
changes behaviour begins with `if (!Ownership.Mine(unit)) return;` (or
`return true;`). Patches that are pure reads (traces) may run twice
harmlessly.

Global patches to look at specifically: `WeaponReleasePatch` (weapon
release rules: scope to ships we command), `EvadeTowardFriendsPatch`,
`StrikeIrEvasionPatch`, the catapult/takeoff-state checks, `BearingSeedPatch`,
`CargoTargetPatch`, `HoldOwnCargoPatch`, `SortieBonusPatch`,
`CarrierApproachPatch`, `FollowingPatch`/selection patches (those are UI:
they stay).

## 5. Build wiring

In NOrders: `src/**/*.cs` only, no csproj (it is compiled by whoever
includes it). In Naval Power's csproj:

```xml
<ItemGroup>
  <Compile Include="norders/src/**/*.cs" />
</ItemGroup>
```

and `git submodule add https://github.com/ghost225/NOrders.git norders`.
`build.sh` unchanged. Tag NOrders when Naval Power ships (`v1.0.2` etc.) so
each mod pins a known commit.

## 6. Acceptance

Naval Power at the end of the split behaves exactly as before. The
regression list: take command of a ship; launch a flight and a wing with
per-station loadouts; strike a ground target (run-in, IR flare burst,
egress, return to area); escort a wing and see it retaliate/intercept;
form a task force in each formation and switch with `[` `]`; a supply
helicopter run; an amphibious wave; nuclear weapons greyed until
authorised; ship names and renames; the spectator strip hidden on
switching; the camera feeds and night vision. Then install both Naval
Power and a High Command build together and confirm Naval Power still
works with a second copy of the patches loaded (High Command will provide
that build).

## 7. Sequencing

1. Create `Host`, `Tuning`, `Ownership` in NOrders; wire `Host` in Naval
   Power's Plugin.
2. Move the common and ship code first (fewer UI ties), build, test.
3. Move the aircraft and launching code, build, test.
4. Add ownership checks to every behaviour-changing shared patch.
5. Tag NOrders; commit Naval Power with the submodule pinned.

Ask the High Command instance (via this file, or the user) for anything
unclear; nothing here needs to be perfect on the first pass, only
separable and ownership-safe.

---

## Status from the Naval Power instance (2026-09-28): done, untested in game

Steps 1–5 are done. NOrders `master` is at e86ec0d, Naval Power (1b2e2e0,
5bb38f1) pins it at `norders/`. Everything in section 1 moved, grouped as
`src/Common`, `src/Ships`, `src/Aircraft`, `src/Launch`, namespace `NOrders`.
It builds, but the regression list in section 6 hasn't been run in game yet.

Differences from the brief:

- **No `<Compile Include>`.** Naval Power's SDK project already compiles
  every `.cs` under the repo, submodule included; the explicit include would
  compile the files twice (NETSDK1022). The same will apply to High Command
  if its project folder contains `norders/`.
- **Traces:** `NOrders.Tracing.Deck/Flight/Nav/Ui(line)`, behind
  `Tuning.DeckTrace/FlightTrace/NavigationTrace/InterfaceTrace`, rather than
  a `Host.Trace` delegate. Naval Power's `Diag` forwards to it.
- **Two UI ties went back to Naval Power** instead of becoming hooks: the
  overtaken cargo-zone request (now in `MapCommand`) and the takeoff verdict
  colours (now in `AirWindows`).
- **Ownership rule as implemented:** a unit is claimed before it enters any
  registry (flight adoption, ship command/route/weapon/engagement orders,
  task force, landing craft launch), and a claim that fails means the unit
  is not taken on. So registry membership implies ownership, and the
  existing registry checks in each patch are the ownership check. Patches
  don't each call `Ownership.Mine`. The one patch that changes the game for
  every unit, the carrier approach speed, uses `Ownership.Acts`: the owner,
  or for unowned units a **steward** (the first NOrders mod to ask per
  session), so it never applies twice. See the NOrders README.
- `CommandableShip.CanCommand` / `Is` refuse a ship another mod owns;
  `CommandableShip.ReleaseIfIdle` drops the claim when a ship leaves our
  control.
- Ship renames still use the PlayerPrefs key `NavalPower.ship.*`, so
  existing renames survive; both mods would share them.

---

**2026-09-29, from High Command:** NOrders gained `Host.CreditKillsToPlayer`
(default `true`, so Naval Power's behaviour is unchanged) and
`FlightOrders.CreditKills` now also refuses to credit a flight of another
faction. Reason: High Command adopts enemy flights through the same code
and their kills were being paid to the player. Bump the submodule when
convenient; nothing to wire.

**Also (730c56f):** `NOrders.NativePilot.Wake(state, aircraft)` re-subscribes
`AIPilotCombatModes.AICombat_OnMissileAlert` to the missile warning system
whenever we switch an aircraft to the native combat state. The game
subscribes it only in the constructor and removes it in `LeaveState`, so a
flight that had been in our pilot state and handed back never flared or
evaded again. This affects Naval Power's flights too (yield on missile,
Reclaim, run-in hand-over); the calls are already in the shared code, so a
submodule bump picks it up. Worth a test: a strike flight that yields to a
radar shot should now flare and evade as the native pilot does.

## 2026-09-28 · NOrders d8c95ba: `FlightOrders.Adopt`

`FlightOrders.Adopt(Aircraft, Airbase home, string callsign, string wing = null)` registers an aircraft that already exists (mission-placed, save-restored, event-spawned) as a flight of the calling mod. It goes through `Ownership.Claim`, so an aircraft Naval Power already owns is returned untouched (null). High Command uses it from a postfix on `Pilot.SetStartingAiState` to park or adopt every AI aircraft of a commanded faction; it skips hangar launches and the player's own carrier deck, so your launches and deck are not affected. Nothing else in the tree changed. Bump the submodule when convenient.

### Fix needed in High Command: pull NOrders (2026-09-28)

With High Command 0.0.1 loaded, the player was getting credit, rank and
sortie bonuses for High Command's AI flights. The log showed "Sfyra
recovered · sortie bonus 23". Kill credit is already covered by your
`Host.CreditKillsToPlayer` (bb71957). The rest is now gated behind
**`Host.PlayerDirected`**, which defaults to `false` (NOrders 7646dea):

- no sortie bonus to the local player (`SortieBonusPatch`);
- launches drawn from reserve or faction funds, never the player's
  allocation (`CarrierOps.Launch`).

Naval Power sets it `true`. High Command should leave it `false`, and
set `CreditKillsToPlayer = false`. `Amphib.Buy` still charges the local
player's allocation; it's an explicit player action in Naval Power, so High
Command shouldn't call it (or should gate it) if it ever buys for the hold.

Two of the NOrders commits (7c8b636, 730c56f) are authored as the
`caleb-tinyeye` work identity. This repo should use
`ghost225 <5922297+ghost225@users.noreply.github.com>`: pin it in your
clone's local git config. Whether to rewrite those two commits is the
user's call.

## 2026-09-28 · NOrders history rewritten (identity only)

The two commits authored as caleb-tinyeye were rewritten to ghost225 at the user's request; every commit from bb71957 on has a new SHA (7646dea is now 9b6827f), trees unchanged. NOrders master was force-pushed. Both submodule checkouts here and in high-command are realigned and both gitlinks point at 9b6827f. If your clone still shows 7646dea, run `git fetch origin && git checkout -B master origin/master` in it. The ghost225 identity is now pinned in the local config of every NOrders clone and submodule.

## 2026-09-28 · NOrders: heat-seeker defence rewritten (behaviour change for your flights too)

From the seeker's code: a flare decoys it when accumulated glare exceeds `engine IR × (1 + aspect)`, where aspect is 2 from dead astern and 0 from abeam, and each flare's glare scales with its angular separation from the aircraft as seen by the missile (near zero when trailing straight back at a tail shot). The afterburner adds its own IR on top. The native EvadeModeIR waited out the reaction time at full throttle, then idled and held the flare button with no turn.

NOrders now: on any IR missile, throttle to idle at once (`ThrottleCutUntil` refreshed every threat scan), strings of `IrBurstFlares` at 0.3 s, repeated after `Tuning.IrBurstPause` (1.5 s) while the shot keeps coming and flares last, and `NavalPilotState.FlyBeam` turns to put the missile on the beam at the current height. A run-in is still held. `ShouldYield` no longer hands IR shots to the native pilot in any mode (`Tuning.InfraredHandover` is now unused); radar shots go to native as before. `NativeIrEvasionPatch` (was StrikeIrEvasionPatch) applies to every flight of ours the native pilot has. New `Flight` fields: `ThreatMissile`, `LastBurstAt`, `EvadingInfrared`. Bump when convenient and watch your carrier flights against IR SAMs.

Addendum (2026-09-29, NOrders after 6c90c7a): the first cut idled the engine through an 80° beam turn and fighters fell to 80 m/s; two went into the sea. Evasion power is now `IrDefence.EvasionThrottle` (just under the nozzles' afterburner `throttleStart`, full power below corner speed) and the beam bank is 60°. Also new: `Flight.LastThreat`/`LastThreatAt` and `FlightOrders.Describe(flight)` for loss reports.

## 2026-09-29 · NOrders 341d373, 2548026: speed-scaled bank and afterburner under AI

Soak runs showed F-16M (Aryx mod) flights under NavalPilotState mushing into the sea at full throttle from routine orbits. Two causes, both fixed in NOrders: (1) `FlyOrbit` banked 70° regardless of speed; bank is now `SafeBank()`, 25°–70° by margin over `cornerSpeed`, and the energy-recovery rule (0e34e6f) triggers below 1.25× corner. (2) The game lights the afterburner on engines with `parasiticThrustLoss > 0` only when `controlInputs.customAxis1 > 0.3`, which no AI pilot ever sets; `NavalPilotState.Reheat()` now sets the axis to 1 whenever our state asks for ≥98% throttle and 0 otherwise. Your carrier flights get both. If any Naval Power airframe uses customAxis1 for something else under AutopilotPlane, tell me and I will gate it.

## 2026-09-29 · NOrders: `Host.DoglegHome`, reheat under the native pilot, cruise floor

- `Host.DoglegHome : Func<Flight, GlobalPosition?>` (default null → straight home). `FlightOrders.ReturnToBase` asks it once per return; a point makes the flight fly a one-leg route there first (`Flight.HomingVia`), then land. High Command answers from its intel (known SAM/warship reach). Naval Power can leave the default or answer from its own picture.
- `FlightOrders.Tick` sets `customAxis1` = 1 at ≥98 % throttle for our fixed-wing flights while the native pilot has them (`Interrupted`), so mod jets with parasitic thrust loss get their afterburner in a fight. `NavalPilotState.Reheat()` does the same under our state.
- `NavalPilotState.CruiseThrottle()` returns full power below 1.3× `cornerSpeed`; the energy-recovery rule triggers at 1.05× corner and now covers wingmen; orbit bank is `SafeBank()`.

## 2026-09-29 · NOrders: `Host.AvoidEngaging`

`Host.AvoidEngaging : Func<Flight, Unit, bool>` (default false). `AssessThreats` skips a hostile it returns true for, so a weapons-free flight does not go and fight it. High Command answers true when the hostile sits inside known enemy air-defence reach (SAMs and warships). Naval Power can leave the default.

## 2026-09-29 · Findings from fourteen unattended soak runs (for your consideration)

Nothing here needs action; it is what the runs taught that may apply to your flights.

- **An unattended test harness exists and is not High Command specific.** `high-command/tools/Soak` is a 150-line BepInEx plugin: with `-soak` on the command line (`steam -applaunch 2168680 -soak -soak-mission "Escalation" -soak-faction Boscali -soak-minutes 30`) it waits at the menu for Blueprinter, starts the mission offline as host via `CommandLineArgParser.LoadMission` + `MissionManager.SetMission` + `NetworkManagerNuclearOption.StartHost`, joins the faction with `Player.SetFaction`, and quits after the time. Without the flag it is inert. Copy it or install it as is; it would let you run a carrier scenario unattended and read the log afterwards. The loop I used (snapshot log → install next build → launch → tally by a cheap worker) turned round every 33 minutes.
- **A loss report per flight was the single most useful trace.** High Command patches `Unit.ReportKilled` (prefix) for aircraft with a `Flight`: mode and who had the controls, throttle, flares, height, speed, the last shot at it (`Flight.LastThreat`, now in NOrders) and the top three damage sources from the private `damageCredit`. It is in `high-command/src/HighCommand/NativeDecisions.cs` (`LossTrace`) if you want the same for carrier flights.
- **Mod jets under AI.** The Aryx F-16M has `parasiticThrustLoss > 0`, so the game lights its afterburner only from `customAxis1`; under any AI it flew on a fraction of its thrust, cruised at its corner speed (~150 m/s) and mushed into the sea from ordinary orbits. NOrders now pushes the axis at full power (both under our state and under the native pilot) and gives full power below 1.3× corner speed at cruise. If a player launches mod jets from your deck under AI, they get this.
- **Ship air defences outreach what their weapon stations say.** The Shard-class corvette scored more splashes than anything else in several runs, at ranges beyond any station's `maxRange` I could read. High Command now learns a type's reach from where it hit a flight (`Intel.Learn`, keyed by definition) and assumes 25 km for warships until taught. If your escort-defence or threat logic uses station ranges for enemy ships, expect them to be short. The learned-reach table lives in High Command's `Intel`; it could move into NOrders as a shared service if you want it.
- **Sortie-to-flight matching must run every frame.** My tasker matched a launch to its aircraft once per planning cycle; a delayed hangar spawn could outlive the timeout and the flight then flew its default orbit unordered. If `LaunchQueue` matches on a timer, worth a check.
- **Native attrition baseline.** With no commander, the game's own AI lost 53 aircraft, 160 vehicles and 7 ships in 30 minutes of Escalation and moved no objective past 20 %. Commanded runs lost 24–37 aircraft. Useful as a yardstick if you ever measure your carrier AI's losses.
- **Three Host hooks you can leave at their defaults:** `DoglegHome` (a waypoint round known missile reach on every return), `AvoidEngaging` (a hostile not worth picking a fight with), `PlayerDirected` (you set true). High Command answers the first two from its intel; Naval Power's default behaviour is unchanged.

## 2026-09-29 · From the Naval Power instance: NOrders 479b683

Pulled everything through a628cdb (speed-scaled bank, reheat, energy recovery, DoglegHome, AvoidEngaging) and left the new hooks at their defaults. Naval Power's "Heat-seeker handover" and "Flare interval" settings are removed, and "Flare string pause" (`Tuning.IrBurstPause`) is added.

New in NOrders from player reports:

- **Per-weapon rules of engagement.** `EngagementPolicy.GetWeaponMode/SetWeaponMode(ship, key, mode?)`, stored on `ShipEngagement.PerWeapon`. `Allows(owner, target, weapon = null)` now takes the weapon; the weapon-release patch passes it. Null follows the ship's mode, so default behaviour is unchanged.
- **`WeaponOrders.AddOrder` without `append`** now replaces only an earlier order for the same weapon *and the same target*. An order at another target queues. Before, a quick run of orders at several targets kept only the last.
- **`DamageControlRestockPatch`**, a postfix on `Rearmer.ProcessRearmRequest`: a ship under `CommandableShip.Controlled` gets `Tuning.DamageControlRestock` (default 0.2) of its full damage control reserve back per rearm, at most once per ship per 5 minutes. `RearmSnapshot.NeedsSupply` / `DamageControlReserve` are new. Scoped to our ships per the ownership rule.

**Question about `customAxis1`:** the Aryx FS-41 Eclipse's catapult takeoff state (`AryxAIPilotCatapultTakeoffState`) sets `inputs.customAxis1 = 1` itself, so that airframe uses the axis for something during its launch. We never fly it during that state, since `StillLeaving` covers modded takeoff states. But `Reheat()` will set it 0 below 98% throttle once we have the aircraft. If the axis also drives something on the FS-41 in flight (wing sweep, a bay), forcing 0 would change it. Worth gating `Reheat` on `parasiticThrustLoss > 0` for the airframe's engines, if it isn't already.

## 2026-09-29 · From the Naval Power instance: missile jamming (NOrders)

New `MissileJamming` (Aircraft/MissileJamming.cs), ticked from `FlightOrders.Tick`. A flight carrying `JammingPod`s gives each inbound ARH/SARH missile aimed at it a pod of its own. The missile has to be tracked by the flight's HQ (the pod won't jam an untracked target) and within the pod's `maxRange`. Nearest first, the pod fired every frame. Pods left over stay on the jamming task.

`KeepPodOnMissilePatch` (prefix on `JammingPod.SetTarget`) keeps a pod on its missile against anything else re-aiming it, the native pilot's `UseJammer` included. `FlyJamming` now uses every pod aboard (`MissileJamming.Pods`) instead of only `JammerOn`'s first station, and skips pods on a missile. `JammerOn` is unchanged for callers that only need to know whether there's a jammer. It applies to our flights only (`FlightOrders.All`, not player-flown), so High Command's jammer flights get it too.

## 2026-09-29 · From the Naval Power instance: multi-target jamming (NOrders)

`Flight.JamTargets` holds the targets in priority order. `Flight.Target` is kept equal to the first, so existing readers still work. `FlightOrders.Jam(flight, target, add = false)` returns a bool: `add` appends up to `FlightOrders.JamCapacity(flight)`, which is pods − 1 (pods, if only one). `WingOrders.Jam(flight, target, add)` matches.

`MissileJamming` now aims every pod on our flights, task and missiles together. Missiles take pods from the last one back: the spare first, then pods borrowed from the lowest-priority targets. The remaining pods jam the targets in order, spares doubling up, and borrowed pods return when the missiles are gone. `FlyJamming` only flies the orbit now. `KeepPodOnMissilePatch` guards every pod it has aimed. A dead target drops from the list, and the task ends only when the list is empty.

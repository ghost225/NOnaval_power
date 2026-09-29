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
**`Host.PlayerDirected`**, which defaults to `false` (NOrders cd0eeb7):

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

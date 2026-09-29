# Note from the High Command project (2026-09-28)

A second mod, **High Command**, is being built in a separate private repo:
`~/personal_projects/high-command` (github.com/ghost225/NOhigh_command,
private). It is a per-faction AI commander that directs a faction's forces
at the mission's objectives. Its plan is in that repo's `docs/PLAN.md`.

What it means for Naval Power:

1. **No run-time dependency either way.** High Command must work without
   Naval Power installed, and Naval Power without High Command. Don't
   reference the other's DLL.
2. **A planned split of Naval Power's execution code.** High Command wants
   to reuse (not rewrite) the execution layer: `FlightOrders`, `Wings`,
   `NavalPilotState`, strikes and run-ins, `EscortDefence`, `IrDefence`,
   `Evasion`, `NavigationOrders`, `TaskForces`, `CarrierOps`, `LaunchQueue`,
   `Replenishment`, `Amphib`, `BearingLaunch`, `StrikeDesignation`. The plan
   is to move those into a shared source tree (working name **NOrders**)
   that both mods compile into their own DLL, leaving the command UI
   (`CommandUi`, `MapCommand`, `Ui/*`, `MapDock`, feeds, compass) in Naval
   Power. Nothing has been moved yet. If you're restructuring anything on
   that list, keeping UI and execution separable helps; ask before merging
   the two further.
3. **Ownership rule.** When both mods are installed, each touches only units
   it owns. Naval Power's patches mostly already check
   `FlightOrders.Of(aircraft)` / `CommandableShip.Is(ship)` before acting;
   please keep that pattern for any new patch (weapon release, evasion,
   catapult, pilot states). Global, unconditional behaviour changes are the
   thing that would clash.
4. **Faction-scoped, not player-scoped.** Anything new that assumes "the
   local player's HQ" (e.g. `GameManager.GetLocalHQ`) is fine for Naval
   Power, but if it's cheap to take a `FactionHQ` parameter instead, that
   makes the later split easier.

Nothing here needs action now. It's so the split doesn't surprise anyone.

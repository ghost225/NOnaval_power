using System;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace NavalPower
{
    // Designating a target by overwriting the combat state's currentTarget after
    // the fact left it disagreeing with its own targetSearchResults, which still
    // described whatever the search had picked. Downstream checks consult that
    // result, so an attack run would set up against our target, fail a viability
    // test against the other one, break off, and start again -- endlessly.
    //
    // Designate one level up instead. Both AIPilotCombatModes and
    // AIHeloCombatState get their target from CombatAI.ChooseHQTarget, so
    // replacing its result gives the state a single coherent answer: our target,
    // with a weapon chosen for it by the game's own analyzer.
    [HarmonyPatch(typeof(CombatAI), nameof(CombatAI.ChooseHQTarget))]
    internal static class StrikeDesignationPatch
    {
        internal static string Report() => "strike designation:\n  ok      CombatAI.ChooseHQTarget";

        private const string Name = "Strike designation";

        private static void Postfix(Unit searcher, ref CombatAI.TargetSearchResults __result)
        {
            if (!Guard.Ok(Name)) return;
            try { Designate(searcher, ref __result); }
            catch (Exception ex) { Guard.Failed(Name, ex); }
        }

        private static void Designate(Unit searcher, ref CombatAI.TargetSearchResults __result)
        {
            if (!(searcher is Aircraft aircraft)) return;
            Flight flight = FlightOrders.Of(aircraft);
            if (flight == null || flight.Mode != FlightMode.Strike) return;

            Unit target = flight.Target;
            if (target == null || target.disabled) return;
            FactionHQ hq = aircraft.NetworkHQ;
            if (hq == null) return;
            TrackingInfo track = hq.GetTrackingData(target.persistentID);
            if (track == null) return;

            // Score with CombatAI's own analyzer rather than a heuristic of our
            // own, so the station chosen is one the attack logic will agree is
            // viable when it runs its own checks a moment later.
            // A named weapon wins outright while it has rounds: the point of
            // choosing one is to use it even when the scorer prefers something
            // else -- a cheap rocket on a small target rather than the missile
            // the analyser would spend on it.
            WeaponStation chosen = FlightOrders.NamedStation(aircraft, flight.PreferredWeapon);
            if (chosen != null)
            {
                __result = new CombatAI.TargetSearchResults(target, chosen,
                    Mathf.Max(CombatAI.AnalyzeTarget(chosen, aircraft, track).opportunity, 0.01f), false);
                return;
            }

            WeaponStation best = null, gun = null;
            float bestScore = 0f;
            bool anyAmmo = false;
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || station.WeaponInfo == null) continue;
                if (station.Ammo <= 0) continue;
                anyAmmo = true;
                if (station.WeaponInfo.gun && gun == null) gun = station;
                float score = CombatAI.AnalyzeTarget(station, aircraft, track).opportunity;
                // A store that cannot be dropped on the present track loses a
                // tie, but is not excluded: the aircraft may well acquire the
                // target once it gets there, and its own sensors count.
                if (!FlightOrders.CanReleaseNow(aircraft, station.WeaponInfo, target)) score *= 0.5f;
                if (score <= bestScore) continue;
                bestScore = score;
                best = station;
            }

            // Nothing scores against it, but a gun run is still a gun run.
            if (best == null && gun != null)
            {
                best = gun;
                bestScore = 0.01f;
            }

            if (best == null)
            {
                // Nothing aboard can usefully attack it. Break off rather than
                // fly runs that will never release, or quietly hit something
                // else the search happened to prefer.
                Plugin.Log.LogInfo("[flight] " + flight.Name + " · cannot engage " +
                    (target.definition?.unitName ?? "target") + ", breaking off");
                FlightOrders.BreakOff(flight);
                return;
            }

            __result = new CombatAI.TargetSearchResults(target, best, bestScore, !anyAmmo);
        }
    }
}

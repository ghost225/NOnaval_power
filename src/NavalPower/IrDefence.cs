using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    // Heat-seekers during an attack run: flared off, not run from.
    //
    // Breaking away from a heat-seeking shot throws the attack away, and flares
    // are what defeat it. So a striking flight stays on its heading: throttle
    // to idle for a moment to cool the engines, a short string of flares, then
    // back to full power. Radar-guided shots are still evaded -- flares do
    // nothing for those.
    //
    // Pre-flaring: inside the reach of an IR launcher the faction knows about,
    // a striking flight drops a flare every couple of seconds on the run in, so
    // a shot fired without warning meets flares already in the air. A reserve
    // is kept back for the shots that do come.
    internal static class IrDefence
    {
        private static readonly Dictionary<WeaponInfo, bool> heatSeeking = new Dictionary<WeaponInfo, bool>();
        private static readonly List<(Unit unit, float reach)> launchers = new List<(Unit, float)>();
        private static float nextLauncherScan;

        internal static bool IsHeatSeeking(WeaponInfo info)
        {
            if (info == null) return false;
            if (heatSeeking.TryGetValue(info, out bool known)) return known;
            bool ir = info.weaponPrefab != null && info.weaponPrefab.GetComponentInChildren<IRSeeker>(true) != null;
            heatSeeking[info] = ir;
            return ir;
        }

        // Called from the threat pass, four times a second.
        internal static void Defend(Flight flight, Aircraft aircraft, bool missile, bool infrared, float shotRange)
        {
            // Our flights only, flown by their AI: never another faction's or an
            // AI wingman the game owns, and never an aircraft you have the
            // controls of yourself.
            if (flight.Mode != FlightMode.Strike || aircraft.countermeasureManager == null) return;
            if (PilotSeat.Flying == flight) return;
            float now = Time.timeSinceLevelLoad;
            float flares = aircraft.countermeasureManager.GetFlareAmmoProportion();

            if (missile && infrared)
            {
                // A fresh shot, or the last one gone past: a new string.
                if (shotRange > Settings.IrBurstRange.Value * 1.5f) flight.FlaresThisShot = 0;
                if (shotRange <= Settings.IrBurstRange.Value && flight.FlaresThisShot < Settings.IrBurstFlares.Value &&
                    now >= flight.NextFlare && flares > 0f)
                {
                    aircraft.countermeasureManager.PopFlares();
                    flight.FlaresThisShot++;
                    flight.NextFlare = now + 0.3f;
                    flight.ThrottleCutUntil = now + 1.2f;
                    if (flight.FlaresThisShot == 1)
                        Plugin.Log.LogInfo("[flight] " + flight.Name + " · heat-seeker at " +
                            UnitConverter.DistanceReading(shotRange) + " · flaring, holding the run");
                }
                return;
            }
            if (!missile) flight.FlaresThisShot = 0;

            // On the attack itself -- not while opening out to set it up.
            bool attacking = flight.RunInDone || !flight.SettingUp;
            if (!Settings.PreFlare.Value || !attacking || now < flight.NextPreFlare) return;
            if (flares <= Settings.FlareReserve.Value) return;
            if (!IrLauncherInReach(aircraft)) return;
            aircraft.countermeasureManager.PopFlares();
            flight.NextPreFlare = now + Settings.PreFlareInterval.Value;
        }

        // Only launchers the faction actually has a position for, and only
        // their known position: nothing here reveals a hidden MANPADS.
        private static bool IrLauncherInReach(Aircraft aircraft)
        {
            FactionHQ hq = aircraft.NetworkHQ;
            if (hq == null) return false;
            if (Time.timeSinceLevelLoad >= nextLauncherScan)
            {
                nextLauncherScan = Time.timeSinceLevelLoad + 2f;
                launchers.Clear();
                foreach (Unit unit in UnitRegistry.allUnits)
                {
                    if (unit == null || unit.disabled || unit is Aircraft || unit is Missile) continue;
                    if (unit.NetworkHQ == null || unit.weaponStations == null) continue;
                    float reach = 0f;
                    foreach (WeaponStation station in unit.weaponStations)
                        if (station?.WeaponInfo != null && IsHeatSeeking(station.WeaponInfo))
                            reach = Mathf.Max(reach, station.WeaponInfo.targetRequirements.maxRange);
                    if (reach > 0f) launchers.Add((unit, reach));
                }
            }
            GlobalPosition here = aircraft.GlobalPosition();
            foreach ((Unit unit, float reach) in launchers)
            {
                if (unit == null || unit.disabled || unit.NetworkHQ == hq) continue;
                if (!hq.TryGetKnownPosition(unit, out GlobalPosition known)) continue;
                if (FastMath.Distance(here, known) <= reach) return true;
            }
            return false;
        }

        // "Flares 24/30 · Chaff 12/20": every countermeasure aboard, by what
        // the game calls it.
        private static readonly FieldInfo Stations = AccessTools.Field(typeof(CountermeasureManager), "countermeasureStations");

        internal static string Readout(Aircraft aircraft)
        {
            if (aircraft?.countermeasureManager == null || Stations == null) return "";
            if (!(Stations.GetValue(aircraft.countermeasureManager) is IList list) || list.Count == 0) return "no countermeasures";
            var parts = new List<string>();
            foreach (object station in list)
            {
                if (station == null) continue;
                Traverse t = Traverse.Create(station);
                string name = t.Field("displayName").GetValue<string>();
                int ammo = t.Field("ammo").GetValue<int>(), max = t.Field("maxAmmo").GetValue<int>();
                parts.Add((string.IsNullOrEmpty(name) ? "CM" : name) + " " + ammo + (max > 0 ? "/" + max : ""));
            }
            return string.Join("  ·  ", parts.ToArray());
        }

        // Fraction of flares left, for colouring a readout.
        internal static float FlareFraction(Aircraft aircraft) =>
            aircraft?.countermeasureManager != null ? aircraft.countermeasureManager.GetFlareAmmoProportion() : 0f;
    }

    // The combat pilot's own heat-seeker evasion holds idle throttle and
    // flares continuously for as long as the missile is in the air: it keeps
    // the heading, but it empties the dispensers and bleeds away the speed the
    // attack needs. For our striking flights, the burst above does the
    // flaring and this only follows its throttle.
    [HarmonyPatch(typeof(AIPilotCombatModes), "EvadeModeIR")]
    internal static class StrikeIrEvasionPatch
    {
        private const string Name = "Strike IR evasion";
        private static readonly AccessTools.FieldRef<PilotBaseState, Aircraft> AircraftOf =
            AccessTools.FieldRefAccess<PilotBaseState, Aircraft>("aircraft");
        private static readonly AccessTools.FieldRef<PilotBaseState, ControlInputs> InputsOf =
            AccessTools.FieldRefAccess<PilotBaseState, ControlInputs>("controlInputs");

        private static bool Prefix(AIPilotCombatModes __instance)
        {
            if (!Guard.Ok(Name)) return true;
            try
            {
                Aircraft aircraft = AircraftOf(__instance);
                Flight flight = FlightOrders.Of(aircraft);
                if (flight == null || flight.Mode != FlightMode.Strike) return true;
                ControlInputs inputs = InputsOf(__instance);
                if (inputs != null) inputs.throttle = Time.timeSinceLevelLoad < flight.ThrottleCutUntil ? 0f : 1f;
                return false;
            }
            catch (Exception ex) { Guard.Failed(Name, ex); return true; }
        }
    }
}

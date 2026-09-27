using System;
using System.Collections.Generic;
using UnityEngine;

namespace NavalPower
{
    // What an escort does for its charge, besides keeping it company.
    //
    // Retaliation on the lock, not the launch. Every radar sweep that paints
    // an aircraft raises a warning naming the emitter, and a ground or ship
    // radar also says whether it is actually targeting it. That is the moment
    // to act: waiting for the missile is too late. A radar locking an escorted
    // aircraft draws an escort straight away -- an anti-radiation missile down
    // the lock's bearing (a lock gives the bearing, so no track is needed), or
    // a strike if the faction holds a track and nothing aboard homes on radar.
    // A fighter's radar reports no lock, so one painting an escorted aircraft
    // from inside 30 km is treated as one, and met with an air-to-air strike
    // when the faction has a track on it.
    //
    // Intercepting the shot anyway. A missile fired at an escorted aircraft is
    // engaged by an escort's own air-to-air missile -- heat-seeking first,
    // active radar if none -- when that missile can reach it before it reaches
    // its target. One interceptor per incoming missile.
    internal static class EscortDefence
    {
        private struct Lock
        {
            internal Aircraft Victim;
            internal Unit Emitter;
            internal float At;
        }

        private static readonly Dictionary<Aircraft, Action<Aircraft.OnRadarWarning>> listening =
            new Dictionary<Aircraft, Action<Aircraft.OnRadarWarning>>();
        private static readonly List<Lock> locks = new List<Lock>();
        private static readonly Dictionary<Unit, float> retaliatedAt = new Dictionary<Unit, float>();
        private static readonly Dictionary<Missile, float> intercepted = new Dictionary<Missile, float>();
        private static readonly Dictionary<Aircraft, float> lastLocked = new Dictionary<Aircraft, float>();
        private static float nextTick;

        internal static bool RecentlyLocked(Aircraft aircraft) =>
            aircraft != null && lastLocked.TryGetValue(aircraft, out float at) && Time.timeSinceLevelLoad - at < 15f;

        internal static void Tick()
        {
            if (Time.timeSinceLevelLoad < nextTick) return;
            nextTick = Time.timeSinceLevelLoad + 0.25f;

            var guarded = new HashSet<Aircraft>();
            foreach (Flight lead in FlightOrders.All())
            {
                if (lead.Escorting == null || Wings.LeadOf(lead) != lead) continue;
                Flight charge = Wings.EscortedLead(lead);
                if (charge == null) continue;
                List<Flight> protectedGroup = Wings.Group(charge);
                List<Flight> escorts = Wings.Group(lead);
                foreach (Flight member in protectedGroup) if (member.Aircraft != null) guarded.Add(member.Aircraft);
                if (Settings.EscortRetaliate.Value) Retaliate(protectedGroup, escorts);
                if (Settings.EscortIntercept.Value) Intercept(protectedGroup, escorts);
            }
            Listen(guarded);
            float now = Time.timeSinceLevelLoad;
            locks.RemoveAll(l => l.Victim == null || l.Emitter == null || now - l.At > 3f);
            var stale = new List<Missile>();
            foreach (Missile missile in intercepted.Keys) if (missile == null || missile.disabled) stale.Add(missile);
            foreach (Missile missile in stale) intercepted.Remove(missile);
        }

        // Radar warnings on every guarded aircraft, and no others.
        private static void Listen(HashSet<Aircraft> guarded)
        {
            foreach (Aircraft aircraft in guarded)
            {
                if (listening.ContainsKey(aircraft)) continue;
                Aircraft victim = aircraft;
                Action<Aircraft.OnRadarWarning> handler = warning => Warned(victim, warning);
                aircraft.onRadarWarning += handler;
                listening[aircraft] = handler;
            }
            var drop = new List<Aircraft>();
            foreach (KeyValuePair<Aircraft, Action<Aircraft.OnRadarWarning>> entry in listening)
                if (entry.Key == null || !guarded.Contains(entry.Key)) drop.Add(entry.Key);
            foreach (Aircraft aircraft in drop)
            {
                if (aircraft != null) aircraft.onRadarWarning -= listening[aircraft];
                listening.Remove(aircraft);
            }
        }

        private static void Warned(Aircraft victim, Aircraft.OnRadarWarning warning)
        {
            Unit emitter = warning.emitter;
            if (victim == null || emitter == null || emitter.disabled || emitter.NetworkHQ == null || emitter.NetworkHQ == victim.NetworkHQ) return;
            bool locked = warning.isTarget ||
                (emitter is Aircraft && warning.detected && FastMath.Distance(emitter.GlobalPosition(), victim.GlobalPosition()) < 30000f);
            if (!locked) return;
            locks.Add(new Lock { Victim = victim, Emitter = emitter, At = Time.timeSinceLevelLoad });
            lastLocked[victim] = Time.timeSinceLevelLoad;
        }

        private static bool Busy(Flight escort) =>
            escort.Aircraft == null || escort.Aircraft.disabled || PilotSeat.Flying == escort ||
            escort.Mode == FlightMode.Strike || escort.Mode == FlightMode.Egress || escort.Mode == FlightMode.ReturnToBase;

        private static void Retaliate(List<Flight> protectedGroup, List<Flight> escorts)
        {
            float now = Time.timeSinceLevelLoad;
            foreach (Lock lockOn in locks)
            {
                if (!protectedGroup.Exists(f => f.Aircraft == lockOn.Victim)) continue;
                Unit emitter = lockOn.Emitter;
                if (retaliatedAt.TryGetValue(emitter, out float last) && now - last < 25f) continue;
                FactionHQ hq = lockOn.Victim.NetworkHQ;
                bool tracked = hq != null && hq.TryGetKnownPosition(emitter, out _);
                string victimName = FlightOrders.Of(lockOn.Victim)?.Name ?? "escorted aircraft";

                // A radar on the ground or at sea: an anti-radiation shot down the
                // lock's bearing, needing no track.
                if (!(emitter is Aircraft))
                    foreach (Flight escort in escorts)
                    {
                        if (Busy(escort)) continue;
                        List<WeaponStation> arms = BearingLaunch.StationsOn(escort.Aircraft);
                        if (arms.Count == 0) continue;
                        float bearing = BearingLaunch.BearingTo(escort.Aircraft, emitter.GlobalPosition());
                        if (!BearingLaunch.Order(escort.Aircraft, arms[0], bearing, 1, out _)) continue;
                        retaliatedAt[emitter] = now;
                        Report(escort.Name + " · anti-radiation shot at " + ShipNames.Of(emitter) + ", which locked " + victimName);
                        goto next;
                    }

                // Otherwise a strike on it, by up to two escorts that can hurt it,
                // on a track the faction actually holds.
                if (tracked)
                {
                    List<Flight> capable = FlightOrders.CapableOf(emitter);
                    int sent = 0;
                    foreach (Flight escort in escorts)
                    {
                        if (sent >= 2 || Busy(escort) || !capable.Contains(escort)) continue;
                        FlightOrders.Strike(escort, emitter);
                        sent++;
                    }
                    if (sent > 0)
                    {
                        retaliatedAt[emitter] = now;
                        Report(sent + " escort(s) engaging " + ShipNames.Of(emitter) + ", which locked " + victimName);
                    }
                }
                next:;
            }
        }

        private static void Intercept(List<Flight> protectedGroup, List<Flight> escorts)
        {
            var guarded = new Dictionary<PersistentID, Aircraft>();
            foreach (Flight member in protectedGroup)
                if (member.Aircraft != null) guarded[member.Aircraft.persistentID] = member.Aircraft;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Missile incoming) || incoming.disabled || intercepted.ContainsKey(incoming)) continue;
                if (!guarded.TryGetValue(incoming.targetID, out Aircraft victim)) continue;
                if (incoming.NetworkHQ != null && incoming.NetworkHQ == victim.NetworkHQ) continue;
                foreach (Flight escort in escorts)
                {
                    if (Busy(escort)) continue;
                    if (TryIntercept(escort, incoming, victim)) { intercepted[incoming] = Time.timeSinceLevelLoad; break; }
                }
            }
        }

        // Heat-seeker first, then active radar; only a shot that gets there in time.
        private static bool TryIntercept(Flight escort, Missile incoming, Aircraft victim)
        {
            Aircraft aircraft = escort.Aircraft;
            WeaponStation best = null;
            foreach (bool heat in new[] { true, false })
            {
                foreach (WeaponStation station in aircraft.weaponStations)
                {
                    WeaponInfo info = station?.WeaponInfo;
                    if (info == null || !info.missile || station.Ammo <= 0) continue;
                    bool ir = IrDefence.IsHeatSeeking(info);
                    bool arh = info.weaponPrefab != null && info.weaponPrefab.GetComponentInChildren<ARHSeeker>(true) != null;
                    if (heat ? !ir : !arh) continue;
                    if (info.effectiveness.antiAir + info.effectiveness.antiMissile <= 0.01f) continue;
                    best = station;
                    break;
                }
                if (best != null) break;
            }
            if (best == null) return false;

            WeaponInfo weapon = best.WeaponInfo;
            Vector3 toIncoming = incoming.GlobalPosition() - aircraft.GlobalPosition();
            float range = toIncoming.magnitude;
            if (range > weapon.targetRequirements.maxRange || range < weapon.targetRequirements.minRange) return false;
            float offNose = Vector3.Angle(aircraft.transform.forward, toIncoming);
            if (offNose > Mathf.Max(weapon.targetRequirements.minAlignment, IrDefence.IsHeatSeeking(weapon) ? 45f : 60f)) return false;

            Vector3 incomingVelocity = incoming.rb != null ? incoming.rb.velocity : Vector3.zero;
            Vector3 toVictim = victim.GlobalPosition() - incoming.GlobalPosition();
            float closingOnVictim = Mathf.Max(Vector3.Dot(incomingVelocity, toVictim.normalized), 50f);
            float impact = toVictim.magnitude / closingOnVictim;
            float closingOnUs = Vector3.Dot(incomingVelocity, (-toIncoming).normalized);
            float ours = range / Mathf.Max(weapon.GetMaxSpeed() * 0.8f + closingOnUs, 50f);
            if (ours > impact - 1.5f) return false;

            Weapon launcher = null;
            foreach (Weapon candidate in best.Weapons) if (candidate != null && candidate.ammo > 0) { launcher = candidate; break; }
            if (launcher == null) return false;
            int before = launcher.ammo;
            launcher.Fire(aircraft, incoming, aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero, best, incoming.GlobalPosition());
            if (launcher.ammo >= before) return false;
            Report(escort.Name + " · " + weapon.weaponName + " at a missile fired on " +
                (FlightOrders.Of(victim)?.Name ?? "its charge") + " · " + ours.ToString("0.0") + " s to meet it, " + impact.ToString("0.0") + " s to impact");
            return true;
        }

        private static void Report(string line)
        {
            Plugin.Log.LogInfo("[escort] " + line);
            CommandState.Say(line);
        }
    }
}

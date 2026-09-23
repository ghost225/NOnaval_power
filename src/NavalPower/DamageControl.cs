using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    public sealed class CompartmentSnapshot
    {
        public int Id;
        public string Name;
        public string State;
        public float IntegrityPercent;      // hull condition, 0-100
        public float FloodedPercent;        // water in the compartment, 0-100
        public float LeakRate, LeakRateMax, LeakPercentOfMax;
        public bool Sealed, Submerged, Detached, Removed, Working, Priority;
    }

    public sealed class DamageSnapshot
    {
        public string ShipState;
        public float DamageControlPool, DamageControlPoolMax;
        public float ListDegrees, TrimDegrees;
        public int Flooding, Critical, Lost;
        public CompartmentSnapshot[] Compartments = new CompartmentSnapshot[0];
    }

    // Reads and directs the game's own damage control.
    //
    // Nuclear Option already simulates this: each ShipPart tracks displacement
    // and a leak rate, parties turn out damageControlDelay seconds after damage
    // (scaled by Ship.skill, i.e. crew quality) and then work once a second,
    // plugging and pumping against the finite Ship.damageControlAvailable pool.
    // A compartment that floods past damageControlDeploymentThreshold is written
    // off and sealed; if the pool runs dry, connected compartments cascade.
    //
    // None of that is reimplemented here. What is added is visibility, and the
    // ability to concentrate a pool that the native code otherwise spreads
    // evenly over every leaking compartment.
    public static class DamageControl
    {
        private static readonly FieldInfo Displacement = AccessTools.Field(typeof(ShipPart), "displacement");
        private static readonly FieldInfo LeakRate = AccessTools.Field(typeof(ShipPart), "leakRate");
        private static readonly FieldInfo LeakRateMax = AccessTools.Field(typeof(ShipPart), "leakRateMax");
        private static readonly FieldInfo Compartmentalized = AccessTools.Field(typeof(ShipPart), "compartmentalized");
        private static readonly FieldInfo Submerged = AccessTools.Field(typeof(ShipPart), "submerged");
        private static readonly FieldInfo DcActive = AccessTools.Field(typeof(ShipPart), "damageControlActive");
        private static readonly FieldInfo LeakRateMin = AccessTools.Field(typeof(ShipPart), "leakRateMin");

        internal static string Report()
        {
            return "damage control bindings:" +
                Line("ShipPart.displacement", Displacement) + Line("ShipPart.leakRate", LeakRate) +
                Line("ShipPart.leakRateMax", LeakRateMax) + Line("ShipPart.compartmentalized", Compartmentalized) +
                Line("ShipPart.submerged", Submerged) + Line("ShipPart.damageControlActive", DcActive) +
                Line("ShipPart.leakRateMin", LeakRateMin);
        }

        private static string Line(string name, MemberInfo member) =>
            "\n  " + (member != null ? "ok      " : "MISSING ") + name;

        private static float Read(FieldInfo field, ShipPart part, float fallback) =>
            field != null && part != null ? (float)field.GetValue(part) : fallback;

        private static bool Flag(FieldInfo field, ShipPart part) =>
            field != null && part != null && (bool)field.GetValue(part);

        public static DamageSnapshot GetSnapshot(Ship ship)
        {
            var result = new DamageSnapshot { ShipState = "Unavailable" };
            if (ship == null || !CommandableShip.Is(ship)) return result;
            if (GameManager.GetLocalHQ(out FactionHQ local) && ship.NetworkHQ != local) return result;

            result.ShipState = ship.disabled ? "Disabled / sinking" : "Underway";
            result.DamageControlPool = ship.damageControlAvailable;
            result.DamageControlPoolMax = Capacity(ship);

            // Heel and trim are what actually kill a flooding ship: CheckShipBuoyancy
            // disables the hull past roughly 45 degrees of list or 14 of trim.
            result.ListDegrees = Vector3.Angle(ship.transform.up, Vector3.up);
            result.TrimDegrees = Mathf.Asin(Mathf.Clamp(Vector3.Dot(ship.transform.forward, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;

            HashSet<int> priority = Priorities(ship);
            var rows = new List<CompartmentSnapshot>();

            for (int i = 0; i < ship.damageables.Count; i++)
            {
                DamageablePart registered = ship.damageables[i];
                var part = registered.Damageable as UnitPart;
                var compartment = part as ShipPart;
                var row = new CompartmentSnapshot
                {
                    Id = i,
                    Name = part != null ? Naming.Pretty(part.name) : "Removed section " + (i + 1),
                    IntegrityPercent = float.NaN,
                    FloodedPercent = float.NaN
                };

                if (registered.Removed || part == null)
                {
                    row.State = "Removed";
                    row.Removed = true;
                    result.Lost++;
                    rows.Add(row);
                    continue;
                }

                row.IntegrityPercent = CommandableShip.Finite(part.hitPoints) ? Mathf.Clamp(part.hitPoints, 0f, 100f) : float.NaN;
                row.Detached = part.IsDetached();
                row.Priority = priority.Contains(i);

                if (compartment != null)
                {
                    float original = compartment.GetOriginalDisplacement();
                    if (original > 0.01f)
                        row.FloodedPercent = Mathf.Clamp01(1f - Read(Displacement, compartment, original) / original) * 100f;
                    row.LeakRate = Read(LeakRate, compartment, 0f);
                    row.LeakRateMax = Read(LeakRateMax, compartment, 0f);
                    // Litres a second, near enough: what the compartment is
                    // taking on right now, which is the number that says whether
                    // damage control is winning.
                    row.LeakPercentOfMax = row.LeakRateMax > 0.001f
                        ? Mathf.Clamp01(row.LeakRate / row.LeakRateMax) * 100f : 0f;
                    row.Sealed = Flag(Compartmentalized, compartment);
                    row.Submerged = Flag(Submerged, compartment);
                    row.Working = Flag(DcActive, compartment) && !row.Sealed && !row.Submerged;
                }

                row.State =
                    row.Detached ? "Detached" :
                    row.Submerged ? "Submerged" :
                    row.Sealed ? "Sealed off" :
                    row.LeakRate > 0.01f ? (row.Working ? "Flooding · DC working" : "Flooding") :
                    compartment != null && compartment.IsCriticallyDamaged() ? "Critical" :
                    row.IntegrityPercent <= 0f ? "Integrity gone" :
                    row.IntegrityPercent < 100f ? "Damaged" : "Sound";

                if (row.Detached || row.Removed) result.Lost++;
                else if (row.LeakRate > 0.01f || row.Submerged) result.Flooding++;
                else if (row.State == "Critical" || row.State == "Integrity gone") result.Critical++;

                rows.Add(row);
            }

            result.Compartments = rows.ToArray();
            return result;
        }

        // ---- control ------------------------------------------------------

        private static readonly Dictionary<Ship, HashSet<int>> priorities = new Dictionary<Ship, HashSet<int>>();

        // The pool is authored per hull and only ever depletes, so the largest
        // value seen is the ship's full capacity. There is no native crew count
        // to read; this is the closest honest measure of reserve remaining.
        private static readonly Dictionary<Ship, float> capacity = new Dictionary<Ship, float>();

        private static float Capacity(Ship ship)
        {
            float now = ship.damageControlAvailable;
            if (!capacity.TryGetValue(ship, out float best) || now > best) capacity[ship] = best = now;
            return best;
        }

        private static HashSet<int> Priorities(Ship ship)
        {
            if (ship == null) return new HashSet<int>();
            if (!priorities.TryGetValue(ship, out HashSet<int> set))
            {
                set = new HashSet<int>();
                priorities[ship] = set;
            }
            return set;
        }

        public static bool TogglePriority(Ship ship, int compartmentId, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            HashSet<int> set = Priorities(ship);
            ShipPart part = PartAt(ship, compartmentId);
            string name = part != null ? Naming.Pretty(part.name) : "Compartment " + compartmentId;
            if (!set.Remove(compartmentId))
            {
                set.Add(compartmentId);
                reason = "Damage control concentrated on " + name + ".";
            }
            else
            {
                reason = set.Count == 0
                    ? "Damage control released to work the whole ship."
                    : name + " no longer prioritised.";
            }
            return true;
        }

        public static bool ClearPriorities(Ship ship, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            Priorities(ship).Clear();
            reason = "Damage control working the whole ship.";
            return true;
        }

        // Deliberately writing a compartment off: it stops drawing on the pool
        // so the rest of the ship keeps it. Irreversible, exactly as when the
        // native code does it at the deployment threshold.
        public static bool SealCompartment(Ship ship, int compartmentId, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            ShipPart part = PartAt(ship, compartmentId);
            if (part == null) { reason = "That compartment cannot be sealed."; return false; }
            if (Compartmentalized == null) { reason = "Sealing is unavailable on this build."; return false; }
            if (Flag(Compartmentalized, part)) { reason = "Already sealed off."; return false; }
            Compartmentalized.SetValue(part, true);
            Priorities(ship).Remove(compartmentId);
            reason = Naming.Pretty(part.name) + " sealed off · abandoned to save the pool.";
            return true;
        }

        private static ShipPart PartAt(Ship ship, int compartmentId)
        {
            if (ship == null || compartmentId < 0 || compartmentId >= ship.damageables.Count) return null;
            return ship.damageables[compartmentId].Damageable as ShipPart;
        }

        private static float nextWork;
        private static readonly List<Ship> commanded = new List<Ship>();

        // A ship keeps its crew's tempo once commanded, rather than reverting
        // the moment the camera moves elsewhere.
        internal static void Adopt(Ship ship)
        {
            if (ship != null && !commanded.Contains(ship)) commanded.Add(ship);
        }

        internal static void WorkAll()
        {
            for (int i = commanded.Count - 1; i >= 0; i--)
            {
                Ship ship = commanded[i];
                if (ship == null || ship.disabled) { commanded.RemoveAt(i); continue; }
                Work(ship);
            }
        }

        // Concentrate the ship's effort rather than merely withholding it.
        //
        // The native routine gives every leaking compartment the same small
        // step once a second, so suppressing the others -- which is all the
        // priority patch did on its own -- changed nothing you could see. The
        // effort those compartments would have received is now applied to the
        // ones actually named, at the same cost to the pool. Total capacity is
        // unchanged; where it goes is the decision.
        internal static void Work(Ship ship)
        {
            if (ship == null || !ship.IsServer || !ship.LocalSim) return;
            if (Time.timeSinceLevelLoad < nextWork) return;
            nextWork = Time.timeSinceLevelLoad + 1f;                 // the native cadence
            if (ship.damageControlAvailable <= 0f) return;

            // Bring the whole ship up to the ordered tempo first. The native
            // rate resolves flooding over something like a thousand seconds,
            // which is far longer than a fight lasts, so at this game's pace
            // nothing damage control does is ever seen.
            int rate = Mathf.Max(1, Settings.DamageControlRate.Value);
            if (rate > 1)
            {
                for (int i = 0; i < ship.damageables.Count; i++)
                {
                    var part = ship.damageables[i].Damageable as ShipPart;
                    if (!Repairable(part)) continue;
                    for (int pass = 1; pass < rate && ship.damageControlAvailable > 0f; pass++)
                        RepairStep(ship, part);
                }
            }

            HashSet<int> priority = Priorities(ship);
            if (priority.Count == 0) return;

            int leaking = 0, chosen = 0;
            for (int i = 0; i < ship.damageables.Count; i++)
            {
                var part = ship.damageables[i].Damageable as ShipPart;
                if (!Repairable(part)) continue;
                leaking++;
                if (priority.Contains(i)) chosen++;
            }
            if (chosen == 0 || leaking <= chosen) return;            // nothing was withheld

            // Split what the suppressed compartments would have had.
            int shares = Mathf.Clamp((leaking - chosen) / chosen, 0, Settings.DamageControlConcentration.Value);
            if (shares <= 0) return;

            for (int i = 0; i < ship.damageables.Count; i++)
            {
                if (!priority.Contains(i)) continue;
                var part = ship.damageables[i].Damageable as ShipPart;
                if (!Repairable(part)) continue;
                for (int pass = 0; pass < shares && ship.damageControlAvailable > 0f; pass++)
                    RepairStep(ship, part);
            }
        }

        private static bool Repairable(ShipPart part) =>
            part != null && !part.IsDetached() && !Flag(Compartmentalized, part) && !Flag(Submerged, part) &&
            (Read(LeakRate, part, 0f) > 0.001f || Read(Displacement, part, 0f) < part.GetOriginalDisplacement());

        // One pass of exactly what the native routine does, so concentrated
        // work costs the pool what the same work would have cost anywhere else.
        private static void RepairStep(Ship ship, ShipPart part)
        {
            if (LeakRate == null || Displacement == null || LeakRateMin == null) return;
            float plug = 0.02f * Read(LeakRateMin, part, 0f);
            float pump = 0.001f * part.GetOriginalDisplacement();
            LeakRate.SetValue(part, Mathf.Max(Read(LeakRate, part, 0f) - plug, 0f));
            Displacement.SetValue(part,
                Mathf.Min(Read(Displacement, part, 0f) + pump, part.GetOriginalDisplacement()));
            // Charging for every pass keeps the ship's total capacity what the
            // game intended: the same damage control, delivered sooner. Not
            // charging makes the crew genuinely better rather than faster.
            if (Settings.DamageControlPreserveCapacity.Value)
                ship.damageControlAvailable -= 10f * plug + pump;
        }

        internal static bool IsDeprioritised(ShipPart part)
        {
            if (part == null) return false;
            var ship = part.parentUnit as Ship;
            if (ship == null || !priorities.TryGetValue(ship, out HashSet<int> set) || set.Count == 0) return false;
            for (int i = 0; i < ship.damageables.Count; i++)
                if (ReferenceEquals(ship.damageables[i].Damageable, part)) return !set.Contains(i);
            return false;
        }
    }

    // The native routine spreads the pool over every leaking compartment. When
    // the player has named priorities, the others wait their turn instead.
    [HarmonyPatch(typeof(ShipPart), "DamageControl")]
    internal static class DamageControlPriorityPatch
    {
        private static bool Prefix(ShipPart __instance) => !DamageControl.IsDeprioritised(__instance);
    }
}

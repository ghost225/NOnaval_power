using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    public enum EngagementMode { WeaponsFree, WeaponsTight, WeaponsHold }

    // Rules of engagement for automatic fire.
    //
    // Vanilla ships carry no FireControl component -- ShipAI picks the ship's
    // target and each Turret picks its own -- so there is no single native
    // decision to patch. Instead a turret whose current pick is not sanctioned
    // is denied it: cleared and held manual until the mode allows it again.
    public static class EngagementPolicy
    {
        public static EngagementMode GetMode(Ship ship) =>
            ship != null ? ShipEngagement.Ensure(ship).Mode : EngagementMode.WeaponsFree;

        public static bool SetMode(Ship ship, EngagementMode mode, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            ShipEngagement.Ensure(ship).Mode = mode;
            reason = mode == EngagementMode.WeaponsFree ? "Weapons free · automatic engagement unrestricted"
                : mode == EngagementMode.WeaponsTight ? "Weapons tight · inbound weapons and units that have fired on us only"
                : "Weapons hold · point defence against inbound weapons only";
            return true;
        }

        // May this shot leave the ship? Anything we did not order, from a ship
        // under command, has to satisfy the rules of engagement.
        internal static bool Allows(Unit owner, Unit target)
        {
            if (ShipWeapons.Firing) return true;              // our own explicit order
            if (!(owner is Ship ship)) return true;
            var state = ship.GetComponent<ShipEngagement>();
            if (state == null || state.Mode == EngagementMode.WeaponsFree) return true;
            return state.Permits(target);
        }

        public static string Describe(EngagementMode mode) =>
            mode == EngagementMode.WeaponsFree ? "Weapons Free"
            : mode == EngagementMode.WeaponsTight ? "Weapons Tight" : "Weapons Hold";
    }

    // Denial at the weapon rather than at the turret.
    //
    // Holding a turret manual stops the base Turret firing on its own, but it
    // is not the only way a shot can leave a ship: a turret subclass or a
    // modded mount can drive its weapon by another path, and then weapons tight
    // and weapons hold are quietly advisory. Every shot goes through
    // Weapon.Fire, so the rules are enforced there instead.
    //
    // Fire is virtual and subclasses override it, so patching the base type
    // alone would miss exactly the weapons that need catching -- each declared
    // override is targeted individually.
    [HarmonyPatch]
    internal static class WeaponReleasePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var signature = new[]
            {
                typeof(Unit), typeof(Unit), typeof(Vector3), typeof(WeaponStation), typeof(GlobalPosition)
            };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }
                foreach (Type type in types)
                {
                    if (!typeof(Weapon).IsAssignableFrom(type)) continue;
                    MethodInfo fire;
                    try { fire = type.GetMethod("Fire", flags, null, signature, null); }
                    catch { continue; }
                    if (fire != null && !fire.IsAbstract) yield return fire;
                }
            }
        }

        private static bool Prefix(Unit owner, Unit target) => EngagementPolicy.Allows(owner, target);
    }

    internal sealed class ShipEngagement : MonoBehaviour
    {
        private Ship ship;
        private float nextSweep, nextThreatScan;
        private readonly HashSet<uint> attackers = new HashSet<uint>();
        private readonly List<Unit> inbound = new List<Unit>();

        internal EngagementMode Mode = EngagementMode.WeaponsFree;

        internal static ShipEngagement Ensure(Ship ship)
        {
            var state = ship.GetComponent<ShipEngagement>();
            if (state == null)
            {
                state = ship.gameObject.AddComponent<ShipEngagement>();
                state.ship = ship;
            }
            return state;
        }

        private void Update()
        {
            if (ship == null || ship.disabled || !ship.IsServer || !ship.LocalSim) return;
            if (Mode == EngagementMode.WeaponsFree)
            {
                // Nothing to deny; make sure nothing stays held from a stricter mode.
                if (Time.timeSinceLevelLoad >= nextSweep) { nextSweep = Time.timeSinceLevelLoad + 1f; ReleaseAll(); }
                return;
            }
            if (Time.timeSinceLevelLoad >= nextThreatScan) { nextThreatScan = Time.timeSinceLevelLoad + .5f; ScanThreats(); }
            if (Time.timeSinceLevelLoad < nextSweep) return;
            nextSweep = Time.timeSinceLevelLoad + .25f;
            Sweep();
        }

        private void ScanThreats()
        {
            inbound.Clear();
            attackers.Clear();
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Missile missile) || missile.disabled) continue;
                if (missile.NetworkHQ == null || missile.NetworkHQ == ship.NetworkHQ) continue;
                if (missile.targetID != ship.persistentID) continue;
                inbound.Add(missile);
                if (missile.owner != null) attackers.Add(missile.owner.persistentID.Id);
            }
        }

        // The same rules the turret sweep applies, asked of a single shot.
        internal bool Permits(Unit target)
        {
            if (target == null) return false;
            if (target is Missile) return inbound.Contains(target);
            if (Mode == EngagementMode.WeaponsHold) return false;
            return attackers.Contains(target.persistentID.Id);
        }

        private bool Sanctioned(Turret turret, Unit target)
        {
            if (target == null) return true;                         // Idle mounts are fine.
            if (target is Missile) return inbound.Contains(target);  // Only actual inbounds.
            if (Mode == EngagementMode.WeaponsHold) return false;    // Hold permits nothing else.
            return attackers.Contains(target.persistentID.Id);       // Tight: only those who shot at us.
        }

        private void Sweep()
        {
            foreach (Turret turret in ship.GetComponentsInChildren<Turret>(true))
            {
                if (turret == null || turret.GetAttachedUnit() != ship) continue;
                // A mount carrying one of our explicit orders is not the policy's business.
                if (ShipWeapons.Holds(ship, turret)) continue;
                Unit target = turret.GetTarget();
                if (Sanctioned(turret, target)) { turret.SetManual(false); continue; }
                if (NativeBindings.TurretChooseTarget != null)
                    NativeBindings.TurretChooseTarget.Invoke(turret, new object[] { true });
                turret.SetManual(true);
            }
        }

        private void ReleaseAll()
        {
            foreach (Turret turret in ship.GetComponentsInChildren<Turret>(true))
            {
                if (turret == null || turret.GetAttachedUnit() != ship) continue;
                if (ShipWeapons.Holds(ship, turret)) continue;
                turret.SetManual(false);
            }
        }
    }
}

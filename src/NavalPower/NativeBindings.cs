using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    // Every private native member this mod touches, resolved once. Anything
    // that fails to bind degrades to a documented fallback rather than throwing
    // at an unpredictable moment, and Report() says which on startup.
    internal static class NativeBindings
    {
        internal static readonly FieldInfo WeaponLastFired = AccessTools.Field(typeof(Weapon), "lastFired");
        internal static readonly FieldInfo LauncherInterval = AccessTools.Field(typeof(MissileLauncher), "fireInterval");
        internal static readonly FieldInfo GunTrigger = AccessTools.Field(typeof(Gun), "ticksSinceTriggerPull");
        internal static readonly FieldInfo GunQueue = AccessTools.Field(typeof(Gun), "queuedBullets");
        internal static readonly FieldInfo LaserTrigger = AccessTools.Field(typeof(Laser), "fireCommanded");
        internal static readonly MethodInfo TurretChooseTarget = AccessTools.Method(typeof(Turret), "ChooseTarget");
        internal static readonly FieldInfo AiCommandedDestination = AccessTools.Field(typeof(ShipAI), "commandedDestination");
        internal static readonly FieldInfo TurretFiresWithoutAiming = AccessTools.Field(typeof(Turret), "firesWithoutAiming");
        internal static readonly FieldInfo TurretFiringCones = AccessTools.Field(typeof(Turret), "firingCones");

        // Turret.AimTurret returns a plain range test for a fixed launcher and
        // never assigns onTarget, so IsOnTarget() is permanently false on those
        // mounts. Gating fire on it would block every VLS order forever.
        internal static bool FiresWithoutAiming(Turret turret) =>
            turret != null && TurretFiresWithoutAiming != null && (bool)TurretFiresWithoutAiming.GetValue(turret);

        // Whether the mount can physically bear on the target. Picking a mount
        // that can never train onto the contact is the main source of an order
        // sitting idle instead of shooting.
        internal static bool CanServe(Turret turret, Unit owner, Unit target)
        {
            if (turret == null) return true;
            if (target == null || owner == null || owner.NetworkHQ == null) return false;
            if (!owner.NetworkHQ.TryGetKnownPosition(target, out GlobalPosition known)) return false;
            Vector3 direction = known - turret.transform.GlobalPosition();
            if (direction.sqrMagnitude < .0001f) return false;
            var cones = TurretFiringCones?.GetValue(turret) as FiringCone[];
            return cones == null || cones.Length == 0 ||
                FiringConeChecker.VectorWithinFiringCones(cones, direction, out _);
        }

        internal static string Report()
        {
            var text = new StringBuilder("native bindings:");
            Append(text, "Weapon.lastFired", WeaponLastFired);
            Append(text, "MissileLauncher.fireInterval", LauncherInterval);
            Append(text, "Gun.ticksSinceTriggerPull", GunTrigger);
            Append(text, "Gun.queuedBullets", GunQueue);
            Append(text, "Laser.fireCommanded", LaserTrigger);
            Append(text, "Turret.ChooseTarget", TurretChooseTarget);
            Append(text, "ShipAI.commandedDestination", AiCommandedDestination);
            Append(text, "Turret.firesWithoutAiming", TurretFiresWithoutAiming);
            Append(text, "Turret.firingCones", TurretFiringCones);
            return text.ToString();
        }

        private static void Append(StringBuilder text, string name, MemberInfo member) =>
            text.Append("\n  ").Append(member != null ? "ok      " : "MISSING ").Append(name);

        internal static float LastFired(Weapon weapon) =>
            WeaponLastFired != null && weapon != null ? (float)WeaponLastFired.GetValue(weapon) : 0f;

        internal static float FireInterval(MissileLauncher launcher) =>
            LauncherInterval != null && launcher != null ? (float)LauncherInterval.GetValue(launcher) : 0f;

        // Best effort. Native weapons also stop on their own once Fire stops
        // being called; this only shortens the tail on continuous mounts.
        internal static void StopTrigger(Weapon weapon)
        {
            if (weapon is Gun gun)
            {
                GunTrigger?.SetValue(gun, int.MaxValue / 2);
                GunQueue?.SetValue(gun, 0);
            }
            else if (weapon is Laser laser)
            {
                LaserTrigger?.SetValue(laser, false);
            }
        }
    }
}

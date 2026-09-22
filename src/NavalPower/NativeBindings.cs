using System.Reflection;
using System.Text;
using HarmonyLib;

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

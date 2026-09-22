using System.Reflection;
using HarmonyLib;

namespace NavalPower
{
    // AIPilotCombatModes.AssessHQTargets runs CombatAI.ChooseHQTarget and takes
    // whatever it likes the look of. Pilot.SetPrimaryTarget exists but nothing
    // in the combat state ever reads it, so designating a target means pinning
    // the state's own choice after its search.
    //
    // A postfix rather than a prefix: the native pass still sets up weapon
    // state and bookkeeping, and only the choice of target is overridden.
    [HarmonyPatch(typeof(AIPilotCombatModes), "AssessHQTargets")]
    internal static class StrikeDesignationPatch
    {
        private static readonly FieldInfo CurrentTarget = AccessTools.Field(typeof(AIPilotCombatModes), "currentTarget");
        private static readonly FieldInfo CurrentWeaponInfo = AccessTools.Field(typeof(AIPilotCombatModes), "currentWeaponInfo");
        private static readonly FieldInfo TargetTracking = AccessTools.Field(typeof(AIPilotCombatModes), "currentTargetTracking");
        private static readonly FieldInfo StateAircraft = AccessTools.Field(typeof(PilotBaseState), "aircraft");

        internal static string Report() =>
            "strike designation:" +
            Line("AIPilotCombatModes.currentTarget", CurrentTarget) +
            Line("AIPilotCombatModes.currentWeaponInfo", CurrentWeaponInfo) +
            Line("AIPilotCombatModes.currentTargetTracking", TargetTracking) +
            Line("PilotBaseState.aircraft", StateAircraft);

        private static string Line(string name, MemberInfo member) =>
            "\n  " + (member != null ? "ok      " : "MISSING ") + name;

        private static void Postfix(AIPilotCombatModes __instance)
        {
            if (CurrentTarget == null || StateAircraft == null) return;
            if (!(StateAircraft.GetValue(__instance) is Aircraft aircraft)) return;

            Flight flight = FlightOrders.Of(aircraft);
            if (flight == null || flight.Mode != FlightMode.Strike) return;
            Unit target = flight.Target;
            if (target == null || target.disabled) return;
            if (ReferenceEquals(CurrentTarget.GetValue(__instance), target)) return;

            // Pick a station that can actually hurt this target rather than
            // keeping whatever was chosen for the target we just replaced.
            WeaponStation station = FlightOrders.BestStationFor(aircraft, target);
            if (station != null && aircraft.weaponManager != null)
            {
                aircraft.weaponManager.currentWeaponStation = station;
                CurrentWeaponInfo?.SetValue(__instance, station.WeaponInfo);
            }

            CurrentTarget.SetValue(__instance, target);
            if (aircraft.NetworkHQ != null)
                TargetTracking?.SetValue(__instance, aircraft.NetworkHQ.GetTrackingData(target.persistentID));
            if (aircraft.weaponManager != null)
            {
                aircraft.weaponManager.ClearTargetList();
                aircraft.weaponManager.AddTargetList(target);
            }
        }
    }
}

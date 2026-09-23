using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    // Approach speed for a recovery to a ship's deck.
    //
    // LandingState_SearchAirbase computes the speed from the airframe and then,
    // for vertical-landing types only, does this:
    //
    //     adjustedLandingSpeed = (airbase.AttachedAirbase ? 90 : 90);
    //
    // Both branches are the same number. The distinction was clearly meant to
    // exist and does not, and for everything else there is no deck adjustment
    // at all -- so an aircraft flies a full runway approach onto a flattop,
    // arrives fast and high, fails to stabilise and goes around, repeatedly.
    //
    // Only recoveries to an attached airbase are touched; approaches to real
    // runways keep the speed the game calculated.
    [HarmonyPatch(typeof(AIPilotLandingState), "LandingState_SearchAirbase")]
    internal static class CarrierApproachPatch
    {
        private static readonly FieldInfo AirbaseField = AccessTools.Field(typeof(AIPilotLandingState), "airbase");
        private static readonly FieldInfo LandingSpeed = AccessTools.Field(typeof(AIPilotLandingState), "adjustedLandingSpeed");
        private static readonly FieldInfo StateAircraft = AccessTools.Field(typeof(PilotBaseState), "aircraft");

        internal static string Report() =>
            "carrier recovery:" +
            "\n  " + (AirbaseField != null ? "ok      " : "MISSING ") + "AIPilotLandingState.airbase" +
            "\n  " + (LandingSpeed != null ? "ok      " : "MISSING ") + "AIPilotLandingState.adjustedLandingSpeed";

        private static void Postfix(AIPilotLandingState __instance)
        {
            if (AirbaseField == null || LandingSpeed == null) return;
            if (!Settings.CarrierApproachFix.Value) return;
            if (!(AirbaseField.GetValue(__instance) is Airbase airbase) || !airbase.AttachedAirbase) return;

            float speed = (float)LandingSpeed.GetValue(__instance);
            float wanted = speed * Mathf.Clamp01(Settings.CarrierApproachFactor.Value);
            LandingSpeed.SetValue(__instance, wanted);

            if (StateAircraft?.GetValue(__instance) is Aircraft aircraft)
                Plugin.Log.LogInfo("[recovery] " + (aircraft.definition?.unitName ?? aircraft.name) +
                    " · deck approach " + speed.ToString("0") + " to " + wanted.ToString("0"));
        }
    }
}

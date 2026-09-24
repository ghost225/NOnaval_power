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

        private static void Postfix(AIPilotLandingState __instance) =>
            Guard.Run("Carrier recovery", () => Adjust(__instance));

        // Only the approach speed. Writing the airbase field as well, which
        // this did briefly, is not the same as sending an aircraft somewhere:
        // LandingState_SearchAirbase picks the airbase and reserves a runway on
        // it in the same breath, and everything after uses that reservation for
        // the glideslope, the touchdown point and the deregistration, while the
        // airbase field is what registers usage. Replace one and they disagree
        // -- the aircraft flies a correct approach to the right deck, lands,
        // disembarks, and is never struck below, because the deck it told was
        // not the deck it booked. Doing it properly means moving the
        // reservation too, which is more machinery than the difference is
        // worth: at sea the nearest usable field already is the carrier.
        private static void Adjust(AIPilotLandingState __instance)
        {
            if (AirbaseField == null || LandingSpeed == null) return;
            var aircraft = StateAircraft?.GetValue(__instance) as Aircraft;
            if (!Settings.CarrierApproachFix.Value) return;
            if (!(AirbaseField.GetValue(__instance) is Airbase airbase) || !airbase.AttachedAirbase) return;

            float speed = (float)LandingSpeed.GetValue(__instance);
            float wanted = speed * Mathf.Clamp01(Settings.CarrierApproachFactor.Value);
            LandingSpeed.SetValue(__instance, wanted);

            if (aircraft != null)
                Plugin.Log.LogInfo("[recovery] " + (aircraft.definition?.unitName ?? aircraft.name) +
                    " · deck approach " + speed.ToString("0") + " to " + wanted.ToString("0"));
        }

    }
}

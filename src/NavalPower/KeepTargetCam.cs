using System;
using HarmonyLib;

namespace NavalPower
{
    // Every aircraft is built with a targeting camera, and it removes itself on
    // its first frame unless the aircraft is being flown by the local player:
    //
    //     if (aircraft == null || aircraft.Player == null || !aircraft.Player.IsLocalPlayer)
    //     { Object.Destroy(this); return; }
    //
    // Natively that only ever throws away something nobody will use -- an AI
    // aircraft never gets a pilot. Ours can, so the camera for a flight we
    // launched is kept, dormant, until someone takes the seat. It stays
    // dormant: nothing in Update does anything without the lenses that only
    // Initialize builds, so skipping it for an unflown flight skips nothing.
    //
    // Everything else keeps the native behaviour, including our own flights
    // the moment they are flown and every aircraft that is not ours.
    [HarmonyPatch(typeof(TargetCam), "Update")]
    internal static class KeepTargetCamPatch
    {
        private const string Name = "Keep flight target cameras";
        private static readonly System.Reflection.FieldInfo CamAircraft =
            AccessTools.Field(typeof(TargetCam), "aircraft");

        private static bool Prefix(TargetCam __instance)
        {
            if (!Guard.Ok(Name)) return true;
            try
            {
                if (!(CamAircraft?.GetValue(__instance) is Aircraft aircraft) || aircraft == null) return true;
                if (aircraft.Player != null && aircraft.Player.IsLocalPlayer) return true;    // flown: native
                return FlightOrders.Of(aircraft) == null;                                   // ours: keep
            }
            catch (Exception ex) { Guard.Failed(Name, ex); return true; }
        }
    }
}

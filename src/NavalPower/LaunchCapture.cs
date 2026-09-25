using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace NavalPower
{
    // Identifies the aircraft a launch actually produced.
    //
    // Matching by type and proximity is unreliable: when the faction AI is
    // launching the same airframe off the same deck, the first one to appear
    // wins and ours is either mis-adopted or missed entirely. Hangar.SpawnAircraft
    // receives the exact Loadout instance handed to TrySpawnAircraft, and AI
    // spawns pass null, so the reference identifies our aircraft precisely.
    [HarmonyPatch(typeof(Hangar), "SpawnAircraft")]
    internal static class LaunchCapturePatch
    {
        private static readonly FieldInfo SpawnedObject = AccessTools.Field(typeof(Hangar), "spawnedObject");

        internal static string Report() =>
            "launch capture:\n  " + (SpawnedObject != null ? "ok      " : "MISSING ") + "Hangar.spawnedObject";

        private static void Postfix(Hangar __instance, Loadout loadout) =>
            Guard.Run("Launch capture", () => Claim(__instance, loadout));

        private static void Claim(Hangar __instance, Loadout loadout)
        {
            if (SpawnedObject == null || __instance == null) return;
            // The hangar records what it just built, so read that one rather
            // than searching the registry for something that looks similar.
            if (!(SpawnedObject.GetValue(__instance) is GameObject spawned) || spawned == null) return;
            Aircraft aircraft = spawned.GetComponent<Aircraft>();
            if (aircraft == null) return;

            // Note every launch off a deck or field, ours or the faction AI's:
            // deck traffic is about this field, not about whatever happens to
            // be flying nearby.
            DeckTraffic.NoteLaunch(__instance.parentAirbase, aircraft);

            if (loadout == null) return;                                // an AI spawn
            Flight claimed = FlightOrders.ClaimLaunch(loadout, aircraft);
            if (claimed != null)
                Plugin.Log.LogInfo("[deck] launch identified · " + claimed.Name);
        }
    }
}

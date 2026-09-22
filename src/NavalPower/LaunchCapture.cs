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

        private static void Postfix(Loadout loadout)
        {
            if (loadout == null || SpawnedObject == null) return;      // an AI spawn
            Flight claimed = FlightOrders.ClaimLaunch(loadout, Spawned());
            if (claimed != null)
                Plugin.Log.LogInfo("[deck] launch identified · " + claimed.Name);
        }

        private static Aircraft Spawned()
        {
            // Read the hangar's own record of what it just built rather than
            // searching the registry for something that looks similar.
            foreach (Hangar hangar in Object.FindObjectsOfType<Hangar>())
            {
                if (!(SpawnedObject.GetValue(hangar) is GameObject spawned) || spawned == null) continue;
                Aircraft aircraft = spawned.GetComponent<Aircraft>();
                if (aircraft != null && !aircraft.disabled && FlightOrders.Of(aircraft) == null) return aircraft;
            }
            return null;
        }
    }
}

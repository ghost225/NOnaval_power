using System;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    // Where the targeting camera goes.
    //
    // Ownership turned out not to be the whole story: with authority taken the
    // log still reports none in the scene at all, while the aircraft's own
    // field holds a reference that Unity says is destroyed. So one existed and
    // something removed it before we ever arrived -- and the component's own
    // teardown hooks are wired inside the ownership branch that never ran, so
    // it is not removing itself.
    //
    // Guessing at this has failed three times. These record every targeting
    // camera as it appears and as it goes, and name what destroyed it.
    internal static class TargetCamTrace
    {
        private static string Describe(TargetCam view)
        {
            if (view == null) return "a targeting camera";
            Transform at = view.transform;
            string path = at.name;
            for (Transform parent = at.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        [HarmonyPatch(typeof(TargetCam), "Awake")]
        internal static class Born
        {
            private static void Postfix(TargetCam __instance) =>
                Guard.Run("Target camera trace: born", () =>
                {
                    if (!Settings.DeckTrace.Value) return;
                    Plugin.Log.LogInfo("[cam] built · " + Describe(__instance));
                });
        }

        [HarmonyPatch(typeof(TargetCam), "OnDestroy")]
        internal static class Gone
        {
            private static void Prefix(TargetCam __instance) =>
                Guard.Run("Target camera trace: gone", () =>
                {
                    if (!Settings.DeckTrace.Value) return;
                    Plugin.Log.LogInfo("[cam] destroyed · " + Describe(__instance) +
                        "\n" + Environment.StackTrace);
                });
        }
    }
}

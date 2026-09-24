
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

        // Only two lines in the game can ask for this component to go, and
        // both are wired inside the ownership branch that we were told never
        // ran. If neither of them speaks, nothing asked: the component went
        // because the object carrying it did, and the answer is in the spawn
        // rather than in the camera.
        [HarmonyPatch(typeof(TargetCam), "Initialize")]
        internal static class Started
        {
            private static void Postfix(TargetCam __instance) =>
                Guard.Run("Target camera trace: start", () =>
                {
                    if (!Settings.DeckTrace.Value) return;
                    var aircraft = AccessTools.Field(typeof(TargetCam), "aircraft")
                        .GetValue(__instance) as Aircraft;
                    Plugin.Log.LogInfo("[cam] initialised · " + Describe(__instance) +
                        " · aircraft " + (aircraft == null ? "none"
                            : (aircraft.definition?.unitName ?? aircraft.name)) +
                        " · authority " + (aircraft?.Identity != null && aircraft.Identity.HasAuthority) +
                        " · lenses " + (aircraft != null && aircraft.targetCam == __instance ? "built" : "not built"));
                });
        }

        [HarmonyPatch(typeof(TargetCam), "TargetCam_OnDetach")]
        internal static class Detached
        {
            private static void Prefix(TargetCam __instance) =>
                Guard.Run("Target camera trace: detach", () =>
                    Plugin.Log.LogInfo("[cam] a part detached · " + Describe(__instance)));
        }

        [HarmonyPatch(typeof(TargetCam), "TargetCam_OnUnitDisable")]
        internal static class UnitGone
        {
            private static void Prefix(TargetCam __instance) =>
                Guard.Run("Target camera trace: unit disabled", () =>
                    Plugin.Log.LogInfo("[cam] its aircraft was disabled · " + Describe(__instance)));
        }

        // The rest of the chain, so a normal spawn and a takeover can be laid
        // side by side and read for the one step that differs. The combat HUD
        // asks for the target view on every frame there is a target; the camera
        // announces a change only when there is one; the screen switches page
        // only when it hears that announcement.
        private static float nextAsk;

        [HarmonyPatch(typeof(TargetCam), nameof(TargetCam.SetTargetCam))]
        internal static class Asked
        {
            private static void Prefix(TargetCam __instance, out bool __state) =>
                __state = Lens(__instance);

            private static void Postfix(TargetCam __instance, bool __state) =>
                Guard.Run("Target camera trace: asked", () =>
                {
                    if (!Settings.DeckTrace.Value) return;
                    bool now = Lens(__instance);
                    // Every frame otherwise; the transition is the interesting part.
                    if (now == __state && Time.unscaledTime < nextAsk) return;
                    nextAsk = Time.unscaledTime + 3f;
                    Plugin.Log.LogInfo("[cam] target view asked for · lens " +
                        (__state ? "was on" : "was off") + ", now " + (now ? "on" : "off") +
                        " · mode " + Mode(__instance));
                });

            private static bool Lens(TargetCam view)
            {
                var lens = AccessTools.Field(typeof(TargetCam), "cam")?.GetValue(view) as Camera;
                return lens != null && lens.enabled;
            }

            private static string Mode(TargetCam view) =>
                AccessTools.Field(typeof(TargetCam), "currentMode")?.GetValue(view)?.ToString() ?? "?";
        }

        [HarmonyPatch(typeof(TacScreen), "TacScreen_OnCamToggle")]
        internal static class ScreenTold
        {
            private static void Prefix(TargetCam.OnCamToggle e) =>
                Guard.Run("Target camera trace: screen told", () =>
                    Plugin.Log.LogInfo("[cam] screen told · " + (e.enabled ? "show " : "hide ") + e.camMode));
        }

        [HarmonyPatch(typeof(TargetCam), "OnDestroy")]
        internal static class Gone
        {
            private static void Prefix(TargetCam __instance) =>
                Guard.Run("Target camera trace: gone", () =>
                {
                    if (!Settings.DeckTrace.Value) return;
                    // The stack here is Unity's own: Destroy is deferred to the
                    // end of the frame, so whoever asked for it is long gone by
                    // the time this runs. The lines above are what name it.
                    Plugin.Log.LogInfo("[cam] destroyed · " + Describe(__instance) +
                        " · object " + (__instance.gameObject == null ? "gone too" : "still here"));
                });
        }
    }
}

using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NavalPower
{
    // Night vision for command mode: the main view and each camera feed.
    //
    // The game's night vision is a post-processing volume swapped in for the
    // level's own. The main view uses the game's toggle as it is, on the
    // player's own night-vision key; the game only refuses it here because the
    // command cursor is showing. The feeds render by hand, so each can look
    // through the same volume for its own render, whatever the main view shows.
    internal static class NightSight
    {
        private static readonly AccessTools.FieldRef<NightVision, Volume> VolumeOf =
            AccessTools.FieldRefAccess<NightVision, Volume>("postProcessing");
        private static readonly AccessTools.FieldRef<NightVision, bool> ActiveOf =
            AccessTools.FieldRefAccess<NightVision, bool>("nightVisActive");
        private static readonly Action<NightVision> UpdateGain =
            AccessTools.MethodDelegate<Action<NightVision>>(AccessTools.Method(typeof(NightVision), "UpdateGain"));

        // Whether the main view is looking through night vision right now.
        internal static bool MainOn => NightVision.i != null && ActiveOf(NightVision.i);

        internal static void Render(Camera camera, bool night)
        {
            Volume nvg = NightVision.i != null ? VolumeOf(NightVision.i) : null;
            Volume level = NetworkSceneSingleton<LevelInfo>.i?.PostProcessing;
            UniversalAdditionalCameraData urp = camera.GetUniversalAdditionalCameraData();
            // The feeds normally render without post-processing, so the main
            // view's night vision never reaches them; only a feed's own does.
            if (!night || nvg == null || level == null || urp == null) { camera.Render(); return; }

            bool nvgWas = nvg.enabled, levelWas = level.enabled, effectsWere = urp.renderPostProcessing;
            Transform triggerWas = urp.volumeTrigger;
            VolumeFrameworkUpdateMode modeWas = camera.GetVolumeFrameworkUpdateMode();
            Camera main = SceneSingleton<CameraStateManager>.i?.mainCamera;
            Describe(nvg, level, camera, urp, main);
            try
            {
                // Its gain follows the light, and is only kept up to date while
                // the main view is using it.
                if (!nvgWas) UpdateGain(NightVision.i);
                nvg.enabled = true;
                level.enabled = false;
                urp.renderPostProcessing = true;
                // The volume may be local to the main camera rather than
                // global, so the feed looks for it from there; and a camera
                // that only updates its volumes when told to would keep the
                // stack it had before night vision came on.
                if (main != null) urp.volumeTrigger = main.transform;
                camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);
                camera.Render();
            }
            finally
            {
                nvg.enabled = nvgWas;
                level.enabled = levelWas;
                urp.renderPostProcessing = effectsWere;
                urp.volumeTrigger = triggerWas;
                camera.SetVolumeFrameworkUpdateMode(modeWas);
            }
        }

        private static bool described;

        // Once, for the log: how the night-vision volume is set up.
        private static void Describe(Volume nvg, Volume level, Camera camera, UniversalAdditionalCameraData urp, Camera main)
        {
            if (described) return;
            described = true;
            UniversalAdditionalCameraData mainUrp = main != null ? main.GetUniversalAdditionalCameraData() : null;
            Plugin.Log.LogInfo("[nv] volume '" + nvg.name + "' global=" + nvg.isGlobal + " layer=" + nvg.gameObject.layer +
                " priority=" + nvg.priority + " weight=" + nvg.weight + " profile=" + (nvg.sharedProfile != null ? nvg.sharedProfile.name : "none") +
                " | level '" + level.name + "' layer=" + level.gameObject.layer + " priority=" + level.priority +
                " | feed mask=" + urp.volumeLayerMask.value + " mode=" + camera.GetVolumeFrameworkUpdateMode() + " asset=" + UniversalRenderPipeline.asset?.volumeFrameworkUpdateMode +
                " | main mask=" + (mainUrp != null ? mainUrp.volumeLayerMask.value.ToString() : "?") +
                " post=" + (mainUrp != null && mainUrp.renderPostProcessing) + " mode=" + (main != null ? main.GetVolumeFrameworkUpdateMode().ToString() : "?"));
        }
    }

    // The game ignores its night-vision key while any cursor flag is up, which
    // in command mode is always. Our own cursor, and the map, are no reason to.
    [HarmonyPatch(typeof(NightVision), "BlockToggle")]
    internal static class CommandNightVisionPatch
    {
        private const string Name = "Night vision in command mode";
        private const CursorFlags Harmless = MapCommand.CommandCursor | CursorFlags.Map | CursorFlags.CameraControlUI;

        private static void Postfix(ref bool __result)
        {
            if (!__result || !Guard.Ok(Name)) return;
            try
            {
                if (!CommandState.Active || NuclearOption.MissionEditorScripts.InputFieldChecker.InsideInputField) return;
                if ((CursorManager.GetFlags() & ~Harmless) == CursorFlags.None) __result = false;
            }
            catch (Exception ex) { Guard.Failed(Name, ex); }
        }
    }
}

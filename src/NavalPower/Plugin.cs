using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    [BepInPlugin(Id, "Naval Power", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "com.navalpower.nuclearoption";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            try
            {
                Settings.Bind(Config);
                harmony = new Harmony(Id);
                harmony.PatchAll(typeof(Plugin).Assembly);
                gameObject.AddComponent<TestHarness>();
                var ui = gameObject.AddComponent<CommandUi>();
                var map = gameObject.AddComponent<MapCommand>();
                map.Ui = ui;
                Log.LogInfo("Naval Power " + Version + " ready. " + NativeBindings.Report() + "\n" + DamageControl.Report() + "\n" + StrikeDesignationPatch.Report() + "\n" + LaunchCapturePatch.Report() + "\n" + DeckTraffic.Report());
            }
            catch (Exception ex)
            {
                harmony?.UnpatchSelf();
                Log.LogError("Naval Power could not bind to this game version: " + ex);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }

        // The ship the spectator camera is following, when it is one we can command.
        internal static Ship CommandedShip()
        {
            var cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null) return null;
            return CommandableShip.Is(cameras.followingUnit) ? cameras.followingUnit as Ship : null;
        }
    }
}

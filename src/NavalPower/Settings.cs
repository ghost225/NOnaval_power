using BepInEx.Configuration;
using UnityEngine;

namespace NavalPower
{
    // Everything worth changing without a rebuild. Rendered by BepInEx
    // ConfigurationManager, and written to BepInEx/config on first run.
    internal static class Settings
    {
        internal static ConfigEntry<KeyboardShortcut> ResumeCommand;

        internal static ConfigEntry<float> DefaultAltitude;
        internal static ConfigEntry<float> DefaultAreaRadius;
        internal static ConfigEntry<float> MinimumClearance;
        internal static ConfigEntry<float> ThreatSettleSeconds;
        internal static ConfigEntry<bool> AutoResume;
        internal static ConfigEntry<float> StandoffMetres;
        internal static ConfigEntry<float> EgressSeconds;
        internal static ConfigEntry<bool> ReattackAfterEgress;

        internal static ConfigEntry<bool> ShowFlightStrip;
        internal static ConfigEntry<bool> ShowRecoveryTracks;

        internal static ConfigEntry<bool> FlightTrace;
        internal static ConfigEntry<bool> HarnessKeys;
        internal static ConfigEntry<KeyboardShortcut> ReportKey;
        internal static ConfigEntry<KeyboardShortcut> OrderNearestKey;
        internal static ConfigEntry<KeyboardShortcut> CeaseFireKey;
        internal static ConfigEntry<KeyboardShortcut> WaypointKey;

        internal static void Bind(ConfigFile config)
        {
            ResumeCommand = config.Bind("Command", "Resume command", new KeyboardShortcut(KeyCode.F10),
                "Re-enter command on the ship the camera is following. Command also resumes on its own " +
                "after a pause menu when Auto resume is on.");

            AutoResume = config.Bind("Command", "Auto resume", true,
                "Return to command automatically once gameplay is ready again, for example after closing " +
                "the pause menu, without having to reselect the ship.");

            DefaultAltitude = config.Bind("Flights", "Default altitude", 600f,
                new ConfigDescription("Altitude above ground a newly adopted flight holds, in metres.",
                    new AcceptableValueRange<float>(60f, 12000f)));

            DefaultAreaRadius = config.Bind("Flights", "Default task area radius", 3000f,
                new ConfigDescription("Radius of the area a flight works when first launched, in metres.",
                    new AcceptableValueRange<float>(500f, 60000f)));

            MinimumClearance = config.Bind("Flights", "Minimum ground clearance", 55f,
                new ConfigDescription("Never command a flight lower than this above the ground, whatever " +
                    "altitude is selected. The autopilot needs room to arrest a descent.",
                    new AcceptableValueRange<float>(20f, 500f)));

            ThreatSettleSeconds = config.Bind("Flights", "Threat settle time", 8f,
                new ConfigDescription("How long a flight's threat picture must stay clear before it resumes " +
                    "its task. Lower reacts sooner; too low and it bounces between fighting and flying.",
                    new AcceptableValueRange<float>(1f, 60f)));

            StandoffMetres = config.Bind("Flights", "Egress standoff", 12000f,
                new ConfigDescription("How far a flight opens from its target after releasing a weapon, " +
                    "before deciding whether to attack again. Larger keeps aircraft out of defences at the " +
                    "cost of slower repeat attacks.",
                    new AcceptableValueRange<float>(1000f, 40000f)));

            EgressSeconds = config.Bind("Flights", "Egress time limit", 45f,
                new ConfigDescription("Give up on opening to standoff after this long and decide anyway.",
                    new AcceptableValueRange<float>(5f, 180f)));

            ReattackAfterEgress = config.Bind("Flights", "Re-attack after egress", true,
                "Press the attack again once clear, while ordnance remains and the target lives. " +
                "Off means one pass per order.");

            ShowFlightStrip = config.Bind("Interface", "Flight strip", true,
                "Show airborne flights as chips along the command bar.");

            ShowRecoveryTracks = config.Bind("Interface", "Recovery tracks", true,
                "Draw aircraft in the pattern to recover on this deck.");

            FlightTrace = config.Bind("Diagnostics", "Flight trace", false,
                "Log each flight's task, destination bearing and range, and altitude every five seconds. " +
                "Useful for diagnosing a flight that will not go where it is sent.");

            HarnessKeys = config.Bind("Diagnostics", "Test harness keys", false,
                "Enable the keyboard test harness. Superseded by the command interface; kept for diagnosis.");

            ReportKey = config.Bind("Diagnostics", "Report key", new KeyboardShortcut(KeyCode.F6),
                "Dump the followed ship's state to the log.");
            OrderNearestKey = config.Bind("Diagnostics", "Order nearest key", new KeyboardShortcut(KeyCode.F7),
                "Order the best available weapon at the best available target.");
            CeaseFireKey = config.Bind("Diagnostics", "Cease fire key", new KeyboardShortcut(KeyCode.F8),
                "Cease fire on the followed ship.");
            WaypointKey = config.Bind("Diagnostics", "Waypoint ahead key", new KeyboardShortcut(KeyCode.F9),
                "Set a waypoint five kilometres off the bow.");
        }
    }
}

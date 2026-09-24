using BepInEx.Configuration;
using UnityEngine;

namespace NavalPower
{
    // Everything worth changing without a rebuild. Rendered by BepInEx
    // ConfigurationManager, and written to BepInEx/config on first run.
    internal static class Settings
    {
        internal static ConfigEntry<KeyboardShortcut> ResumeCommand;

        internal static ConfigEntry<float> DefaultFuel;
        internal static ConfigEntry<float> DefaultAltitude;
        internal static ConfigEntry<float> DefaultAreaRadius;
        internal static ConfigEntry<float> MinimumClearance;
        internal static ConfigEntry<float> ThreatSettleSeconds;
        internal static ConfigEntry<bool> AutoResume;
        internal static ConfigEntry<float> StandoffMetres;
        internal static ConfigEntry<float> EgressSeconds;
        internal static ConfigEntry<bool> ReattackAfterEgress;
        internal static ConfigEntry<bool> DeckTrace;
        internal static ConfigEntry<bool> LaunchCostFromAllocation;
        internal static ConfigEntry<bool> SortieBonusOnRecovery;
        internal static ConfigEntry<bool> CarrierApproachFix;
        internal static ConfigEntry<float> CarrierApproachFactor;
        internal static ConfigEntry<float> StrikePatience;
        internal static ConfigEntry<float> JammingStandoff;
        internal static ConfigEntry<float> EgressAltitude;
        internal static ConfigEntry<float> RadarHandover;
        internal static ConfigEntry<float> InfraredHandover;
        internal static ConfigEntry<float> FlareInterval;

        internal static ConfigEntry<int> DamageControlConcentration;
        internal static ConfigEntry<int> DamageControlRate;
        internal static ConfigEntry<bool> DamageControlPreserveCapacity;
        internal static ConfigEntry<float> ZoomSensitivity;
        internal static ConfigEntry<bool> TargetFeed;
        internal static ConfigEntry<int> FeedResolution;
        internal static ConfigEntry<float> FeedWidth;
        internal static ConfigEntry<float> FeedFieldOfView;
        internal static ConfigEntry<bool> ShowFlightStrip;
        internal static ConfigEntry<bool> ShowRecoveryTracks;

        internal static ConfigEntry<bool> FlightTrace;
        internal static ConfigEntry<bool> NavigationTrace;
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

            DefaultFuel = config.Bind("Flights", "Default fuel load", 1f,
                new ConfigDescription("Fraction of internal fuel a launch starts with. Some airframes " +
                    "default low enough that the pilot's own fuel check fails immediately and the flight " +
                    "turns straight back, so this defaults to full rather than to the airframe's figure.",
                    new AcceptableValueRange<float>(0.25f, 1f)));

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

            DeckTrace = config.Bind("Diagnostics", "Deck clearance trace", true,
                "Log how a landed aircraft leaves the deck: which of the game's two removal paths it takes, " +
                "and what the conditions were when the choice was made.");

            LaunchCostFromAllocation = config.Bind("Flights", "Launches cost your allocation", true,
                "You pay for every aircraft you launch, out of your own allocation, at its full value. " +
                "The faction supplies the airframe but no longer buys it. Off means launches are drawn " +
                "from faction reserves and faction funds, as they were.");

            SortieBonusOnRecovery = config.Bind("Flights", "Sortie bonus on recovery", true,
                "Recovering a flight pays the mission's successful sortie bonus, so bringing one home is " +
                "worth more than losing it. The game pays this only to an aircraft a player was flying " +
                "personally, so a commanded flight otherwise earns nothing by coming back.");

            CarrierApproachFix = config.Bind("Flights", "Slow deck approaches", true,
                "Approach a ship's deck more slowly than a runway. The game computes one landing speed for " +
                "both -- the branch meant to distinguish them returns the same number either way -- so " +
                "aircraft arrive at a flattop fast and high, fail to stabilise and go around repeatedly.");

            CarrierApproachFactor = config.Bind("Flights", "Deck approach factor", 0.75f,
                new ConfigDescription("Fraction of the normal approach speed used when recovering to a ship, " +
                    "for vertical-landing aircraft only -- the case the game's own branch was meant to " +
                    "distinguish and does not. Conventional aircraft keep the speed the game computed.",
                    new AcceptableValueRange<float>(0.4f, 1f)));

            StrikePatience = config.Bind("Flights", "Strike patience", 120f,
                new ConfigDescription("How long a flight may press an attack without anything leaving the " +
                    "rails before trying a different store, and then giving up. Long enough to reach the " +
                    "target and acquire it, short enough not to circle forever.",
                    new AcceptableValueRange<float>(20f, 600f)));

            JammingStandoff = config.Bind("Flights", "Jamming standoff", 18000f,
                new ConfigDescription("How far a jamming aircraft holds off the emitter it is suppressing. " +
                    "It never closes: the point of sending a jammer is that it works from outside.",
                    new AcceptableValueRange<float>(2000f, 60000f)));

            EgressAltitude = config.Bind("Flights", "Egress altitude", 200f,
                new ConfigDescription("Height above ground a flight runs at while leaving a target. " +
                    "Capped against the flight's ordered altitude, so a low flight does not climb to egress.",
                    new AcceptableValueRange<float>(60f, 3000f)));

            RadarHandover = config.Bind("Flights", "Radar shot handover", 15000f,
                new ConfigDescription("While egressing, keep running from a radar-guided shot until it is " +
                    "this close, then hand the aircraft to native evasion.",
                    new AcceptableValueRange<float>(1000f, 60000f)));

            InfraredHandover = config.Bind("Flights", "Heat-seeker handover", 2000f,
                new ConfigDescription("The same for a heat-seeking shot, which is let in far closer: flares " +
                    "work and the endgame is short.",
                    new AcceptableValueRange<float>(200f, 20000f)));

            FlareInterval = config.Bind("Flights", "Flare interval", 1.5f,
                new ConfigDescription("Seconds between flare releases while egressing with a heat-seeker " +
                    "inbound. The native pilot runs its own countermeasures once it has the aircraft.",
                    new AcceptableValueRange<float>(0.5f, 10f)));

            DamageControlRate = config.Bind("Damage control", "Work rate", 5,
                new ConfigDescription("How much faster damage control works on ships you command. The game's " +
                    "own rate dewaters a compartment in something like a thousand seconds, which is far " +
                    "longer than an engagement lasts, so nothing it does is visible at this pace.",
                    new AcceptableValueRange<int>(1, 20)));

            DamageControlPreserveCapacity = config.Bind("Damage control", "Preserve total capacity", true,
                "Charge the reserve for the extra work, so a faster crew gets through the same total amount " +
                "of damage control, just sooner. Turn this off to make damage control genuinely more capable " +
                "rather than merely quicker, at the cost of the game's own balance.");

            DamageControlConcentration = config.Bind("Damage control", "Concentration limit", 6,
                new ConfigDescription("How many extra shares of effort a prioritised compartment may take " +
                    "from the compartments being withheld. The ship's total capacity is unchanged either " +
                    "way; this caps how sharply it can be focused.",
                    new AcceptableValueRange<int>(1, 20)));

            ZoomSensitivity = config.Bind("Interface", "Zoom sensitivity", 4f,
                new ConfigDescription("Degrees of field of view per wheel notch when zooming the world view " +
                    "while in command. The wheel still zooms the map when the map is open, and a camera " +
                    "feed under the cursor takes it instead.",
                    new AcceptableValueRange<float>(0.5f, 20f)));

            TargetFeed = config.Bind("Interface", "Target feed", true,
                "Show a camera view of whatever the ship is engaging, and of our weapons while they fly. " +
                "Costs a second camera render; turn it off if it hurts the frame rate.");

            FeedResolution = config.Bind("Interface", "Target feed resolution", 480,
                new ConfigDescription("Render width of the feed in pixels. Height follows at sixteen by nine.",
                    new AcceptableValueRange<int>(160, 1920)));

            FeedWidth = config.Bind("Interface", "Target feed size", 420f,
                new ConfigDescription("On-screen width of the feed panel.",
                    new AcceptableValueRange<float>(200f, 900f)));

            FeedFieldOfView = config.Bind("Interface", "Target feed field of view", 35f,
                new ConfigDescription("Narrower reads like a sensor feed; wider shows more context.",
                    new AcceptableValueRange<float>(10f, 80f)));

            ShowFlightStrip = config.Bind("Interface", "Flight strip", true,
                "Show airborne flights as chips along the command bar.");

            ShowRecoveryTracks = config.Bind("Interface", "Recovery tracks", true,
                "Draw aircraft in the pattern to recover on this deck.");

            FlightTrace = config.Bind("Diagnostics", "Flight trace", false,
                "Log each flight's task, destination bearing and range, and altitude every five seconds. " +
                "Useful for diagnosing a flight that will not go where it is sent.");

            NavigationTrace = config.Bind("Diagnostics", "Navigation trace", false,
                "Log the commanded ship's route state, ordered against actual speed, throttle and whether " +
                "the native controller is being held off its own choices. For diagnosing a ship that will " +
                "not follow the course it was given.");

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

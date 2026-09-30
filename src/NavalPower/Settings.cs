using BepInEx.Configuration;
using UnityEngine;
using NOrders;

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
        internal static ConfigEntry<bool> ClaimAuthority;
        internal static ConfigEntry<bool> CarrierApproachFix;
        internal static ConfigEntry<float> CarrierApproachFactor;
        internal static ConfigEntry<float> StrikePatience;
        internal static ConfigEntry<float> JammingStandoff;
        internal static ConfigEntry<float> EgressAltitude;
        internal static ConfigEntry<float> RadarHandover;
        internal static ConfigEntry<KeyboardShortcut> NextInForce, PreviousInForce;
        internal static ConfigEntry<float> CloseSpacing, CombatSpacing;
        internal static ConfigEntry<bool> EscortRetaliate, EscortIntercept;
        internal static ConfigEntry<float> BombingHeight;
        internal static ConfigEntry<float> CruiseThrottle;
        internal static ConfigEntry<bool> PreFlare;
        internal static ConfigEntry<float> PreFlareInterval, FlareReserve, IrBurstRange, IrBurstPause;
        internal static ConfigEntry<int> IrBurstFlares;

        internal static ConfigEntry<int> DamageControlConcentration;
        internal static ConfigEntry<int> DamageControlRate;
        internal static ConfigEntry<bool> DamageControlPreserveCapacity;
        internal static ConfigEntry<float> DamageControlRestock;
        internal static ConfigEntry<float> ZoomSensitivity;
        internal static ConfigEntry<bool> TargetFeed;
        internal static ConfigEntry<int> FeedResolution;
        internal static ConfigEntry<float> FeedWidth;
        internal static ConfigEntry<float> FeedLinger;
        internal static ConfigEntry<bool> FeedAutoOpen;
        internal static ConfigEntry<bool> FeedHoverPeek;
        internal static ConfigEntry<bool> ShowCompass;
        internal static ConfigEntry<float> FeedFieldOfView;
        internal static ConfigEntry<bool> ShowRecoveryTracks;

        internal static ConfigEntry<bool> FlightTrace;
        internal static ConfigEntry<bool> NavigationTrace;
        internal static ConfigEntry<bool> HarnessKeys;
        internal static ConfigEntry<bool> InterfaceTrace;
        internal static ConfigEntry<bool> OwnRadarEvasion;
        internal static ConfigEntry<bool> StandOnWhenCovered;
        internal static ConfigEntry<int> MissilesPerAirTarget, MissilesPerSurfaceTarget;
        internal static ConfigEntry<float> RadarEvasionFloor;
        internal static ConfigEntry<float> InterfaceScale;
        internal static ConfigEntry<float> LowFuelAlert;
        internal static ConfigEntry<bool> NameShips;
        internal static ConfigEntry<KeyboardShortcut> ReportKey;
        internal static ConfigEntry<KeyboardShortcut> OrderNearestKey;
        internal static ConfigEntry<KeyboardShortcut> CeaseFireKey;
        internal static ConfigEntry<KeyboardShortcut> WaypointKey;

        // Tuning and diagnostics: kept, but out of the way behind the
        // configuration manager's "Advanced settings" toggle.
        private static ConfigDescription Advanced(string description, AcceptableValueBase range = null) =>
            new ConfigDescription(description, range, new ConfigurationManagerAttributes { IsAdvanced = true });

        internal static void Bind(ConfigFile config)
        {
            NextInForce = config.Bind("Command", "Next ship in task force", new KeyboardShortcut(KeyCode.RightBracket),
                "Take command of the next ship in the task force.");
            PreviousInForce = config.Bind("Command", "Previous ship in task force", new KeyboardShortcut(KeyCode.LeftBracket),
                "Take command of the previous ship in the task force.");
            ResumeCommand = config.Bind("Command", "Resume command", new KeyboardShortcut(KeyCode.F10),
                "Re-enter command on the ship the camera is following. Command also resumes on its own " +
                "after a pause menu when Auto resume is on.");

            NameShips = config.Bind("Command", "Name ships automatically", true,
                "Give every ship a name from its side's registry (BMDF, PALN, MV), renamable. Off leaves ships " +
                "with the game's names; renames made while it was on are kept for when it is turned back on.");

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
                Advanced("Never command a flight lower than this above the ground, whatever " +
                    "altitude is selected. The autopilot needs room to arrest a descent.",
                    new AcceptableValueRange<float>(20f, 500f)));

            ThreatSettleSeconds = config.Bind("Flights", "Threat settle time", 8f,
                Advanced("How long a flight's threat picture must stay clear before it resumes " +
                    "its task. Lower reacts sooner; too low and it bounces between fighting and flying.",
                    new AcceptableValueRange<float>(1f, 60f)));

            StandoffMetres = config.Bind("Flights", "Egress standoff", 12000f,
                new ConfigDescription("How far a flight opens from its target after releasing a weapon, " +
                    "before deciding whether to attack again. Larger keeps aircraft out of defences at the " +
                    "cost of slower repeat attacks.",
                    new AcceptableValueRange<float>(1000f, 40000f)));

            EgressSeconds = config.Bind("Flights", "Egress time limit", 45f,
                Advanced("Give up on opening to standoff after this long and decide anyway.",
                    new AcceptableValueRange<float>(5f, 180f)));

            ReattackAfterEgress = config.Bind("Flights", "Re-attack after egress", true,
                "Press the attack again once clear, while ordnance remains and the target lives. " +
                "Off means one pass per order.");

            ClaimAuthority = config.Bind("Flights", "Take ownership when flying a flight", true,
                Advanced("Claim network ownership of an aircraft while you fly it, and hand it back on release. " +
                "The game builds an aircraft's targeting camera only for an owned airframe, so without " +
                "this the cockpit's target view has nothing behind it. Turn off if ownership causes " +
                "trouble in multiplayer."));

            // A new key rather than the old "Deck and cockpit trace", which
            // defaulted on while launches were being debugged: a new key starts
            // everyone off.
            DeckTrace = config.Bind("Diagnostics", "Deck, cockpit and camera trace", false,
                Advanced("Log launches and recoveries in detail, taking the controls of a flight, cockpit " +
                "displays, and the life of an aircraft's targeting camera."));

            LaunchCostFromAllocation = config.Bind("Flights", "Launches cost your allocation", true,
                "You pay for every aircraft you launch, out of your own allocation, at its full value. " +
                "The faction supplies the airframe but no longer buys it. Off means launches are drawn " +
                "from faction reserves and faction funds, as they were.");

            SortieBonusOnRecovery = config.Bind("Flights", "Sortie bonus on recovery", true,
                "Recovering a flight pays the mission's successful sortie bonus, so bringing one home is " +
                "worth more than losing it. The game pays this only to an aircraft a player was flying " +
                "personally, so a commanded flight otherwise earns nothing by coming back.");

            CarrierApproachFix = config.Bind("Flights", "Slow deck approaches", true,
                Advanced("Approach a ship's deck more slowly than a runway. The game computes one landing speed for " +
                "both -- the branch meant to distinguish them returns the same number either way -- so " +
                "aircraft arrive at a flattop fast and high, fail to stabilise and go around repeatedly."));

            CarrierApproachFactor = config.Bind("Flights", "Deck approach factor", 0.75f,
                Advanced("Fraction of the normal approach speed used when recovering to a ship, " +
                    "for vertical-landing aircraft only -- the case the game's own branch was meant to " +
                    "distinguish and does not. Conventional aircraft keep the speed the game computed.",
                    new AcceptableValueRange<float>(0.4f, 1f)));

            StrikePatience = config.Bind("Flights", "Strike patience", 120f,
                Advanced("How long a flight may press an attack without anything leaving the " +
                    "rails before trying a different store, and then giving up. Long enough to reach the " +
                    "target and acquire it, short enough not to circle forever.",
                    new AcceptableValueRange<float>(20f, 600f)));

            JammingStandoff = config.Bind("Flights", "Jamming standoff", 18000f,
                Advanced("A jamming flight works from as far off as its pods still jam well, on its own side " +
                    "of the targets; this is the distance used only when the pods don't say how far they reach.",
                    new AcceptableValueRange<float>(2000f, 60000f)));

            EgressAltitude = config.Bind("Flights", "Egress altitude", 200f,
                new ConfigDescription("Height above ground a flight runs at while leaving a target. " +
                    "Capped against the flight's ordered altitude, so a low flight does not climb to egress.",
                    new AcceptableValueRange<float>(60f, 3000f)));

            RadarHandover = config.Bind("Flights", "Radar shot handover", 15000f,
                Advanced("While egressing, keep running from a radar-guided shot until it is " +
                    "this close, then hand the aircraft to native evasion.",
                    new AcceptableValueRange<float>(1000f, 60000f)));

            CloseSpacing = config.Bind("Wings", "Transit spacing", 200f,
                "Metres between wingmen in the close route formation flown in transit.");
            // 120 m was the first default and proved too tight; move a config
            // still on it to the new one.
            if (Mathf.Approximately(CloseSpacing.Value, 120f)) CloseSpacing.Value = 200f;
            CombatSpacing = config.Bind("Wings", "Combat spacing", 1600f,
                "Metres between wingmen in combat spread, which the wing opens into when threatened, attacking " +
                "or escaping, and holds for 20 s after it goes quiet.");
            EscortRetaliate = config.Bind("Wings", "Escorts retaliate on locks", true,
                "An escort attacks a radar that locks the aircraft it escorts: an anti-radiation shot down the " +
                "lock's bearing, or a strike on a tracked emitter.");
            EscortIntercept = config.Bind("Wings", "Escorts intercept missiles", true,
                "An escort fires an air-to-air missile (heat-seeking first, then active radar) at a missile fired " +
                "on the aircraft it escorts, when it can reach it in time.");
            LowFuelAlert = config.Bind("Flights", "Low fuel alert", 25f,
                new ConfigDescription("Fuel percentage below which a flight is marked as needing attention, " +
                    "until it is heading home. Raise it when the deck is far from the fight.",
                    new AcceptableValueRange<float>(5f, 60f)));
            BombingHeight = config.Bind("Flights", "Bombing height", 1500f,
                "Height above ground a level-bombing run is flown at. Lower is more accurate and more exposed.");
            CruiseThrottle = config.Bind("Flights", "Cruise throttle", 0.8f,
                "Throttle for jets on area, route, station and jamming tasks. Strike run-ins and egress use full " +
                "power. A wing's lead cruises 5% below this so its wingmen can close up, and slows further while " +
                "they are well behind.");
            OwnRadarEvasion = config.Bind("Flights", "Own radar evasion", true,
                "A radar-guided shot at one of our flights is flown off by our own logic: full power, the shot put " +
                "on the beam, chaff in bursts, and a gentle descent. Off leaves it to the game's own evasion, which " +
                "dives for the deck -- too hard for heavily loaded aircraft, which went into the sea.");
            StandOnWhenCovered = config.Bind("Flights", "Hold the task when covered", true,
                "A flight shot at keeps to its orders instead of evading while it can defend itself where it is: " +
                "a jamming pod on every radar-guided missile coming at it, and flares left for any heat-seeker. " +
                "A radar missile still coming inside 2.5 km is evaded regardless. Off, every shot is evaded.");
            MissilesPerAirTarget = config.Bind("Flights", "Missiles per air target", 2,
                new ConfigDescription("Most of our missiles closing on one aircraft at once; further launches at it are held. " +
                    "A flight's own setting (its rules & weapons page) overrides this.", new AcceptableValueRange<int>(1, 8)));
            MissilesPerSurfaceTarget = config.Bind("Flights", "Missiles per surface target", 0,
                new ConfigDescription("Most of our missiles closing on one ship, vehicle or building at once. 0 is automatic: the game's " +
                    "own estimate of the hits it needs, at most four. A flight's own setting overrides this.", new AcceptableValueRange<int>(0, 8)));
            RadarEvasionFloor = config.Bind("Flights", "Radar evasion floor", 250f,
                Advanced("Height above ground that our radar evasion's descent stops at.",
                    new AcceptableValueRange<float>(50f, 3000f)));
            IrBurstRange = config.Bind("Flights", "Heat-seeker flare range", 3000f,
                "A heat-seeking shot inside this range is met with strings of flares, whatever the flight is " +
                "doing. The engine goes to idle and the aircraft turns the missile onto its beam as soon as " +
                "one is fired; an attack run is held through it.");
            IrBurstFlares = config.Bind("Flights", "Flares per string", 4,
                "Flares in each string released against a heat-seeking shot. A string that does not shake it " +
                "is followed by another after the flare string pause, while flares last.");
            IrBurstPause = config.Bind("Flights", "Flare string pause", 1.5f,
                Advanced("Seconds between strings of flares while a heat-seeker keeps coming.",
                    new AcceptableValueRange<float>(0.3f, 10f)));
            PreFlare = config.Bind("Flights", "Pre-flare near IR launchers", true,
                "On an attack run inside the reach of an IR launcher the faction knows about, release a flare " +
                "every few seconds, so a shot fired without warning meets flares already in the air.");
            PreFlareInterval = config.Bind("Flights", "Pre-flare interval", 2f,
                Advanced("Seconds between pre-emptive flares near known IR launchers."));
            FlareReserve = config.Bind("Flights", "Flare reserve", 0.3f,
                Advanced("Pre-flaring stops when the flares left fall to this fraction, keeping them for actual shots."));
            DamageControlRate = config.Bind("Damage control", "Work rate", 5,
                new ConfigDescription("How much faster damage control works on ships you command. The game's " +
                    "own rate dewaters a compartment in something like a thousand seconds, which is far " +
                    "longer than an engagement lasts, so nothing it does is visible at this pace.",
                    new AcceptableValueRange<int>(1, 20)));

            DamageControlPreserveCapacity = config.Bind("Damage control", "Preserve total capacity", true,
                Advanced("Charge the reserve for the extra work, so a faster crew gets through the same total amount " +
                "of damage control, just sooner. Turn this off to make damage control genuinely more capable " +
                "rather than merely quicker, at the cost of the game's own balance."));

            DamageControlRestock = config.Bind("Damage control", "Restock on resupply", 0.2f,
                new ConfigDescription("Share of a ship's full damage control reserve that each resupply delivered " +
                    "to it restores (the game itself never replenishes the reserve). 0 turns restocking off.",
                    new AcceptableValueRange<float>(0f, 1f)));

            DamageControlConcentration = config.Bind("Damage control", "Concentration limit", 6,
                Advanced("How many extra shares of effort a prioritised compartment may take " +
                    "from the compartments being withheld. The ship's total capacity is unchanged either " +
                    "way; this caps how sharply it can be focused.",
                    new AcceptableValueRange<int>(1, 20)));

            InterfaceScale = config.Bind("Interface", "Interface scale", 1f,
                new ConfigDescription("Size of the command interface -- windows, text, menus, the tool strip, " +
                    "the compass and camera feeds -- on top of the automatic scaling to screen size. " +
                    "1 is the normal size; above 1 is bigger, below fits more on screen.",
                    new AcceptableValueRange<float>(0.6f, 2f), new ConfigurationManagerAttributes { Order = 100 }));

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

            FeedLinger = config.Bind("Interface", "Target feed hold after destruction", 5f,
                new ConfigDescription("Seconds a feed holds on the spot after what it was watching is destroyed, " +
                    "so the destruction is seen rather than cut away from.",
                    new AcceptableValueRange<float>(0f, 15f)));

            FeedHoverPeek = config.Bind("Interface", "Camera peek on hover", true,
                "Show a small live picture of a contact in its hover card, after a moment's hover. " +
                "One more camera render while it is up.");
            ShowCompass = config.Bind("Interface", "Compass tape", true,
                "A heading tape across the top of the screen while commanding, with markers for the " +
                "ship's heading and next waypoint, the flight being tasked and the contact under the cursor.");

            FeedAutoOpen = config.Bind("Interface", "Open the target feed on launch", true,
                "Open the live feed when one of our weapons leaves the rails, unless it was closed by hand " +
                "during this spell of command.");

            FeedFieldOfView = config.Bind("Interface", "Target feed field of view", 35f,
                new ConfigDescription("Narrower reads like a sensor feed; wider shows more context.",
                    new AcceptableValueRange<float>(10f, 80f)));

            ShowRecoveryTracks = config.Bind("Interface", "Recovery tracks", true,
                "Draw aircraft in the pattern to recover on this deck.");

            FlightTrace = config.Bind("Diagnostics", "Flight trace", false,
                Advanced("Log each flight's task, destination bearing and range, and altitude every five seconds, and " +
                "the steps of its attacks: run-ins, re-attacks, join-ups, flares. Useful for diagnosing a " +
                "flight that will not go where it is sent."));

            NavigationTrace = config.Bind("Diagnostics", "Navigation trace", false,
                Advanced("Log the commanded ship's route state, ordered against actual speed, throttle and whether " +
                "the native controller is being held off its own choices, and each task-force escort's " +
                "station keeping. For diagnosing a ship that will not follow the course it was given."));

            InterfaceTrace = config.Bind("Diagnostics", "Interface trace", false,
                Advanced("Log map docking, night vision set-up and the name given to each ship."));

            HarnessKeys = config.Bind("Diagnostics", "Test harness keys", false,
                Advanced("Enable the keyboard test harness. Superseded by the command interface; kept for diagnosis."));

            ReportKey = config.Bind("Diagnostics", "Report key", new KeyboardShortcut(KeyCode.F6),
                Advanced("Dump the followed ship's state to the log."));
            OrderNearestKey = config.Bind("Diagnostics", "Order nearest key", new KeyboardShortcut(KeyCode.F7),
                Advanced("Order the best available weapon at the best available target."));
            CeaseFireKey = config.Bind("Diagnostics", "Cease fire key", new KeyboardShortcut(KeyCode.F8),
                Advanced("Cease fire on the followed ship."));
            WaypointKey = config.Bind("Diagnostics", "Waypoint ahead key", new KeyboardShortcut(KeyCode.F9),
                Advanced("Set a waypoint five kilometres off the bow."));

            // The shared code reads plain values; keep them in step.
            Sync();
            config.SettingChanged += (_, __) => Sync();
        }

        // Copies every entry the shared execution code (NOrders) reads into
        // its Tuning values.
        private static void Sync()
        {
            Tuning.DefaultFuel = DefaultFuel.Value;
            Tuning.DefaultAltitude = DefaultAltitude.Value;
            Tuning.DefaultAreaRadius = DefaultAreaRadius.Value;
            Tuning.MinimumClearance = MinimumClearance.Value;
            Tuning.ThreatSettleSeconds = ThreatSettleSeconds.Value;
            Tuning.StandoffMetres = StandoffMetres.Value;
            Tuning.EgressSeconds = EgressSeconds.Value;
            Tuning.ReattackAfterEgress = ReattackAfterEgress.Value;
            Tuning.LaunchCostFromAllocation = LaunchCostFromAllocation.Value;
            Tuning.SortieBonusOnRecovery = SortieBonusOnRecovery.Value;
            Tuning.CarrierApproachFix = CarrierApproachFix.Value;
            Tuning.CarrierApproachFactor = CarrierApproachFactor.Value;
            Tuning.StrikePatience = StrikePatience.Value;
            Tuning.JammingStandoff = JammingStandoff.Value;
            Tuning.EgressAltitude = EgressAltitude.Value;
            Tuning.RadarHandover = RadarHandover.Value;
            Tuning.LowFuelAlert = LowFuelAlert.Value;
            Tuning.BombingHeight = BombingHeight.Value;
            Tuning.CruiseThrottle = CruiseThrottle.Value;
            Tuning.IrBurstRange = IrBurstRange.Value;
            Tuning.IrBurstFlares = IrBurstFlares.Value;
            Tuning.PreFlare = PreFlare.Value;
            Tuning.PreFlareInterval = PreFlareInterval.Value;
            Tuning.FlareReserve = FlareReserve.Value;
            Tuning.IrBurstPause = IrBurstPause.Value;
            Tuning.OwnRadarEvasion = OwnRadarEvasion.Value;
            Tuning.StandOnWhenCovered = StandOnWhenCovered.Value;
            Tuning.MissilesPerAirTarget = MissilesPerAirTarget.Value;
            Tuning.MissilesPerSurfaceTarget = MissilesPerSurfaceTarget.Value;
            Tuning.RadarEvasionFloor = RadarEvasionFloor.Value;
            Tuning.CloseSpacing = CloseSpacing.Value;
            Tuning.CombatSpacing = CombatSpacing.Value;
            Tuning.EscortRetaliate = EscortRetaliate.Value;
            Tuning.EscortIntercept = EscortIntercept.Value;
            Tuning.DamageControlRate = DamageControlRate.Value;
            Tuning.DamageControlPreserveCapacity = DamageControlPreserveCapacity.Value;
            Tuning.DamageControlConcentration = DamageControlConcentration.Value;
            Tuning.DamageControlRestock = DamageControlRestock.Value;
            Tuning.NameShips = NameShips.Value;
            Tuning.DeckTrace = DeckTrace.Value;
            Tuning.FlightTrace = FlightTrace.Value;
            Tuning.NavigationTrace = NavigationTrace.Value;
            Tuning.InterfaceTrace = InterfaceTrace.Value;
        }
    }
}

using System;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace NavalPower
{
    // Taking the controls of a flight we launched, and giving them back.
    //
    // The game already knows how to seat a player in an aircraft: it does it
    // once, in Aircraft.SetupLocalPlayerAndUI, for an airframe that spawned
    // belonging to the local player. That routine is the model here, but it is
    // not called: it was written for something that happens once in an
    // aircraft's life, so it creates HUD objects nobody ever expects to have to
    // remove again. Mounting and dismounting repeatedly means owning every one
    // of those objects, which means building them here where they can be taken
    // back down.
    //
    // What the aircraft does for a living does not change while we fly it. It
    // stays in its flight, keeps its task, keeps its place on the map; only the
    // pilot changes. Handing back puts the task straight back into effect.
    internal static class PilotSeat
    {
        private static readonly FieldInfo AircraftStatusDisplay =
            AccessTools.Field(typeof(Aircraft), "statusDisplay");
        private static readonly FieldInfo HudThreatList =
            AccessTools.Field(typeof(CombatHUD), "threatList");
        private static readonly MethodInfo ThreatListRelease =
            AccessTools.Method(typeof(ThreatList), "ThreatList_OnAircraftDisable");

        internal static string Report() =>
            "pilot seat:" +
            "\n  " + (AircraftStatusDisplay != null ? "ok      " : "MISSING ") + "Aircraft.statusDisplay" +
            "\n  " + (HudThreatList != null ? "ok      " : "MISSING ") + "CombatHUD.threatList" +
            "\n  " + (ThreatListRelease != null ? "ok      " : "MISSING ") + "ThreatList.ThreatList_OnAircraftDisable";

        internal static Flight Flying { get; private set; }
        internal static bool Active => Flying != null;

        private static Ship home;
        private static string flyingName;
        private static int bindDisplaysFrame;
        private static GameObject hudExtras;
        private static StatusDisplay statusDisplay;

        internal static bool CanTake(Flight flight, out string reason)
        {
            reason = null;
            if (flight == null || flight.Aircraft == null || flight.Aircraft.disabled)
            { reason = "That flight is gone."; return false; }
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null)
            { reason = "No local player to put in the seat."; return false; }
            if (player.Aircraft != null)
            { reason = "You are already flying something."; return false; }
            Pilot crew = Seat(flight.Aircraft);
            if (crew == null || crew.dead || crew.ejected)
            { reason = "That aircraft has no pilot to relieve."; return false; }
            if (FlightOrders.StillLeaving(crew))
            { reason = "Wait until it is off the deck."; return false; }
            if (SceneSingleton<CameraStateManager>.i == null || SceneSingleton<CombatHUD>.i == null ||
                SceneSingleton<FlightHud>.i == null || SceneSingleton<DynamicMap>.i == null)
            { reason = "The flight interface is not up."; return false; }
            return true;
        }

        // The game seats a player in the first pilot station and nowhere else,
        // so that is the one being relieved.
        private static Pilot Seat(Aircraft aircraft) =>
            aircraft != null && aircraft.pilots != null && aircraft.pilots.Length > 0 ? aircraft.pilots[0] : null;

        internal static bool Take(Flight flight, out string reason)
        {
            if (Active) { reason = "Hand back the aircraft you are flying first."; return false; }
            if (!CanTake(flight, out reason)) return false;

            GameManager.GetLocalPlayer(out Player player);
            Aircraft aircraft = flight.Aircraft;
            Pilot crew = Seat(aircraft);
            home = flight.Parent;

            // Command mode ends the moment the camera moves, and it would hand
            // the ship's kill credit back on the way out. We are not giving the
            // ship up, only leaving its bridge for a while, so the credit stays.
            MapCommand.Instance?.LeaveForTakeover();

            // Ownership first: the cockpit camera and the HUD both decide what
            // to show by asking the player which aircraft is theirs.
            player.SetAircraft(aircraft);               // Player.Aircraft, and the kill credit
            aircraft.playerRef = new PlayerRef(player); // and the airframe's own idea of who flies it
            player.AttachToAircraft(aircraft);
            crew.playerControlled = true;
            crew.SwitchState(crew.playerState);

            // The combat HUD first, as the game does it: everything built
            // after this asks the HUD which aircraft it is showing, and one
            // built before it would ask while the answer is still nothing.
            SceneSingleton<CombatHUD>.i.SetAircraft(aircraft);
            SceneSingleton<DynamicMap>.i.SetFaction(aircraft.NetworkHQ);
            SceneSingleton<DynamicMap>.i.DeselectAllIcons();

            AircraftParameters parameters = aircraft.GetAircraftParameters();
            if (parameters != null && parameters.StatusDisplay != null)
            {
                GameObject panel = UnityEngine.Object.Instantiate(parameters.StatusDisplay, Vector3.zero, Quaternion.identity);
                statusDisplay = panel.GetComponent<StatusDisplay>();
                if (statusDisplay != null)
                {
                    statusDisplay.Initialize(aircraft);
                    AircraftStatusDisplay?.SetValue(aircraft, statusDisplay);
                }
            }
            if (parameters != null && parameters.HUDExtras != null)
                hudExtras = UnityEngine.Object.Instantiate(parameters.HUDExtras, SceneSingleton<FlightHud>.i.GetHUDCenter());

            var cameras = SceneSingleton<CameraStateManager>.i;
            cameras.SetFollowingUnit(aircraft);
            cameras.SwitchState(cameras.cockpitState);
            SceneSingleton<DynamicMap>.i.Maximize();
            SceneSingleton<DynamicMap>.i.Minimize();
            DynamicMap.EnableCanvas(enable: true);

            // Next frame, not now. These managers live under the flight HUD
            // canvas, which the cockpit camera has only just switched on, and
            // Unity runs Awake on activation but defers Start. Looking now
            // cannot tell a manager that has not started yet from one that
            // started badly, and those want opposite treatment.
            bindDisplaysFrame = Time.frameCount + 2;

            Flying = flight;
            flyingName = flight.Name;
            Plugin.Log.LogInfo("[seat] flying " + flight.Name + " · " + flight.Describe());
            // Our own feedback line lives on the command bar, which is exactly
            // what is not on screen from in here. The game has a place for
            // saying things to a pilot, so use that one.
            Say("You have the controls · " + Settings.ResumeCommand.Value.MainKey +
                " returns control, or use the map bar to hand it back");
            return true;
        }

        // Hand the aircraft back and the camera back to the ship. Handing back
        // is not the same as abandoning the sortie, so the standing task is set
        // here rather than left as whatever half-finished thing it was doing
        // when the controls changed hands: either back on station in its task
        // area, or straight home to the deck.
        internal static void Release(bool recoverToShip)
        {
            if (!Active) return;
            Flight flight = Flying;
            Aircraft aircraft = flight.Aircraft;
            Flying = null;

            if (recoverToShip) FlightOrders.RecoverToShip(flight);
            else FlightOrders.SetArea(flight, flight.OrbitCentre, flight.OrbitRadius);

            // Out of the seat before the seat is taken apart: leaving the
            // player state is what puts the flight HUD away and lets go of the
            // g-load simulation, and it has to do that while the pieces it
            // refers to are all still there.
            if (aircraft != null && !aircraft.disabled)
            {
                Pilot crew = Seat(aircraft);
                if (crew != null)
                {
                    crew.playerControlled = false;
                    // Never leave it without a pilot state, even for a frame:
                    // a state that writes no control inputs is a state that
                    // drops the aircraft. The native combat state always flies,
                    // whatever the airframe; the next tick swaps in whichever
                    // state the flight's standing task actually calls for.
                    PilotBaseState flying = FlightOrders.CombatStateFor(crew);
                    if (flying != null) crew.SwitchStateNew(flying);
                    else NavalPilotState.Install(crew, flight);
                }
                flight.Adopted = false;
                flight.Interrupted = false;
                flight.CargoSeeded = false;         // re-solve the approach from where it is now
            }

            Dismantle(aircraft);

            var cameras = SceneSingleton<CameraStateManager>.i;
            Ship ship = home != null && !home.disabled ? home : null;
            home = null;
            if (cameras != null) cameras.SetFollowingUnit(ship != null ? (Unit)ship : aircraft);
            Plugin.Log.LogInfo("[seat] handed " + flight.Name + " back · " + flight.Describe());
            CommandState.Say(flight.Name + (recoverToShip
                ? " · released and recovering to the ship"
                : " · released to its task area"));
        }

        // The MFD and HUD app managers bind themselves to whatever aircraft the
        // combat HUD is holding, once, in Start. That is written for a player
        // who arrives in an aircraft and leaves by parachute: bind at birth,
        // destroy at death, never rebind. Arriving in an aircraft that was
        // already flying means the managers have either never started, started
        // against nothing, or are still holding an airframe that is gone -- and
        // a manager holding the wrong aircraft shows nothing. Point them here.
        private static void BindCockpitDisplays(Aircraft aircraft)
        {
            Bind(SceneSingleton<HUDAppManager>.i, aircraft, "HUDAppManager");
            Bind(SceneSingleton<MFDAppManager>.i, aircraft, "MFDAppManager");
        }

        private static void Bind(MonoBehaviour manager, Aircraft aircraft, string what)
        {
            if (manager == null)
            {
                // It destroys itself with the aircraft it was showing, so this
                // is what an earlier sortie in the same session leaves behind.
                Plugin.Log.LogInfo("[seat] no " + what + " in the scene · its displays will stay dark");
                return;
            }
            try
            {
                Type type = manager.GetType();
                FieldInfo held = AccessTools.Field(type, "aircraft");
                FieldInfo appsField = AccessTools.Field(type, "apps");
                MethodInfo onDisable = AccessTools.Method(type, "HUDAppManager_OnUnitDisable");
                if (held == null || appsField == null)
                {
                    Plugin.Log.LogWarning("[seat] cannot bind " + what + "; its displays will stay dark");
                    return;
                }

                // By now Start has had its frame. Still holding nothing means
                // it ran at a moment when the combat HUD held nothing either --
                // which is what happens when the flight HUD canvas is first
                // switched on by anything other than a player getting into an
                // aircraft -- and threw on the line after, leaving its displays
                // bound to nothing for the rest of the session.
                var bound = held.GetValue(manager) as Aircraft;
                if (bound == aircraft)
                { Plugin.Log.LogInfo("[seat] " + what + " already on this aircraft"); return; }

                // Its teardown hook follows the aircraft it is showing.
                var teardown = onDisable != null
                    ? (Action<Unit>)Delegate.CreateDelegate(typeof(Action<Unit>), manager, onDisable) : null;
                if (teardown != null && bound != null) bound.onDisableUnit -= teardown;
                held.SetValue(manager, aircraft);
                if (teardown != null) aircraft.onDisableUnit += teardown;

                if (!(appsField.GetValue(manager) is Array apps))
                {
                    Plugin.Log.LogWarning("[seat] " + what + " has no apps to bind");
                    return;
                }
                int rebound = 0;
                foreach (object app in apps)
                {
                    if (!(app is HUDApp page)) continue;
                    page.Initialize(aircraft);
                    page.RefreshSettings();
                    rebound++;
                }
                Plugin.Log.LogInfo("[seat] " + what + " · " + rebound + " display(s) bound, from " +
                    (bound == null ? "nothing" : bound.definition?.unitName ?? bound.name));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[seat] could not bind " + what + ": " + ex.Message);
            }
        }

        // The missile alarm is a looping AudioSource that the threat list adds
        // to the aircraft itself, started when a missile is seen and stopped
        // only when the last one goes away -- or destroyed when the aircraft
        // is. Nothing stops it when a player merely leaves, because natively a
        // player leaves an aircraft by dying in it. Hand back while something
        // is still tracking you and the tone goes with the aircraft, wailing,
        // for the rest of the mission. The threat list has a teardown for
        // exactly this; it is simply only ever reached by a death.
        private static void ReleaseThreatAlarms(Aircraft aircraft)
        {
            if (aircraft == null || HudThreatList == null || ThreatListRelease == null) return;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null) return;
            try
            {
                if (!(HudThreatList.GetValue(hud) is ThreatList threats)) return;
                ThreatListRelease.Invoke(threats, new object[] { aircraft });
                // Its own teardown does not unhook the event that calls it, so
                // without this the aircraft's eventual death runs it again.
                var teardown = (Action<Unit>)Delegate.CreateDelegate(
                    typeof(Action<Unit>), threats, (MethodInfo)ThreatListRelease);
                aircraft.onDisableUnit -= teardown;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[seat] could not silence the missile alarm: " + ex.Message);
            }
        }

        // The cockpit's own message line.
        private static void Say(string message)
        {
            var report = SceneSingleton<AircraftActionsReport>.i;
            if (report != null) report.ReportText(message, 8f);
        }

        // Everything Take built, taken down in the order the native eject path
        // takes its own down.
        private static void Dismantle(Aircraft aircraft)
        {
            ReleaseThreatAlarms(aircraft);
            if (SceneSingleton<CombatHUD>.i != null) SceneSingleton<CombatHUD>.i.RemoveAircraft();

            // CombatHUD.SetAircraft builds one of these every time and nothing
            // removes it until the aircraft dies, so a second sortie would
            // stack a second report on the first -- and it is a scene singleton.
            var report = SceneSingleton<AircraftActionsReport>.i;
            if (report != null) UnityEngine.Object.Destroy(report.gameObject);

            // Its damage alert source is added to the cockpit body rather than
            // to the panel, so one is left behind per sortie. Silent, idle, and
            // not ours to go hunting for among the aircraft's own audio.
            if (statusDisplay != null) UnityEngine.Object.Destroy(statusDisplay.gameObject);
            if (aircraft != null) AircraftStatusDisplay?.SetValue(aircraft, null);
            statusDisplay = null;

            if (hudExtras != null) UnityEngine.Object.Destroy(hudExtras);
            hudExtras = null;

            if (GameManager.GetLocalPlayer(out Player player) && player != null && aircraft != null)
                player.RemoveAircraft(aircraft);
            if (aircraft != null) aircraft.playerRef = PlayerRef.Invalid;

            // PilotPlayerState adds one of these on entry and never takes it
            // off, because natively there is no way back out of the seat.
            Pilot crew = Seat(aircraft);
            if (crew != null)
                foreach (GLOC gloc in crew.GetComponents<GLOC>()) UnityEngine.Object.Destroy(gloc);

            FlightHud.EnableCanvas(enable: false);
        }

        // The seat can also be lost natively -- shot down, or ejected from --
        // in which case the game has already torn its own side down and all
        // that is left is to stop believing we are flying.
        internal static void Tick()
        {
            if (!Active) return;
            if (bindDisplaysFrame > 0 && Time.frameCount >= bindDisplaysFrame)
            {
                bindDisplaysFrame = 0;
                BindCockpitDisplays(Flying.Aircraft);
            }
            Aircraft aircraft = Flying.Aircraft;
            bool lost = aircraft == null || aircraft.disabled ||
                !GameManager.GetLocalPlayer(out Player player) || player == null ||
                player.Aircraft != aircraft;
            if (!lost) return;

            Plugin.Log.LogInfo("[seat] " + (flyingName ?? "flight") + " · seat lost, the game has it now");
            Flying = null;
            home = null;
            statusDisplay = null;
            hudExtras = null;
            bindDisplaysFrame = 0;
        }
    }
}

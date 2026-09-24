using System;
using System.Collections.Generic;
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
        private static readonly MethodInfo ThreatListWarn =
            AccessTools.Method(typeof(ThreatList), "ThreatList_OnMissileWarning");
        private static readonly MethodInfo CockpitBuild =
            AccessTools.Method(typeof(Cockpit), "Cockpit_OnAircraftInitialize");
        private static readonly MethodInfo CockpitTearDown =
            AccessTools.Method(typeof(Cockpit), "Cockpit_OnAircraftDisable");
        private static readonly FieldInfo CockpitTacScreen = AccessTools.Field(typeof(Cockpit), "tacScreen");
        private static readonly FieldInfo CockpitAircraft = AccessTools.Field(typeof(Cockpit), "aircraft");
        private static readonly FieldInfo TargetCamMode = AccessTools.Field(typeof(TargetCam), "currentMode");
        private static readonly FieldInfo TargetCamCamera = AccessTools.Field(typeof(TargetCam), "cam");

        internal static string Report() =>
            "pilot seat:" +
            "\n  " + (AircraftStatusDisplay != null ? "ok      " : "MISSING ") + "Aircraft.statusDisplay" +
            "\n  " + (HudThreatList != null ? "ok      " : "MISSING ") + "CombatHUD.threatList" +
            "\n  " + (ThreatListRelease != null ? "ok      " : "MISSING ") + "ThreatList.ThreatList_OnAircraftDisable" +
            "\n  " + (ThreatListWarn != null ? "ok      " : "MISSING ") + "ThreatList.ThreatList_OnMissileWarning" +
            "\n  " + (CockpitBuild != null ? "ok      " : "MISSING ") + "Cockpit.Cockpit_OnAircraftInitialize" +
            "\n  " + (CockpitTearDown != null ? "ok      " : "MISSING ") + "Cockpit.Cockpit_OnAircraftDisable" +
            "\n  " + (CockpitTacScreen != null ? "ok      " : "MISSING ") + "Cockpit.tacScreen" +
            "\n  " + (CockpitAircraft != null ? "ok      " : "MISSING ") + "Cockpit.aircraft" +
            "\n  " + (TargetCamMode != null ? "ok      " : "MISSING ") + "TargetCam.currentMode" +
            "\n  " + (TargetCamCamera != null ? "ok      " : "MISSING ") + "TargetCam.cam";

        internal static Flight Flying { get; private set; }
        internal static bool Active => Flying != null;

        private static Ship home;
        private static string flyingName;
        private static int bindDisplaysFrame;
        private static bool builtScreens;
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
            AnnounceExistingThreats(aircraft);
            // SetAircraft does not show the current weapon: the readout is
            // built by ShowWeaponStation, which the game calls when a weapon is
            // selected and when an aircraft arms itself at spawn -- and that
            // second one is skipped for any aircraft the combat HUD is not
            // already showing, which an AI aircraft never is. So the panel
            // still described whatever was in it before, until cycling a
            // weapon built it again.
            if (aircraft.weaponManager != null)
                SceneSingleton<CombatHUD>.i.ShowWeaponStation(aircraft.weaponManager.currentWeaponStation);
            BuildCockpitScreens(aircraft);
            ResetTargetCamera(aircraft);
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
            // Never two at once, whatever happened to the last one.
            if (hudExtras != null) UnityEngine.Object.Destroy(hudExtras);
            hudExtras = null;
            if (parameters != null && parameters.HUDExtras != null)
                hudExtras = UnityEngine.Object.Instantiate(parameters.HUDExtras, SceneSingleton<FlightHud>.i.GetHUDCenter());

            var cameras = SceneSingleton<CameraStateManager>.i;
            cameras.SetFollowingUnit(aircraft);
            cameras.SwitchState(cameras.cockpitState);
            // The map object itself starts inactive -- DynamicMap.Awake turns
            // it off -- and Maximize activates a canvas *inside* it. Activating
            // a child of an inactive object does not make it live, and a
            // component that never goes live never runs its Awake. So the map
            // is switched on before it is asked to open, not after.
            DynamicMap.EnableCanvas(enable: true);
            SceneSingleton<DynamicMap>.i.Maximize();
            SceneSingleton<DynamicMap>.i.Minimize();
            DynamicMap.EnableCanvas(enable: true);

            // The map dance above is what the game does on spawning into an
            // aircraft, and maximizing the map is one of the things that turns
            // the flight HUD off. It turns it back on again on the way down,
            // but only from states it believes it left -- so the HUD is asked
            // for once more here, where nothing follows to countermand it.
            FlightHud.EnableCanvas(enable: true);
            Plugin.Log.LogInfo("[seat] cockpit up · camera " + CameraStateManager.cameraMode +
                " · map " + (DynamicMap.mapMaximized ? "maximized" : "minimized") +
                " · combat HUD on " + (SceneSingleton<CombatHUD>.i?.aircraft == aircraft ? "this aircraft" : "something else"));
            ReportHud(aircraft);

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

        // The target camera only announces itself on a change. SetTargetCam
        // raises its event inside `if (!cam.enabled)`, and refuses outright
        // while the camera is in landing mode, which gear extension puts it in
        // and only gear retraction or a touchdown takes it out of. An aircraft
        // that has been flying itself since it left the deck can therefore be
        // holding either state, and a tac screen built a moment ago has heard
        // nothing either way -- so the panel never switches to the target view,
        // however many targets are selected afterwards.
        //
        // Handing the camera over in a known state is what the game itself does
        // on touchdown: mode forward, camera off. The combat HUD asks for the
        // target camera every frame there is a target, so the next frame turns
        // it back on properly and the screen hears about it.
        private static void ResetTargetCamera(Aircraft aircraft)
        {
            if (aircraft == null || TargetCamMode == null || TargetCamCamera == null) return;
            TargetCam view = aircraft.targetCam;
            if (view == null)
            {
                // Two different failures read the same here. A reference that
                // is genuinely null means TargetCam.Initialize never ran -- it
                // is gated on the aircraft having client authority, which an AI
                // aircraft is not given. One that is merely Unity-null is a
                // camera that existed and has since been destroyed.
                bool never = ReferenceEquals(aircraft.targetCam, null);
                TargetCam part = FindTargetCam(aircraft);
                Plugin.Log.LogInfo("[seat] target camera " + (never ? "was never built" : "has been destroyed") +
                    " · component on the airframe: " + (part == null ? "none" : "yes") +
                    " · authority " + (aircraft.Identity != null && aircraft.Identity.HasAuthority));
                if (part == null) return;
                // Its own initialiser is the only thing that builds the lenses,
                // and it will decline again if the authority is still not ours.
                part.Initialize();
                view = aircraft.targetCam;
                Plugin.Log.LogInfo("[seat] target camera " + (view == null ? "still not built" : "built"));
                if (view == null) return;
            }
            try
            {
                object mode = TargetCamMode.GetValue(view);
                var lens = TargetCamCamera.GetValue(view) as Camera;
                Plugin.Log.LogInfo("[seat] target camera was " + mode +
                    " · " + (lens == null ? "no lens" : lens.enabled ? "on" : "off"));
                TargetCamMode.SetValue(view, TargetCam.CamMode.targetForward);
                if (lens != null) lens.enabled = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[seat] could not reset the target camera: " + ex.Message);
            }
        }

        // Like the cockpit, reached through the part rather than the hierarchy.
        private static TargetCam FindTargetCam(Aircraft aircraft)
        {
            foreach (TargetCam candidate in Resources.FindObjectsOfTypeAll<TargetCam>())
            {
                if (!candidate.gameObject.scene.IsValid()) continue;
                var attached = AccessTools.Field(typeof(TargetCam), "attachedPart")?.GetValue(candidate) as UnitPart;
                if (attached != null && attached.parentUnit == aircraft) return candidate;
            }
            return null;
        }

        // The physical panels in the cockpit -- the tac screen, and on a glass
        // cockpit that is most of the instrumentation -- are built once, when
        // the aircraft initialises, and only for an aircraft the combat HUD is
        // already showing:
        //
        //     if (CombatHUD.i != null && CombatHUD.i.aircraft == aircraft)
        //     { tacScreen = Instantiate(tacScreenUIPrefab, transform); ... }
        //     else base.enabled = false;
        //
        // An AI aircraft can never satisfy that at the moment it is born, so
        // its cockpit switches itself off and the screens are never made. There
        // is nothing to light up later, which is why they were dark whatever
        // was enabled around them. Arriving late means building them late: the
        // condition is true now, so the game's own routine is run now.
        private static void BuildCockpitScreens(Aircraft aircraft)
        {
            if (aircraft == null || CockpitBuild == null || CockpitTacScreen == null) return;
            Cockpit cockpit = CockpitOf(aircraft);
            if (cockpit == null) { Plugin.Log.LogInfo("[seat] this airframe has no cockpit screens"); return; }
            try
            {
                if (CockpitTacScreen.GetValue(cockpit) != null)
                {
                    cockpit.enabled = true;              // already built, just left off
                    Plugin.Log.LogInfo("[seat] cockpit screens already built");
                    return;
                }
                CockpitBuild.Invoke(cockpit, null);
                builtScreens = CockpitTacScreen.GetValue(cockpit) != null;
                Plugin.Log.LogInfo("[seat] cockpit screens " + (builtScreens ? "built" : "NOT built"));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[seat] could not build the cockpit screens: " + ex.Message);
            }
        }

        // Not by walking down from the airframe: a Vortex reports no Cockpit,
        // no TacScreen and no canvas at all beneath it, because an aircraft's
        // parts are separate bodies that can come off in flight rather than
        // children of its transform. The cockpit is reached through the part
        // the aircraft names, and failing that by asking every cockpit in the
        // scene which aircraft it belongs to.
        private static Cockpit CockpitOf(Aircraft aircraft)
        {
            if (aircraft == null) return null;
            if (aircraft.cockpit != null)
            {
                Cockpit onPart = aircraft.cockpit.GetComponentInChildren<Cockpit>(includeInactive: true)
                    ?? aircraft.cockpit.GetComponentInParent<Cockpit>();
                if (onPart != null) return onPart;
            }
            if (CockpitAircraft == null) return null;
            foreach (Cockpit candidate in Resources.FindObjectsOfTypeAll<Cockpit>())
            {
                if (!candidate.gameObject.scene.IsValid()) continue;      // prefab assets
                if (CockpitAircraft.GetValue(candidate) as Aircraft == aircraft) return candidate;
            }
            return null;
        }

        // And taken down again, or a second sortie in the same airframe stacks
        // a second screen on the first.
        private static void RemoveCockpitScreens(Aircraft aircraft)
        {
            if (!builtScreens || aircraft == null || CockpitTearDown == null) return;
            builtScreens = false;
            Cockpit cockpit = CockpitOf(aircraft);
            if (cockpit == null) return;
            try
            {
                CockpitTearDown.Invoke(cockpit, new object[] { aircraft });
                CockpitTacScreen?.SetValue(cockpit, null);
                cockpit.enabled = false;
                // Its teardown destroys the screen but leaves itself subscribed.
                var handler = (Action<Unit>)Delegate.CreateDelegate(
                    typeof(Action<Unit>), cockpit, (MethodInfo)CockpitTearDown);
                aircraft.onDisableUnit -= handler;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[seat] could not remove the cockpit screens: " + ex.Message);
            }
        }

        // A threat that was already inbound never announces itself again. The
        // warning is an event, raised once, at the moment a missile crosses
        // into the aircraft's known list -- and an aircraft that has been under
        // fire for the last thirty seconds raised all of its warnings before we
        // were listening. Natively that is fine, because a player is in the
        // aircraft from the moment it exists. Arriving late means the threat
        // display starts empty and silent under missiles that are plainly on
        // the map. So each one already known is announced now, through the same
        // handler a live warning uses, which is what puts it on the display,
        // flashes its marker, flags it on the map and sounds the tone.
        private static void AnnounceExistingThreats(Aircraft aircraft)
        {
            if (aircraft == null || HudThreatList == null || ThreatListWarn == null) return;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null) return;
            try
            {
                if (!(HudThreatList.GetValue(hud) is ThreatList threats)) return;
                MissileWarning warning = aircraft.GetMissileWarningSystem();
                if (warning == null || warning.knownMissiles == null) return;

                int announced = 0;
                // Copied first: the handler reaches back into the threat list,
                // and the warning system is free to edit this list as it runs.
                var inbound = new List<Missile>(warning.knownMissiles);
                foreach (Missile missile in inbound)
                {
                    if (missile == null || missile.disabled) continue;
                    ThreatListWarn.Invoke(threats,
                        new object[] { new MissileWarning.OnMissileWarning { missile = missile } });
                    announced++;
                }
                if (announced > 0)
                    Plugin.Log.LogInfo("[seat] " + announced +
                        " missile(s) already inbound · put on the threat display");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[seat] could not show the threats already inbound: " + ex.Message);
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

        private static readonly FieldInfo FlightHudCanvas = AccessTools.Field(typeof(FlightHud), "canvas");

        // Everything asked for has been confirmed done and the HUD is still not
        // there, so this stops reporting what was requested and reports what is
        // actually on screen: whether the canvas object is live all the way up
        // its parents, whether the Canvas component itself is drawing, and
        // where each piece actually sits in the hierarchy. A GameObject can be
        // active and still invisible because something above it is not.
        private static void ReportHud(Aircraft aircraft)
        {
            try
            {
                FlightHud hud = SceneSingleton<FlightHud>.i;
                if (hud == null) { Plugin.Log.LogWarning("[hud] no FlightHud"); return; }
                Plugin.Log.LogInfo("[hud] FlightHud " + Describe(hud.gameObject));

                var canvas = FlightHudCanvas?.GetValue(hud) as Canvas;
                if (canvas == null) { Plugin.Log.LogWarning("[hud] FlightHud has no canvas field"); return; }
                Plugin.Log.LogInfo("[hud] canvas " + Describe(canvas.gameObject) +
                    " · component " + (canvas.enabled ? "enabled" : "DISABLED") +
                    " · order " + canvas.sortingOrder +
                    " · group alpha " + GroupAlpha(canvas.gameObject));

                var apps = SceneSingleton<HUDAppManager>.i;
                Plugin.Log.LogInfo("[hud] HUDAppManager " +
                    (apps == null ? "absent" : Describe(apps.gameObject)));

                // The singleton is only set by Awake, and Awake never runs for
                // an object that has never been live. This finds it anyway,
                // inactive or not, which is the one question three rounds of
                // guessing have not been able to answer: does the thing that
                // drives the cockpit panels exist at all, and if so, what is
                // switched off above it.
                foreach (MFDAppManager found in Resources.FindObjectsOfTypeAll<MFDAppManager>())
                {
                    if (!found.gameObject.scene.IsValid()) continue;     // skip the prefab assets
                    Plugin.Log.LogInfo("[hud] MFDAppManager found · " + Describe(found.gameObject) +
                        " · singleton " + (SceneSingleton<MFDAppManager>.i == null ? "NOT set" : "set"));
                }
                if (SceneSingleton<MFDAppManager>.i == null)
                    Plugin.Log.LogInfo("[hud] HUD extras for this airframe: " +
                        (hudExtras == null ? "none instantiated" : Describe(hudExtras)));
                Plugin.Log.LogInfo("[hud] status display: " +
                    (statusDisplay == null ? "none instantiated" : Describe(statusDisplay.gameObject)));

                // The panels themselves, rather than the manager that was
                // supposed to own them.
                foreach (MFDScreen screen in Resources.FindObjectsOfTypeAll<MFDScreen>())
                    if (screen.gameObject.scene.IsValid())
                        Plugin.Log.LogInfo("[hud] MFDScreen " + screen.shortName + " · " +
                            Describe(screen.gameObject) +
                            " · panel " + (screen.displayPanel == null ? "none"
                                : screen.displayPanel.activeInHierarchy ? "on" : "OFF"));
                foreach (VirtualMFD mfd in Resources.FindObjectsOfTypeAll<VirtualMFD>())
                    if (mfd.gameObject.scene.IsValid())
                        Plugin.Log.LogInfo("[hud] VirtualMFD " + Describe(mfd.gameObject));
                ReportAircraftScreens(aircraft);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[hud] could not report: " + ex.Message); }
        }

        // A glass cockpit's panels are drawn in the aircraft itself, not on
        // the flight HUD, so they are canvases somewhere under the airframe --
        // and a world-space canvas renders through a camera it is handed. One
        // that is null, or pointing at a camera that is off, draws nothing at
        // all while looking perfectly healthy from every other angle.
        private static void ReportAircraftScreens(Aircraft aircraft)
        {
            if (aircraft == null) return;
            Cockpit pit = CockpitOf(aircraft);
            Plugin.Log.LogInfo("[hud] Cockpit " + (pit == null ? "not found for this airframe"
                : Describe(pit.gameObject) + " · component " + (pit.enabled ? "enabled" : "DISABLED")));
            Plugin.Log.LogInfo("[hud] aircraft.cockpit part " +
                (aircraft.cockpit == null ? "null" : Describe(aircraft.cockpit.gameObject)));
            foreach (TacScreen screen in Resources.FindObjectsOfTypeAll<TacScreen>())
                if (screen.gameObject.scene.IsValid())
                    Plugin.Log.LogInfo("[hud] TacScreen " + Describe(screen.gameObject));
            int found = 0;
            foreach (Canvas screen in aircraft.GetComponentsInChildren<Canvas>(includeInactive: true))
            {
                found++;
                Plugin.Log.LogInfo("[hud] cockpit canvas " + Describe(screen.gameObject) +
                    " · " + screen.renderMode +
                    " · component " + (screen.enabled ? "enabled" : "DISABLED") +
                    " · camera " + (screen.worldCamera == null ? "NONE"
                        : screen.worldCamera.name + (screen.worldCamera.enabled ? "" : " (off)")));
            }
            Plugin.Log.LogInfo("[hud] " + found + " canvas(es) under the airframe · cockpit render camera " +
                (SceneSingleton<CameraStateManager>.i?.cockpitCamRender == null ? "none"
                    : SceneSingleton<CameraStateManager>.i.cockpitCamRender.enabled ? "on" : "OFF"));
        }

        private static string Describe(GameObject go)
        {
            string path = go.name;
            for (Transform parent = go.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path + (parent.gameObject.activeSelf ? "" : " (parent OFF)");
            return path + " · self " + (go.activeSelf ? "on" : "OFF") +
                " · in hierarchy " + (go.activeInHierarchy ? "on" : "OFF");
        }

        // A CanvasGroup anywhere above it can hide a perfectly active canvas.
        private static string GroupAlpha(GameObject go)
        {
            var found = go.GetComponentInParent<CanvasGroup>();
            return found == null ? "none" : found.name + " " + found.alpha.ToString("0.00");
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
            RemoveCockpitScreens(aircraft);
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
            // Dropping the references is not the same as taking the objects
            // down. The airframe's HUD extras are ours -- we instantiated them
            // onto the flight HUD -- and nothing in the game removes them,
            // because natively nobody gets into a second aircraft. Two lost
            // seats in a session left two more sets of instruments stacked on
            // the one in use, each drawing its own text over the others.
            if (hudExtras != null) UnityEngine.Object.Destroy(hudExtras);
            statusDisplay = null;
            hudExtras = null;
            bindDisplaysFrame = 0;
            builtScreens = false;
        }
    }

    // PilotPlayerState.EnterState adds a GLOC component without looking for one
    // already there, and LeaveState never removes it. Natively that is not a
    // leak, because natively a pilot enters the seat once. Getting in and out
    // repeatedly stacks one per sortie on the same pilot, each simulating the
    // same g-load.
    //
    // Clearing it on the way out, which is what this did before, misses the
    // ways out that do not go through us: shot down, or ejected from. Clearing
    // it on the way in cannot be missed, because the way in is the leak. Found
    // independently by NOAutopilot, which needs it for the same reason -- it
    // re-enters the seat after an automatic landing.
    [HarmonyPatch(typeof(PilotPlayerState), "EnterState")]
    internal static class GlocLeakPatch
    {
        private const string Name = "GLOC leak fix";

        private static void Prefix(Pilot __0)
        {
            if (!Guard.Ok(Name)) return;
            try
            {
                if (__0 == null) return;
                foreach (GLOC stale in __0.GetComponents<GLOC>()) UnityEngine.Object.Destroy(stale);
            }
            catch (Exception ex) { Guard.Failed(Name, ex); }
        }
    }
}

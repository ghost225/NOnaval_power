using NuclearOption.Networking;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NavalPower
{
    // Input ownership is explicit: a target click never also selects a new ship.
    internal enum SelectionAction { Native, Enter, Exit, Consume }
    internal enum RightClickAction { None, Menu, AttackTarget, MissingTarget, ReplaceWaypoint, AppendWaypoint }

    internal static class InputPolicy
    {
        internal static SelectionAction Select(bool active, bool sameUnit, bool eligible, bool suppressed)
        {
            if (active) return sameUnit ? SelectionAction.Consume : SelectionAction.Exit;
            return eligible && !suppressed ? SelectionAction.Enter : SelectionAction.Native;
        }

        internal static RightClickAction RightClick(bool active, bool onMap, bool unit, bool weapon, bool shift)
        {
            if (!active) return RightClickAction.None;
            if (unit) return weapon ? RightClickAction.AttackTarget : RightClickAction.Menu;
            // Selection owns the gesture: a rejected shot must never silently
            // become a navigation order.
            if (weapon) return RightClickAction.MissingTarget;
            if (!onMap) return RightClickAction.None;
            return shift ? RightClickAction.AppendWaypoint : RightClickAction.ReplaceWaypoint;
        }

        internal static bool NativeMouseDown(bool active, int button, bool actual) => actual && !(active && button == 1);
    }

    // Unity's pointer handlers fire on release, possibly several frames after
    // the press. Ownership has to survive the whole gesture and its release frame.
    internal sealed class PointerGesture
    {
        internal bool Claimed { get; private set; }
        private int releaseFrame = -1;

        internal void Update(int frame, bool down, bool held)
        {
            if (down) { Claimed = false; releaseFrame = -1; return; }
            if (!Claimed || held) return;
            if (releaseFrame < 0) releaseFrame = frame;
            else if (frame > releaseFrame) { Claimed = false; releaseFrame = -1; }
        }

        internal void Claim() { Claimed = true; releaseFrame = -1; }
    }

    // Admit only a fresh left press in unobstructed world space. Entering the
    // map or any UI, or losing the press, cancels it until the next one.
    internal sealed class CameraGesture
    {
        internal bool Dragging { get; private set; }

        internal void Update(bool down, bool held, bool ready, bool blocked)
        {
            if (!ready || !held || blocked) { Dragging = false; return; }
            if (down) Dragging = true;
        }
    }

    internal sealed class MapCommand : MonoBehaviour
    {
        internal static MapCommand Instance;

        // A private flag: never borrow or clear the map's or another menu's.
        private const CursorFlags CommandCursor = (CursorFlags)0x20000000;

        private int suppressEntryFrame = -1, inputFrame = -1, gestureFrame = -1;
        // The ship we were commanding, so command can be resumed after a pause
        // menu or a look at something else, rather than having to be re-found.
        private Ship lastCommanded;
        private Airbase lastField;
        private readonly PointerGesture leftGesture = new PointerGesture();
        private readonly CameraGesture cameraGesture = new CameraGesture();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>(32);
        private PointerEventData pointer;
        private EventSystem pointerEvents;
        private float pickedUnitDistance;

        private static readonly System.Reflection.FieldInfo DebugFollowing =
            AccessTools.Field(typeof(UnitDebug), "followingUnit");
        private CanvasGroup nativeBar;
        private bool addedNativeBar;
        private float nativeAlpha;
        private bool nativeInteractable, nativeBlocks;
        private Player creditedPlayer;

        internal Unit HoverUnit { get; private set; }
        internal EsmContact HoverEsm { get; private set; }
        private float pickedEsmDistance;
        internal CommandUi Ui;

        private void Awake() { Instance = this; }
        private void OnDestroy() { Leave(); if (Instance == this) Instance = null; }

        // Command is entered by following a ship, and every way it can decline
        // to start is a silent one. After a long session that ends with clicking
        // a ship and getting nothing, the difference between "the game is not in
        // a state for it", "this ship cannot be commanded" and "something of
        // ours is still holding the seat" matters, and none of them said
        // anything. Now the refusal explains itself.
        private static string WhyNotReady()
        {
            if (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer)
                return "game state is " + GameManager.gameState;
            var gameplay = SceneSingleton<GameplayUI>.i;
            if (gameplay == null) return "no gameplay UI";
            if (GameplayUI.GameIsPaused) return "the game is paused";
            if (MenuOpen()) return "a menu is open";
            if (GameManager.GetLocalAircraft(out Aircraft own) && own != null && !own.disabled)
                return "you still hold an aircraft (" + (own.definition?.unitName ?? own.name) + ")";
            if (SceneSingleton<CameraStateManager>.i == null) return "no camera manager";
            return null;
        }

        // The menus themselves, by the flags the game sets while each is up:
        // the pause and join menus, and the aircraft selection screen. Not the
        // menu canvas -- the selection screen switches that on and never off
        // again when you back out to the map, which left command refused with
        // "a menu is open" until the next pause and resume.
        private static bool MenuOpen() =>
            CursorManager.GetFlag(CursorFlags.GameMenu) || CursorManager.GetFlag(CursorFlags.SelectionMenu);

        private static bool GameplayReady()
        {
            if (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer) return false;
            var gameplay = SceneSingleton<GameplayUI>.i;
            if (gameplay == null || GameplayUI.GameIsPaused || MenuOpen()) return false;
            if (GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null && !aircraft.disabled) return false;
            return SceneSingleton<CameraStateManager>.i != null;
        }

        internal void FollowingChanged(Unit unit)
        {
            if (CommandState.Active)
            {
                if (unit == CommandState.Ship && unit != null) return;
                // An airfield is watched from a free camera, which follows
                // nothing: the map's own camera jumps land here too, and none
                // of them is a reason to give up the field.
                if (CommandState.Base != null && unit == null) return;
                lastField = null;
                Leave();
                // Same rule when the camera moves by some other route.
                if (unit is Ship next && CommandableShip.CanCommand(next, out _)) { Enter(next); return; }
                suppressEntryFrame = Time.frameCount;
                return;
            }
            if (Time.frameCount == suppressEntryFrame) return;
            if (!(unit is Ship ship)) return;

            string blocked = WhyNotReady();
            if (blocked != null)
            {
                Explain(ship, blocked);
                return;
            }
            if (!CommandableShip.CanCommand(ship, out string why)) { Explain(ship, why); return; }
            Enter(ship);
        }

        private string lastExplained;
        private float nextExplain;

        private void Explain(Ship ship, string why)
        {
            string line = ShipNames.Of(ship) + " · not taking command · " + why;
            if (line == lastExplained && Time.unscaledTime < nextExplain) return;
            lastExplained = line;
            nextExplain = Time.unscaledTime + 5f;
            Plugin.Log.LogInfo("[command] " + line);
            CommandState.Say(line);
        }

        internal void Enter(Ship ship)
        {
            if (!GameplayReady()) return;
            CommandState.Base = null;
            CommandState.Ship = ship;
            lastCommanded = ship;
            lastField = null;
            CommandState.SelectedKey = null;
            CommandState.Quantity = 1;
            Esm.Configure(ship);
            DamageControl.Adopt(ship);
            HideNativeBar(ship);
            CreditKillsToCommander(ship);
            CursorManager.SetFlag(CommandCursor, true);
            CommandState.Say("Command active · right-click map: waypoint · shift: append · right-click contact: menu");
        }

        // Command of a land airbase. There is no unit to follow, so the view
        // becomes a free camera over the field -- fly it anywhere with the
        // usual keys; command holds until the camera is sent to follow some
        // unit, the field changes hands, or command is left.
        internal void EnterAirfield(Airbase field)
        {
            string blocked = WhyNotReady();
            if (blocked == null && !Airfields.CanCommand(field, out string why)) blocked = why;
            if (blocked != null)
            {
                string line = Airfields.NameOf(field) + " · not taking command · " + blocked;
                Plugin.Log.LogInfo("[command] " + line);
                CommandState.Say(line);
                return;
            }
            Leave();
            // The camera first: moving it to a free view fires the follow
            // event, and that must not find a half-entered field.
            FrameAirfield(field);
            CommandState.Base = field;
            lastField = field;
            lastCommanded = null;
            CommandState.SelectedKey = null;
            CommandState.Quantity = 1;
            CursorManager.SetFlag(CommandCursor, true);
            Plugin.Log.LogInfo("[command] airfield · " + Airfields.NameOf(field));
            Airfields.Report(field);
            CommandState.Say("Airfield command · " + Airfields.NameOf(field) + " · AIR to launch · fly the view with the movement keys");
        }

        // Up and back from the field's centre, looking down across it.
        private static void FrameAirfield(Airbase field)
        {
            var cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null || field == null) return;
            Vector3 centre = field.center != null ? field.center.position : field.transform.position;
            float distance = Mathf.Clamp(field.GetRadius() * 0.9f, 500f, 2500f);
            Quaternion view = Quaternion.Euler(32f, cameras.transform.eulerAngles.y, 0f);
            cameras.FocusPosition(centre, view, distance);
        }

        // Whether what is being commanded is still ours to command, from
        // where the camera is.
        private bool StillCommanding()
        {
            var cameras = SceneSingleton<CameraStateManager>.i;
            if (!GameplayReady() || cameras == null) return false;
            if (CommandState.Base != null)
                return cameras.followingUnit == null && Airfields.CanCommand(CommandState.Base, out _);
            return cameras.followingUnit == CommandState.Ship && CommandableShip.CanCommand(CommandState.Ship, out _);
        }

        internal void Leave()
        {
            if (!CommandState.Active) return;
            FlightIcons.Clear();
            ReleaseKillCredit();
            // lastCommanded deliberately survives, so command can be resumed.
            RestoreNativeBar();
            CommandState.Clear();
            Ui?.Tidy();
            CursorManager.SetFlag(CommandCursor, false);
        }

        // The EXIT button: a decision, not an interruption, so nothing comes
        // back on its own afterwards. Resuming is for the pause menu.
        internal void Dismiss()
        {
            lastCommanded = null;
            lastField = null;
            LeaveForNativeFlow();
        }

        internal void LeaveForNativeFlow()
        {
            if (!CommandState.Active) return;
            Leave();
            suppressEntryFrame = Time.frameCount;
        }

        // Leaving the bridge to fly one of this ship's own flights. The ship is
        // still ours and still shooting, so its kills still belong to us:
        // forgetting the credit here is what stops Leave handing it back.
        internal void LeaveForTakeover()
        {
            creditedPlayer = null;
            LeaveForNativeFlow();
        }

        private void LateUpdate()
        {
            // The native bar re-shows itself as the spectator UI updates, so the
            // suppression has to be reapplied rather than set once.
            if (CommandState.Active && nativeBar != null)
            {
                nativeBar.alpha = 0f;
                nativeBar.interactable = false;
                nativeBar.blocksRaycasts = false;
            }
        }

        // UnitDebug is the game's spectator status and weapon strip. Ours sits
        // on top of it, and its text shows through the translucent panel.
        private void HideNativeBar(Ship ship)
        {
            RestoreNativeBar();
            foreach (UnitDebug candidate in Resources.FindObjectsOfTypeAll<UnitDebug>())
            {
                if (!candidate.gameObject.scene.IsValid()) continue;
                if (DebugFollowing != null && DebugFollowing.GetValue(candidate) as Unit != ship) continue;
                nativeBar = candidate.GetComponent<CanvasGroup>();
                addedNativeBar = nativeBar == null;
                if (addedNativeBar) nativeBar = candidate.gameObject.AddComponent<CanvasGroup>();
                nativeAlpha = nativeBar.alpha;
                nativeInteractable = nativeBar.interactable;
                nativeBlocks = nativeBar.blocksRaycasts;
                nativeBar.alpha = 0f;
                nativeBar.interactable = false;
                nativeBar.blocksRaycasts = false;
                break;
            }
        }

        private void RestoreNativeBar()
        {
            if (nativeBar != null)
            {
                nativeBar.alpha = nativeAlpha;
                nativeBar.interactable = nativeInteractable;
                nativeBar.blocksRaycasts = nativeBlocks;
                if (addedNativeBar) Destroy(nativeBar);
            }
            nativeBar = null;
            addedNativeBar = false;
        }

        // Unit.ReportKilled splits rewards by damage credit, then pays the
        // individual only when the crediting unit's PersistentUnit has a player.
        // A ship never does, so a commander's kills paid the faction but never
        // the player. The game already uses this field for owned ground
        // vehicles, so filling it in is how the credit is meant to flow.
        private void CreditKillsToCommander(Ship ship)
        {
            ReleaseKillCredit();
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null) return;
            if (!UnitRegistry.TryGetPersistentUnit(ship.persistentID, out PersistentUnit persistent) || persistent == null) return;
            // Already ours from an earlier stint on this bridge -- taking the
            // controls of one of its flights leaves the credit in place on the
            // way out, so coming back has to pick the release up again.
            if (persistent.player != null && persistent.player != player) return;
            persistent.player = player;
            creditedPlayer = player;
        }

        private void ReleaseKillCredit()
        {
            Ship ship = CommandState.Ship;
            if (creditedPlayer == null || ship == null) { creditedPlayer = null; return; }
            if (UnitRegistry.TryGetPersistentUnit(ship.persistentID, out PersistentUnit persistent) &&
                persistent != null && persistent.player == creditedPlayer)
                persistent.player = null;
            creditedPlayer = null;
        }

        private void Update()
        {
            // Independent subsystems, independently retired. A fault in cargo
            // missions must not also stop damage control from running.
            Guard.Run("Flight orders", FlightOrders.Tick);
            Guard.Run("Launch queue", LaunchQueue.Tick);
            Guard.Run("Wings", Wings.Tick);
            Guard.Run("Ship names", ShipNames.Tick);
            Guard.Run("Task forces", TaskForces.Tick);
            Guard.Run("Bearing launch", BearingLaunch.Tick);
            Guard.Run("Pilot seat", PilotSeat.Tick);
            Guard.Run("Damage control", DamageControl.WorkAll);
            Guard.Run("Flight icons", () => FlightIcons.Refresh(CommandState.Active));
            UpdateGesture();
            if (!CommandState.Active) { TryResume(); return; }
            if (!StillCommanding())
            {
                LeaveForNativeFlow();
                return;
            }
            RefreshHover();
            Ui?.RefreshPinned();
            HandleZoom();
        }

        private void UpdateGesture()
        {
            if (gestureFrame == Time.frameCount) return;
            gestureFrame = Time.frameCount;
            bool down = Input.GetMouseButtonDown(0), held = Input.GetMouseButton(0);
            leftGesture.Update(Time.frameCount, down, held);

            // The world camera may only be orbited by a press that started in
            // open world space. While the map is up, or over any of our own or
            // the game's UI, the mouse belongs to the map, not the camera.
            bool overOwnUi = Ui != null && (Ui.PointerInside() || Ui.PopupOpen);
            // Full screen the map owns every pixel; docked, only its own square,
            // so the world can be orbited around it.
            bool mapOwns = Ui != null ? Ui.MapCovers(Input.mousePosition) : DynamicMap.mapMaximized;
            bool ready = CommandState.Active && !mapOwns && !overOwnUi && !PointerOnForeignUi(null);
            cameraGesture.Update(down, held, ready, leftGesture.Claimed);
        }

        // The native orbit gate is `if (!Cursor.visible)`, so "visible" means
        // "do not orbit". The command bar keeps the cursor up permanently, so
        // report it hidden only for a genuine world drag.
        internal bool AllowsWorldCameraDrag()
        {
            UpdateGesture();
            return CommandState.Active && cameraGesture.Dragging;
        }

        internal bool BlocksMapDrag() { UpdateGesture(); return leftGesture.Claimed; }

        // Opening the pause menu drops command because gameplay is no longer
        // ready, and nothing re-enters afterwards: the camera never changed, so
        // no follow event fires. Come back on our own once the conditions hold
        // again, and give a key for the case where the camera did move.
        private void TryResume()
        {
            // The same key that asks for the command view back is what asks for
            // it back from the cockpit.
            if (PilotSeat.Active)
            {
                if (Settings.ResumeCommand.Value.IsDown()) PilotSeat.Release(recoverToShip: false);
                return;
            }
            if (Settings.ResumeCommand.Value.IsDown())
            {
                var cameras = SceneSingleton<CameraStateManager>.i;
                string why = "Follow a ship you can command, then press " +
                    Settings.ResumeCommand.Value.MainKey + ".";
                if (GameplayReady() && cameras != null && cameras.followingUnit is Ship followed)
                {
                    if (CommandableShip.CanCommand(followed, out string reason)) { Enter(followed); return; }
                    why = reason ?? why;
                }
                else if (cameras != null && cameras.followingUnit == null && lastField != null)
                {
                    EnterAirfield(lastField);
                    return;
                }
                CommandState.Say(why);
                return;
            }

            if (!Settings.AutoResume.Value) return;
            if (Time.frameCount == suppressEntryFrame) return;
            var camera = SceneSingleton<CameraStateManager>.i;
            // A field is resumed only while the view is still a free camera:
            // the pause menu drops command without moving it.
            if (lastField != null)
            {
                if (camera == null || camera.followingUnit != null || !GameplayReady() ||
                    !Airfields.CanCommand(lastField, out _)) return;
                Airbase field = lastField;
                CommandState.Base = field;
                CursorManager.SetFlag(CommandCursor, true);
                return;
            }
            if (lastCommanded == null) return;
            if (camera == null || camera.followingUnit != lastCommanded) return;
            if (!GameplayReady() || !CommandableShip.CanCommand(lastCommanded, out _)) return;
            Enter(lastCommanded);
        }

        internal bool HandleSelection(Unit unit)
        {
            if (unit == null) return true;
            UpdateGesture();
            if (leftGesture.Claimed) return false;
            bool eligible = unit is Ship ship && CommandableShip.CanCommand(ship, out _);
            SelectionAction action = InputPolicy.Select(CommandState.Active, unit == CommandState.Ship,
                eligible, Time.frameCount == suppressEntryFrame);
            if (action == SelectionAction.Consume) return false;
            if (action == SelectionAction.Exit)
            {
                // Choosing something else to look at is leaving the field.
                lastField = null;
                // Going from one commandable ship to another is a change of
                // command, not an exit. Suppressing entry treats the click as
                // leaving, and the camera arrives at the new ship on the same
                // frame the suppression is set, so it declines to take it --
                // which is why the second ship needed selecting twice, once to
                // let go of the first and once to actually arrive.
                if (eligible) { Leave(); return true; }
                LeaveForNativeFlow();
                return true;
            }
            return true;
        }

        internal void ProcessInput()
        {
            UpdateGesture();
            if (!CommandState.Active || inputFrame == Time.frameCount) return;
            inputFrame = Time.frameCount;

            bool left = Input.GetMouseButtonDown(0), right = Input.GetMouseButtonDown(1);
            if (!left && !right) return;
            if (Ui != null && Ui.PointerInside()) return;

            var map = SceneSingleton<DynamicMap>.i;
            bool onMap = map != null && DynamicMap.mapMaximized && map.IsCursorInMapRectangle();
            // Native left-drag orbits the world camera; do not pick there.
            if (left && !onMap)
            {
                if (Ui != null && Ui.PopupOpen) { leftGesture.Claim(); Ui.ClosePopup(); }
                return;
            }

            Unit pointed = onMap ? PickMapUnit(map) : PickWorldUnit();
            EsmContact estimate = onMap ? PickEsm(map) : null;
            if (estimate != null && (pointed == null || pickedEsmDistance < pickedUnitDistance)) pointed = null;
            else estimate = null;

            if (left)
            {
                if (Ui != null && Ui.PopupOpen) { leftGesture.Claim(); Ui.ClosePopup(); }
                return;
            }

            if (PointerOnForeignUi(onMap ? map : null)) return;
            Flight tasking = CommandState.SelectedFlight;
            bool pinned = Ui != null && Ui.Pinned && tasking != null;
            // A new right-click makes the old menu stale; standing windows,
            // including a flight being tasked, stay as they are.
            Ui?.ClosePopup();
            bool append = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (estimate != null) { Ui?.OpenEsmContext(Input.mousePosition, estimate); return; }

            // A selected flight takes the contact rather than opening a menu:
            // with a flight in hand, right-clicking a hostile plainly means
            // "attack that". Refuse rather than send it when nothing aboard can.
            // Only on a live track: a stale one gets its menu, which offers a
            // search rather than a strike on a position nobody has.
            if (tasking != null && pointed != null && pointed != CommandState.Ship &&
                pointed.NetworkHQ != null && pointed.NetworkHQ != CommandState.Hq && TrackReadout.IsCurrent(pointed))
            {
                string contact = pointed.definition?.unitName ?? pointed.name;
                if (FlightOrders.BestStationFor(tasking.Aircraft, pointed) == null)
                {
                    CommandState.Say(tasking.Name + " carries nothing that can attack " + contact);
                    return;
                }
                WingOrders.Strike(tasking, pointed);
                CommandState.Say(tasking.Name + " striking " + contact);
                return;
            }

            // A weapon in hand shoots at contacts, not at our own side: a
            // friendly still gets its menu.
            bool friendly = pointed != null && pointed.NetworkHQ != null && pointed.NetworkHQ == CommandState.Hq;
            RightClickAction action = InputPolicy.RightClick(CommandState.Active, onMap,
                pointed != null, CommandState.Armed && !friendly, append);
            // Ctrl on a bare point asks what to do there rather than sailing to it.
            bool ask = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ask && onMap && pointed == null &&
                (action == RightClickAction.ReplaceWaypoint || action == RightClickAction.AppendWaypoint))
            {
                Ui?.OpenPointContext(Input.mousePosition, map.GetCursorCoordinates());
                return;
            }

            switch (action)
            {
                case RightClickAction.AttackTarget:
                    WeaponOrders.Attack(CommandState.Ship, CommandState.SelectedKey, pointed, CommandState.Quantity, out string attackReason, append);
                    CommandState.Say(attackReason);
                    break;
                case RightClickAction.Menu:
                    Ui?.OpenContext(Input.mousePosition, pointed, append);
                    break;
                case RightClickAction.MissingTarget:
                    // A weapon that finds its own target goes down the bearing
                    // of the point clicked; anything else needs a contact.
                    WeaponStation seeking = null;
                    if (onMap && CommandState.Ship != null)
                        foreach (WeaponStation station in WeaponOrders.StationsFor(CommandState.Ship, CommandState.SelectedKey))
                            if (station.Ammo > 0 && BearingLaunch.SelfAcquiring(station.WeaponInfo)) { seeking = station; break; }
                    if (seeking != null)
                    {
                        float bearing = BearingLaunch.BearingTo(CommandState.Ship, map.GetCursorCoordinates());
                        BearingLaunch.Order(CommandState.Ship, seeking, bearing, CommandState.Quantity, out string launched);
                        CommandState.Say(launched);
                        break;
                    }
                    CommandState.Say((CommandState.SelectedWeapon()?.Name ?? "This weapon") +
                        " needs a target. Right-click a compatible contact.");
                    break;
                // Shift keeps tasking the flight so a multi-leg route can be
                // laid down; a plain click sends it and hands the map back to
                // the ship, so orders never silently keep going to the aircraft.
                case RightClickAction.AppendWaypoint when tasking != null:
                    WingOrders.SetRoute(tasking, map.GetCursorCoordinates(), true);
                    CommandState.Say(tasking.Name + " · leg appended · shift-click to add more");
                    break;
                // A zone that was asked for, or a new one for a delivery
                // already running.
                case RightClickAction.ReplaceWaypoint when tasking != null &&
                        (CommandState.AwaitingCargoZone == tasking || tasking.Mode == FlightMode.Cargo):
                    bool airdrop = CommandState.AwaitingCargoZone == tasking
                        ? CommandState.AwaitingAirdrop : tasking.Airdrop;
                    CommandState.AwaitingCargoZone = null;
                    WingOrders.Deliver(tasking, map.GetCursorCoordinates(), airdrop);
                    CommandState.Say(tasking.Name + " · " +
                        (airdrop ? "airdropping at" : "landing at") + " the marked zone");
                    break;
                case RightClickAction.ReplaceWaypoint when tasking != null:
                    // A plain click sends the flight to work an area, which is
                    // the common order; shift lays down an explicit route.
                    WingOrders.SetArea(tasking, map.GetCursorCoordinates(), tasking.OrbitRadius);
                    CommandState.Say(tasking.Name + " · task area set · " +
                        UnitConverter.DistanceReading(tasking.OrbitRadius) + " radius");
                    if (!pinned) CommandState.SelectedFlight = null;
                    break;
                // An airfield does not move: a bare click asks what to send
                // there instead.
                case RightClickAction.AppendWaypoint when CommandState.Ship == null:
                case RightClickAction.ReplaceWaypoint when CommandState.Ship == null:
                    Ui?.OpenPointContext(Input.mousePosition, map.GetCursorCoordinates());
                    break;
                case RightClickAction.AppendWaypoint:
                    NavigationOrders.AppendWaypoint(CommandState.Ship, map.GetCursorCoordinates(), out string appendReason);
                    CommandState.Say(appendReason);
                    break;
                case RightClickAction.ReplaceWaypoint:
                    NavigationOrders.ReplaceWaypoint(CommandState.Ship, map.GetCursorCoordinates(), out string replaceReason);
                    CommandState.Say(replaceReason);
                    break;
            }
        }

        // Camera states keep their own FOV trim and clamp it themselves, so
        // nudging that field is all this needs to do -- the state applies and
        // bounds it on its next update. Which state is driving depends on how
        // the player is viewing, so all of them are adjusted together.
        private static readonly string[] ZoomStates = { "orbitState", "chaseState", "freeState" };

        private void HandleZoom()
        {
            float delta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(delta) < 0.01f) return;

            // A feed under the cursor takes the wheel; so does the map, which
            // has its own zoom, and so does any of our own panels.
            if (Ui != null && Ui.ZoomedAFeed(delta)) return;
            if (Ui != null ? Ui.MapCovers(Input.mousePosition) : DynamicMap.mapMaximized) return;
            if (Ui != null && Ui.PointerInside()) return;

            var cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null) return;
            foreach (string name in ZoomStates)
            {
                System.Reflection.FieldInfo holder = AccessTools.Field(typeof(CameraStateManager), name);
                object state = holder?.GetValue(cameras);
                if (state == null) continue;
                System.Reflection.FieldInfo trim = AccessTools.Field(state.GetType(), "FOVAdjustment");
                if (trim == null) continue;
                float current = (float)trim.GetValue(state);
                trim.SetValue(state, current - delta * Settings.ZoomSensitivity.Value);
            }
        }

        private void RefreshHover()
        {
            var map = SceneSingleton<DynamicMap>.i;
            bool canHover = map != null && DynamicMap.mapMaximized && map.IsCursorInMapRectangle() &&
                (Ui == null || !Ui.PointerInside()) && !PointerOnForeignUi(map);
            HoverUnit = canHover ? PickMapUnit(map) : null;
            HoverEsm = canHover ? PickEsm(map) : null;
            // Whichever symbol the cursor is actually nearer to wins.
            if (HoverUnit != null && HoverEsm != null)
            {
                if (pickedEsmDistance < pickedUnitDistance) HoverUnit = null; else HoverEsm = null;
            }
        }

        internal EsmContact PickEsm(DynamicMap map)
        {
            pickedEsmDistance = float.PositiveInfinity;
            if (map == null || map.mapImage == null || CommandState.Ship == null) return null;
            EsmContact nearest = null;
            float distance = 24f * 24f;
            float factor = 900f * map.mapImage.transform.lossyScale.x / map.mapDimension;
            foreach (EsmContact contact in Esm.GetContacts(CommandState.Ship))
            {
                Vector2 screen = map.mapImage.transform.position +
                    new Vector3(contact.Position.x, contact.Position.z, 0f) * factor;
                float d = ((Vector2)Input.mousePosition - screen).sqrMagnitude;
                if (d >= distance) continue;
                distance = d;
                nearest = contact;
            }
            if (nearest != null) pickedEsmDistance = distance;
            return nearest;
        }

        private Unit PickMapUnit(DynamicMap map)
        {
            pickedUnitDistance = float.PositiveInfinity;
            Unit nearest = null;
            float distance = 24f * 24f;
            // Only native visible icons; this discovers no hidden contacts.
            foreach (MapIcon icon in map.mapIcons)
            {
                if (!(icon is UnitMapIcon unitIcon) || unitIcon.unit == null || !icon.gameObject.activeInHierarchy ||
                    icon.iconImage == null || !icon.iconImage.enabled || icon.iconImage.color.a < 0.02f) continue;
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, icon.iconImage.transform.position);
                float d = ((Vector2)Input.mousePosition - screen).sqrMagnitude;
                if (d >= distance) continue;
                distance = d;
                nearest = unitIcon.unit;
            }
            if (nearest != null) pickedUnitDistance = distance;
            return nearest;
        }

        private static Unit PickWorldUnit()
        {
            var cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null || cameras.mainCamera == null) return null;
            if (!Physics.Raycast(cameras.mainCamera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit,
                500000f, PhysicsLayers.Everything, QueryTriggerInteraction.Ignore)) return null;
            var part = hit.collider.GetComponentInParent<UnitPart>();
            Unit unit = part != null ? part.parentUnit : hit.collider.GetComponentInParent<Unit>();
            if (unit == null || unit.disabled) return null;
            // A world click grants no knowledge beyond the native accurate track.
            if (GameManager.GetLocalHQ(out FactionHQ hq) && unit.NetworkHQ != hq && !hq.IsTargetPositionAccurate(unit, 100f)) return null;
            return unit;
        }

        private bool PointerOnForeignUi(DynamicMap map)
        {
            EventSystem events = EventSystem.current;
            if (events == null) return false;
            if (pointer == null || pointerEvents != events) { pointer = new PointerEventData(events); pointerEvents = events; }
            pointer.position = Input.mousePosition;
            uiHits.Clear();
            events.RaycastAll(pointer, uiHits);
            foreach (RaycastResult hit in uiHits)
            {
                Transform hitTransform = hit.gameObject.transform;
                if (Ui != null && Ui.Contains(hitTransform)) return true;
                if (map != null && (hitTransform.IsChildOf(map.transform) || hitTransform == map.transform)) continue;
                if (hit.gameObject.GetComponent<Graphic>() != null) return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SetFollowingUnit))]
    internal static class FollowingPatch
    {
        private static void Postfix(Unit unit) =>
            Guard.Run("Camera follow", () => MapCommand.Instance?.FollowingChanged(unit));
    }

    [HarmonyPatch(typeof(UnitMapIcon), nameof(UnitMapIcon.ClickIcon))]
    internal static class MapSelectionPatch
    {
        private static bool Prefix(UnitMapIcon __instance) =>
            MapCommand.Instance == null || MapCommand.Instance.HandleSelection(__instance.unit);
    }

    // Right-click belongs to the command layer while active; native map pan and
    // zoom keep left-drag and the wheel. Rewriting the two Input calls inside
    // MapControls is narrower than suppressing the method wholesale.
    [HarmonyPatch(typeof(DynamicMap), "MapControls")]
    internal static class MapInputPatch
    {
        private const string Name = "Map command input";

        private static bool Prefix()
        {
            if (!Guard.Ok(Name)) return true;                 // give the map back to the game
            try { return Body(); }
            catch (Exception ex) { Guard.Failed(Name, ex); return true; }
        }

        private static bool Body()
        {
            MapCommand instance = MapCommand.Instance;
            instance?.ProcessInput();
            // The map keeps its own controls except under our surfaces -- which
            // now includes the seat bar, so clicking a button on it does not
            // also drag the map underneath.
            if (instance == null || instance.Ui == null) return true;
            if (!CommandState.Active && !PilotSeat.Active) return true;
            // Docked, the map's pan and zoom would otherwise act wherever the
            // cursor is -- stealing every orbit and zoom of the world camera.
            if (!instance.Ui.MapControlsAllowed()) return false;
            return !instance.Ui.PointerInside();
        }

        internal static bool MouseDown(int button) =>
            InputPolicy.NativeMouseDown(CommandState.Active, button, Input.GetMouseButtonDown(button));

        internal static bool MouseHeld(int button) =>
            Input.GetMouseButton(button) && !(button == 0 && MapCommand.Instance != null && MapCommand.Instance.BlocksMapDrag());

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            MethodInfo down = AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonDown), new[] { typeof(int) });
            MethodInfo held = AccessTools.Method(typeof(Input), nameof(Input.GetMouseButton), new[] { typeof(int) });
            MethodInfo downReplacement = AccessTools.Method(typeof(MapInputPatch), nameof(MouseDown));
            MethodInfo heldReplacement = AccessTools.Method(typeof(MapInputPatch), nameof(MouseHeld));
            int downMatches = 0, heldMatches = 0;
            foreach (CodeInstruction item in code) { if (item.Calls(down)) downMatches++; if (item.Calls(held)) heldMatches++; }
            if (downMatches != 1 || heldMatches != 1)
                throw new InvalidOperationException("Native map input shape changed; refusing to patch.");
            foreach (CodeInstruction item in code)
            {
                if (item.Calls(down)) item.operand = downReplacement;
                else if (item.Calls(held)) item.operand = heldReplacement;
            }
            return code;
        }
    }

    // The command bar keeps the cursor visible, which the native orbit gate
    // reads as "do not orbit". Swap just that read; axes, sensitivity,
    // smoothing and clamps stay native.
    [HarmonyPatch(typeof(CameraOrbitState), "Inputs")]
    internal static class CameraOrbitInputPatch
    {
        internal static bool CursorVisibleForOrbit()
        {
            MapCommand instance = MapCommand.Instance;
            return instance == null || !CommandState.Active
                ? Cursor.visible
                : !instance.AllowsWorldCameraDrag();
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            MethodInfo native = AccessTools.PropertyGetter(typeof(Cursor), nameof(Cursor.visible));
            MethodInfo adapter = AccessTools.Method(typeof(CameraOrbitInputPatch), nameof(CursorVisibleForOrbit));
            int matches = 0;
            foreach (CodeInstruction item in code) if (item.Calls(native)) matches++;
            if (matches != 1) throw new InvalidOperationException("Native orbit input gate changed; refusing to patch.");
            foreach (CodeInstruction item in code) if (item.Calls(native)) item.operand = adapter;
            return code;
        }
    }

    // Shift-click on an airbase takes command of it. A plain click stays the
    // native one, which is how you pick a field to fly from yourself.
    [HarmonyPatch(typeof(AirbaseMapIcon), nameof(AirbaseMapIcon.ClickIcon))]
    internal static class AirbaseSelectionPatch
    {
        private static bool Prefix(AirbaseMapIcon __instance)
        {
            if (!(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return true;
            if (MapCommand.Instance == null || __instance.airbase == null || __instance.airbase.AttachedAirbase) return true;
            if (PilotSeat.Active) return true;
            Guard.Run("Airfield command", () => MapCommand.Instance.EnterAirfield(__instance.airbase));
            return false;
        }
    }

    [HarmonyPatch]
    internal static class LeaveCommandPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameplayUI), nameof(GameplayUI.SelectAircraft));
            yield return AccessTools.Method(typeof(GameplayUI), nameof(GameplayUI.ShowJoinMenu));
        }

        private static void Prefix() =>
            Guard.Run("Leave for native flow", () => MapCommand.Instance?.LeaveForNativeFlow());
    }
}

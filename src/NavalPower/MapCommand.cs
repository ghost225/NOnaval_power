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

        private static bool GameplayReady()
        {
            if (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer) return false;
            var gameplay = SceneSingleton<GameplayUI>.i;
            if (gameplay == null || (gameplay.menuCanvas != null && gameplay.menuCanvas.enabled) || GameplayUI.GameIsPaused) return false;
            if (GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null && !aircraft.disabled) return false;
            return SceneSingleton<CameraStateManager>.i != null;
        }

        internal void FollowingChanged(Unit unit)
        {
            if (CommandState.Active)
            {
                if (unit == CommandState.Ship) return;
                Leave();
                suppressEntryFrame = Time.frameCount;
                return;
            }
            if (Time.frameCount == suppressEntryFrame || !GameplayReady()) return;
            if (unit is Ship ship && CommandableShip.CanCommand(ship, out _)) Enter(ship);
        }

        internal void Enter(Ship ship)
        {
            if (!GameplayReady()) return;
            CommandState.Ship = ship;
            lastCommanded = ship;
            CommandState.SelectedKey = null;
            CommandState.Quantity = 1;
            Esm.Configure(ship);
            HideNativeBar(ship);
            CreditKillsToCommander(ship);
            CursorManager.SetFlag(CommandCursor, true);
            CommandState.Say("Command active · right-click map: waypoint · shift: append · right-click contact: menu");
        }

        internal void Leave()
        {
            if (!CommandState.Active) return;
            FlightIcons.Clear();
            ReleaseKillCredit();
            // lastCommanded deliberately survives, so command can be resumed.
            RestoreNativeBar();
            CommandState.Clear();
            Ui?.ClosePopup();
            CursorManager.SetFlag(CommandCursor, false);
        }

        internal void LeaveForNativeFlow()
        {
            if (!CommandState.Active) return;
            Leave();
            suppressEntryFrame = Time.frameCount;
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
            if (persistent.player != null) return;          // already owned; leave it alone
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
            FlightOrders.Tick();
            FlightIcons.Refresh(CommandState.Ship);
            UpdateGesture();
            if (!CommandState.Active) { TryResume(); return; }
            var cameras = SceneSingleton<CameraStateManager>.i;
            if (!GameplayReady() || cameras == null || cameras.followingUnit != CommandState.Ship ||
                !CommandableShip.CanCommand(CommandState.Ship, out _))
            {
                LeaveForNativeFlow();
                return;
            }
            RefreshHover();
            Ui?.RefreshPinned();
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
            bool ready = CommandState.Active && !DynamicMap.mapMaximized && !overOwnUi && !PointerOnForeignUi(null);
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
                CommandState.Say(why);
                return;
            }

            if (!Settings.AutoResume.Value) return;
            if (lastCommanded == null || Time.frameCount == suppressEntryFrame) return;
            var camera = SceneSingleton<CameraStateManager>.i;
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
            if (action == SelectionAction.Exit) { LeaveForNativeFlow(); return true; }
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
                if (Ui != null && Ui.PopupOpen && !Ui.Pinned) { leftGesture.Claim(); Ui.ClosePopup(); }
                return;
            }

            Unit pointed = onMap ? PickMapUnit(map) : PickWorldUnit();
            EsmContact estimate = onMap ? PickEsm(map) : null;
            if (estimate != null && (pointed == null || pickedEsmDistance < pickedUnitDistance)) pointed = null;
            else estimate = null;

            if (left)
            {
                if (Ui != null && Ui.PopupOpen && !Ui.Pinned) { leftGesture.Claim(); Ui.ClosePopup(); }
                return;
            }

            if (PointerOnForeignUi(onMap ? map : null)) return;
            Flight tasking = CommandState.SelectedFlight;
            bool pinned = Ui != null && Ui.Pinned && tasking != null;
            if (!pinned) Ui?.ClosePopup();
            bool append = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (estimate != null) { Ui?.OpenEsmContext(Input.mousePosition, estimate); return; }

            RightClickAction action = InputPolicy.RightClick(CommandState.Active, onMap,
                pointed != null, CommandState.Armed, append);

            switch (action)
            {
                case RightClickAction.AttackTarget:
                    WeaponOrders.Attack(CommandState.Ship, CommandState.SelectedKey, pointed, CommandState.Quantity, out string attackReason, append);
                    CommandState.Say(attackReason);
                    break;
                case RightClickAction.Menu:
                    Ui?.OpenContext(Input.mousePosition, pointed == CommandState.Ship ? null : pointed, append);
                    break;
                case RightClickAction.MissingTarget:
                    CommandState.Say((CommandState.SelectedWeapon()?.Name ?? "This weapon") +
                        " needs a target. Right-click a compatible contact.");
                    break;
                // Shift keeps tasking the flight so a multi-leg route can be
                // laid down; a plain click sends it and hands the map back to
                // the ship, so orders never silently keep going to the aircraft.
                case RightClickAction.AppendWaypoint when tasking != null:
                    FlightOrders.SetRoute(tasking, map.GetCursorCoordinates(), true);
                    CommandState.Say(tasking.Name + " · leg appended · shift-click to add more");
                    break;
                case RightClickAction.ReplaceWaypoint when tasking != null:
                    // A plain click sends the flight to work an area, which is
                    // the common order; shift lays down an explicit route.
                    FlightOrders.SetArea(tasking, map.GetCursorCoordinates(), tasking.OrbitRadius);
                    CommandState.Say(tasking.Name + " · task area set · " +
                        UnitConverter.DistanceReading(tasking.OrbitRadius) + " radius");
                    if (!pinned) CommandState.SelectedFlight = null;
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
        private static void Postfix(Unit unit) => MapCommand.Instance?.FollowingChanged(unit);
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
        private static bool Prefix()
        {
            MapCommand instance = MapCommand.Instance;
            instance?.ProcessInput();
            return instance == null || !CommandState.Active || instance.Ui == null || !instance.Ui.PointerInside();
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

    [HarmonyPatch]
    internal static class LeaveCommandPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameplayUI), nameof(GameplayUI.SelectAircraft));
            yield return AccessTools.Method(typeof(GameplayUI), nameof(GameplayUI.ShowJoinMenu));
        }

        private static void Prefix() => MapCommand.Instance?.LeaveForNativeFlow();
    }
}

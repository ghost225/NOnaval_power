using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.MissionEditorScripts;
using UnityEngine;
using NOrders;

namespace NavalPower
{
    // Deck view: a free camera that rides with the ship.
    //
    // A third step in the game's own "Switch View" cycle for a ship: orbit,
    // fly-by, deck view, back to orbit. Flown like the free camera -- the
    // movement keys, free look, the wheel to zoom -- but its position and
    // heading are kept relative to the ship, so it is carried along with the
    // ship's motion and turns: a camera hovering off the starboard bow stays
    // off the starboard bow through a turn. The horizon stays level (the
    // ship's heading only, not its pitch or roll). It is a camera state of
    // our own, so the camera keeps following the ship and command holds.
    // Kept above sea and terrain as the free camera is, and out of the hull.
    // The place it was left at is remembered per ship and used next time.
    internal sealed class DeckViewState : CameraBaseState
    {
        internal static readonly DeckViewState Instance = new DeckViewState();

        private Ship ship;
        private Vector3 local;              // the camera, in the ship's level frame
        private Vector3 localVelocity;
        private float pan, tilt;            // the view, relative to the ship's heading
        private float fovAdjust;
        private static readonly Dictionary<Ship, (Vector3 at, float pan, float tilt)> remembered = new Dictionary<Ship, (Vector3, float, float)>();

        internal static bool Active
        {
            get
            {
                var cameras = SceneSingleton<CameraStateManager>.i;
                return cameras != null && cameras.currentState == Instance;
            }
        }

        internal static void Zoom(float delta)
        {
            if (Active) Instance.fovAdjust -= delta * Settings.ZoomSensitivity.Value;
        }

        private static Quaternion Level(Ship s) => Quaternion.Euler(0f, s.transform.eulerAngles.y, 0f);

        public override void EnterState(CameraStateManager cam)
        {
            ship = cam.followingUnit as Ship;
            cam.transform.SetParent(null, worldPositionStays: true);
            CameraStateManager.cameraMode = CameraMode.free;
            cam.cockpitCamRender.enabled = false;
            FlightHud.EnableCanvas(enable: false);
            fovAdjust = 0f;
            localVelocity = Vector3.zero;
            if (ship == null) return;
            Survey(ship);
            Quaternion level = Level(ship);
            if (remembered.TryGetValue(ship, out var last))
            {
                local = last.at; pan = last.pan; tilt = last.tilt;
            }
            else
            {
                // First time on this ship: above and behind the stern, looking
                // up the deck -- not the fly-by's fixed spot out in the sea.
                UnitDefinition def = ship.definition;
                float length = def != null && def.length > 0f ? def.length : 100f;
                float height = def != null ? def.height : 15f;
                local = new Vector3(0f, height + Mathf.Max(25f, length * 0.25f), -(length * 0.5f + Mathf.Max(50f, length * 0.5f)));
                pan = 0f;
                tilt = 15f;
            }
            Place(cam, level, snap: true);
        }

        public override void LeaveState(CameraStateManager cam)
        {
            if (ship != null) remembered[ship] = (local, pan, tilt);
        }

        public override void UpdateState(CameraStateManager cam)
        {
            if (ship == null || ship.disabled || cam.followingUnit != ship)
            {
                cam.SwitchState(cam.freeState);
                return;
            }
            cam.windNoiseExternal.volume = 0f;
            fovAdjust -= cam.fovChangeSpeed * Mathf.Min(cam.mainCamera.fieldOfView / 20f, 1f) * GameManager.playerInput.GetAxis("FOV");
            fovAdjust = Mathf.Clamp(fovAdjust, 1f - cam.desiredFOV, 120f - cam.desiredFOV);
            float fov = Mathf.Clamp(cam.desiredFOV + fovAdjust, 1f, 120f);
            cam.mainCamera.fieldOfView = Mathf.Lerp(cam.mainCamera.fieldOfView, fov, 1f / (1f + 100f * cam.fovChangeInertia));
            if (Input.GetMouseButton(1)) CursorManager.Refresh();

            bool moving = false;
            Vector3 wanted = Vector3.zero;
            Quaternion view = Quaternion.Euler(tilt, pan, 0f);
            if (!InputFieldChecker.InsideInputField)
            {
                float scale = Mathf.Min(cam.mainCamera.fieldOfView / 20f, 1f) * 0.3f * PlayerSettings.viewSensitivity;
                if (GameManager.playerInput.GetButton("Free Look"))
                {
                    pan += scale * GameManager.playerInput.GetAxis("Pan View") * (PlayerSettings.viewInvertPitch ? -1f : 1f);
                    tilt = Mathf.Clamp(tilt + scale * GameManager.playerInput.GetAxis("Tilt View"), -89f, 89f);
                }
                float along = GameManager.playerInput.GetAxis("Move Longitudinal");
                float across = GameManager.playerInput.GetAxis("Move Lateral");
                float up = GameManager.playerInput.GetAxis("Move Vertical");
                if (cam.allowInputs && (along != 0f || across != 0f || up != 0f))
                {
                    Vector3 direction = view * Vector3.forward * along + view * Vector3.right * across + Vector3.up * up;
                    if (direction.sqrMagnitude > 1f) direction.Normalize();
                    wanted = direction * Speed() * Mathf.Max(cam.desiredTransSpeed, 0.1f);
                    moving = true;
                }
            }
            // Direct control: the camera goes the speed asked for almost at
            // once and stops almost at once, with no drift after the keys are
            // let go -- the free camera's build-up and coast overshot any spot
            // on a deck. Smoothed over about a tenth of a second, the same at
            // any frame rate.
            if (!moving) wanted = Vector3.zero;
            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.1f);
            localVelocity = Vector3.Lerp(localVelocity, wanted, blend);
            if (!moving && localVelocity.sqrMagnitude < 0.01f) localVelocity = Vector3.zero;
            local += localVelocity * Time.unscaledDeltaTime;

            Place(cam, Level(ship), snap: false);

            if (GameManager.playerInput.GetButtonTimedPressUp("Switch View", 0f, PlayerSettings.clickDelay) ||
                GameManager.playerInput.GetButtonDown("Center"))
                cam.SwitchState(cam.orbitState);
        }

        public override void FixedUpdateState(CameraStateManager cam) { }

        // What a class offers for fixed views -- its parts by name, its sensor
        // mounts and its deck -- logged once per class, to choose how views
        // such as "bridge" are found across stock and mod ships.
        private static readonly HashSet<string> surveyed = new HashSet<string>();
        private static void Survey(Ship s)
        {
            string type = s.definition?.unitName ?? s.name;
            if (!surveyed.Add(type)) return;
            try
            {
                Quaternion level = Level(s);
                string At(Transform t)
                {
                    Vector3 v = Quaternion.Inverse(level) * (t.position - s.transform.position);
                    return "(" + v.x.ToString("0") + "," + v.y.ToString("0") + "," + v.z.ToString("0") + ")";
                }
                var parts = new List<string>();
                foreach (ShipPart part in s.parts) if (part != null) parts.Add(part.name + At(part.transform));
                var sensors = new List<string>();
                foreach (TargetDetector d in s.GetComponentsInChildren<TargetDetector>(true)) sensors.Add(d.name + At(d.transform));
                string deck = "";
                foreach (Airbase a in s.GetComponentsInChildren<Airbase>(true))
                    if (a?.runways != null)
                        foreach (Airbase.Runway r in a.runways)
                            if (r?.Start != null && r.End != null) deck += " · runway " + At(r.Start) + "→" + At(r.End);
                UnitDefinition def = s.definition;
                Plugin.Log.LogInfo("[deckview] " + type + " · " + (def != null ? def.length.ToString("0") + "×" + def.width.ToString("0") + "×" + def.height.ToString("0") + " m" : "") +
                    " · parts: " + string.Join(", ", parts.ToArray()) + " · sensors: " + string.Join(", ", sensors.ToArray()) + deck);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[deckview] survey of " + type + ": " + ex.Message); }
        }

        // Metres a second: walking pace on the deck, faster the further out
        // the camera is, so a long way off is not a long wait. Shift for four
        // times as fast.
        private float Speed()
        {
            UnitDefinition def = ship.definition;
            float half = def != null ? Mathf.Max(def.length * 0.5f, 10f) : 50f;
            Vector3 flat = new Vector3(local.x, 0f, local.z);
            float fromHull = Mathf.Max(flat.magnitude - half, 0f);
            float aboveDeck = Mathf.Max(local.y - (def != null ? def.height : 0f), 0f);
            float speed = Mathf.Clamp(6f + Mathf.Max(fromHull, aboveDeck) * 0.35f, 6f, 250f);
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? speed * 4f : speed;
        }

        // The camera from its place in the ship's frame, kept out of the
        // hull, the sea and the ground.
        private void Place(CameraStateManager cam, Quaternion level, bool snap)
        {
            UnitDefinition def = ship.definition;
            if (def != null && def.length > 0f && Mathf.Abs(local.z) < def.length * 0.5f && Mathf.Abs(local.x) < Mathf.Max(def.width, 8f) * 0.5f)
                local.y = Mathf.Max(local.y, def.height + 2f);
            Vector3 position = ship.transform.position + level * local;
            if ((SceneSingleton<MissionEditor>.i == null || !SceneSingleton<MissionEditor>.i.allowCameraClip) &&
                Physics.Linecast(position + Vector3.up * 5000f, position - Vector3.up * 5000f, out RaycastHit hit, PhysicsLayers.StaticsMask))
                position.y = Mathf.Max(position.y, hit.point.y + 1.7f);
            position.y = Mathf.Max(position.y, Datum.LocalSeaY + 1.7f);
            local = Quaternion.Inverse(level) * (position - ship.transform.position);
            Quaternion wanted = level * Quaternion.Euler(tilt, pan, 0f);
            Quaternion rotation = snap ? wanted
                : Quaternion.Lerp(cam.transform.rotation, wanted, Mathf.Min(2f * Time.unscaledDeltaTime / Mathf.Max(PlayerSettings.viewSmoothing, 0.01f), 1f));
            cam.transform.SetPositionAndRotation(position, rotation);
        }
    }

    // Into the cycle: on a ship, "Switch View" in the fly-by goes to deck
    // view. The fly-by only reads that key while flight controls are enabled
    // -- never while commanding a ship -- so the key is read here as well;
    // and where the fly-by does act on it, its step back to orbit (or to a
    // cockpit view, for a unit with one) is turned into deck view.
    [HarmonyPatch(typeof(CameraTVState), nameof(CameraTVState.UpdateState))]
    internal static class DeckViewCyclePatch
    {
        private const string Name = "Deck view";
        internal static bool InFlyby;

        private static void Prefix() => InFlyby = true;

        private static void Postfix(CameraStateManager cam)
        {
            InFlyby = false;
            if (!Guard.Ok(Name)) return;
            try
            {
                if (cam == null || cam.currentState != cam.TVState || !Settings.DeckView.Value) return;
                if (!(cam.followingUnit is Ship ship) || ship.disabled) return;
                if (!GameManager.playerInput.GetButtonTimedPressUp("Switch View", 0f, PlayerSettings.clickDelay)) return;
                Plugin.Log.LogInfo("[deckview] fly-by → deck view · " + ShipNames.Of(ship));
                cam.SwitchState(DeckViewState.Instance);
            }
            catch (Exception ex) { Guard.Failed(Name, ex); }
        }

        private static void Finalizer() => InFlyby = false;
    }

    [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SwitchState))]
    internal static class DeckViewSwitchPatch
    {
        private const string Name = "Deck view switch";

        private static void Prefix(CameraStateManager __instance, ref CameraBaseState state)
        {
            if (!DeckViewCyclePatch.InFlyby || !Guard.Ok(Name)) return;
            try
            {
                if ((state == __instance.orbitState || state == __instance.cockpitState) && __instance.currentState == __instance.TVState &&
                    __instance.followingUnit is Ship ship && !ship.disabled && Settings.DeckView.Value)
                {
                    Plugin.Log.LogInfo("[deckview] fly-by → deck view · " + ShipNames.Of(ship));
                    state = DeckViewState.Instance;
                }
            }
            catch (Exception ex) { Guard.Failed(Name, ex); }
        }
    }
}

namespace NavalPower
{
    // Commanding a ship, the aircraft HUD stays off. Closing the map turns it
    // back on whenever the camera is in a cockpit view, and the fly-by could
    // step to a ship's cockpit view -- the HUD came up over the ship's
    // controls.
    [HarmonyLib.HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Minimize))]
    internal static class ShipHudPatch
    {
        private static void Postfix()
        {
            try
            {
                var cameras = SceneSingleton<CameraStateManager>.i;
                if (CommandState.Ship != null && cameras != null && cameras.followingUnit is Ship) FlightHud.EnableCanvas(enable: false);
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning("[deckview] hud: " + ex.Message); }
        }
    }
}

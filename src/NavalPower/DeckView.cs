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
            Quaternion level = Level(ship);
            if (remembered.TryGetValue(ship, out var last))
            {
                local = last.at; pan = last.pan; tilt = last.tilt;
            }
            else
            {
                // From wherever the fly-by left the camera.
                local = Quaternion.Inverse(level) * (cam.transform.position - ship.transform.position);
                Vector3 euler = (Quaternion.Inverse(level) * cam.transform.rotation).eulerAngles;
                pan = euler.y; tilt = euler.x > 180f ? euler.x - 360f : euler.x;
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
                    // Gentler than the free camera's: this is for moving about a ship.
                    float speed = Input.GetKey(KeyCode.LeftShift) ? 1500f : 150f;
                    localVelocity += cam.desiredTransSpeed * speed * Time.unscaledDeltaTime *
                        (view * Vector3.forward * along + view * Vector3.right * across + Vector3.up * up);
                    moving = true;
                }
            }
            local += localVelocity * Time.unscaledDeltaTime;
            float sq = localVelocity.sqrMagnitude;
            localVelocity *= moving ? 0.96f : sq < 1f ? 0f : Mathf.Lerp(0.7f, 0.96f, sq / 250000f);

            Place(cam, Level(ship), snap: false);

            if (GameManager.playerInput.GetButtonTimedPressUp("Switch View", 0f, PlayerSettings.clickDelay) ||
                GameManager.playerInput.GetButtonDown("Center"))
                cam.SwitchState(cam.orbitState);
        }

        public override void FixedUpdateState(CameraStateManager cam) { }

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

    // Into the cycle: the fly-by's "Switch View" on a ship goes to deck view
    // instead of back to orbit (ships have no cockpit view, the step the
    // game would otherwise take).
    [HarmonyPatch(typeof(CameraTVState), nameof(CameraTVState.UpdateState))]
    internal static class DeckViewCyclePatch
    {
        internal static bool InFlyby;
        private static void Prefix() => InFlyby = true;
        private static void Finalizer() => InFlyby = false;
    }

    [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SwitchState))]
    internal static class DeckViewSwitchPatch
    {
        private const string Name = "Deck view";

        private static void Prefix(CameraStateManager __instance, ref CameraBaseState state)
        {
            if (!DeckViewCyclePatch.InFlyby || !Guard.Ok(Name)) return;
            try
            {
                if (state == __instance.orbitState && __instance.currentState == __instance.TVState &&
                    __instance.followingUnit is Ship ship && !ship.disabled && Settings.DeckView.Value)
                    state = DeckViewState.Instance;
            }
            catch (Exception ex) { Guard.Failed(Name, ex); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NuclearOption.MissionEditorScripts;
using UnityEngine;
using NOrders;

namespace NavalPower
{
    // Deck view: a free camera that rides with the ship.
    //
    // Entered from the Deck view button on the command bar while commanding
    // a ship -- not from the game's "Switch View" cycle, where it was an
    // extra step whenever the camera followed your own ship. Switch View or
    // Center goes back to the orbit camera. Flown like the free camera -- the
    // movement keys, free look, the wheel to zoom -- but its position and
    // heading are kept relative to the ship, so it is carried along with the
    // ship's motion and turns: a camera hovering off the starboard bow stays
    // off the starboard bow through a turn. The horizon stays level (the
    // ship's heading only, not its pitch or roll). It is a camera state of
    // our own, so the camera keeps following the ship and command holds.
    // Kept above sea and terrain as the free camera is, and at eye height or
    // more over the ship itself -- down to standing on the deck.
    // The place it was left at is remembered per ship class, between
    // sessions, and used next time.
    internal sealed class DeckViewState : CameraBaseState
    {
        internal static readonly DeckViewState Instance = new DeckViewState();

        private Ship ship;
        private Vector3 local;              // the camera, in the ship's level frame
        private Vector3 localVelocity;
        private float pan, tilt;            // the view, relative to the ship's heading
        private float fovAdjust;
        private float nextSave, savedPan, savedTilt;
        private Vector3 savedLocal;
        private const float EyeHeight = 1.7f;      // metres: standing on the deck, as the free camera stands on the ground
        private static readonly Dictionary<Ship, (Vector3 at, float pan, float tilt)> remembered = new Dictionary<Ship, (Vector3, float, float)>();

        internal static bool Active
        {
            get
            {
                var cameras = SceneSingleton<CameraStateManager>.i;
                return cameras != null && cameras.currentState == Instance;
            }
        }

        // The setting on, and the ship the one under command.
        internal static bool AllowedOn(Ship ship)
        {
            // Only the ship under command: in the game's own spectator camera
            // it fought the free camera's momentum.
            return Settings.DeckView.Value && ship != null && CommandState.Ship == ship;
        }

        // Height held: movement stays level whatever the view's pitch, and
        // the up/down keys do nothing -- look up without climbing.
        internal static bool HeightLocked;

        // Kept at eye height over the ship's surfaces (on by default); off,
        // the camera passes through the ship -- into the bridge and so on.
        internal static bool SnapToDeck = true;

        internal static void ResetZoom() { Instance.fovAdjust = 0f; }

        // The button: into deck view on the commanded ship, or back to orbit.
        internal static void Toggle()
        {
            var cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null) return;
            if (Active) { cameras.SwitchState(cameras.orbitState); return; }
            Ship ship = CommandState.Ship;
            if (ship == null || ship.disabled || !AllowedOn(ship) || cameras.followingUnit != ship) return;
            Plugin.Log.LogInfo("[deckview] deck view · " + ShipNames.Of(ship));
            cameras.SwitchState(Instance);
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
            if (remembered.TryGetValue(ship, out var last) || Saved(ship, out last))
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
            savedLocal = local; savedPan = pan; savedTilt = tilt;
            nextSave = Time.unscaledTime + 2f;
        }

        public override void LeaveState(CameraStateManager cam)
        {
            if (ship == null) return;
            remembered[ship] = (local, pan, tilt);
            Save(ship, local, pan, tilt);
        }

        // Kept between sessions per ship class (its unit name), so a spot
        // found on one Annex is where every Annex's deck view opens.
        private static string KeyOf(Ship s) => "NavalPower.DeckView." + (s.definition?.unitName ?? s.name);

        private static void Save(Ship s, Vector3 at, float pan, float tilt)
        {
            try
            {
                PlayerPrefs.SetString(KeyOf(s), string.Join(",", new[] { at.x, at.y, at.z, pan, tilt }.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
                PlayerPrefs.Save();
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[deckview] save: " + ex.Message); }
        }

        private static bool Saved(Ship s, out (Vector3 at, float pan, float tilt) view)
        {
            view = default;
            try
            {
                string text = PlayerPrefs.GetString(KeyOf(s), "");
                string[] parts = text.Split(',');
                if (parts.Length != 5) return false;
                var v = new float[5];
                for (int i = 0; i < 5; i++)
                    if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) return false;
                view = (new Vector3(v[0], v[1], v[2]), v[3], v[4]);
                return true;
            }
            catch { return false; }
        }

        public override void UpdateState(CameraStateManager cam)
        {
            if (ship == null || ship.disabled || cam.followingUnit != ship)
            {
                cam.SwitchState(cam.freeState);
                return;
            }
            if (!AllowedOn(ship))                       // command left: back to the ship's orbit
            {
                cam.SwitchState(cam.orbitState);
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
                // Raw axes: the smoothed ones ramp up and coast down the way a
                // keyboard axis does, which slid the camera on after the key.
                float along = GameManager.playerInput.GetAxisRaw("Move Longitudinal");
                float across = GameManager.playerInput.GetAxisRaw("Move Lateral");
                float up = GameManager.playerInput.GetAxisRaw("Move Vertical");
                if (HeightLocked) up = 0f;
                if (cam.allowInputs && (along != 0f || across != 0f || up != 0f))
                {
                    Vector3 ahead = view * Vector3.forward, side = view * Vector3.right;
                    if (HeightLocked)
                    {
                        ahead = Quaternion.Euler(0f, pan, 0f) * Vector3.forward;
                        side = Quaternion.Euler(0f, pan, 0f) * Vector3.right;
                    }
                    Vector3 direction = ahead * along + side * across + Vector3.up * up;
                    if (direction.sqrMagnitude > 1f) direction.Normalize();
                    wanted = direction * Speed() * Mathf.Max(cam.desiredTransSpeed, 0.1f);
                    moving = true;
                }
            }
            // Direct control: the camera goes the speed asked for at once and
            // stops the moment the keys are let go -- no build-up, no coast.
            // The free camera's drift overshot any spot on a deck, and even a
            // tenth of a second of smoothing read as sliding.
            localVelocity = moving ? wanted : Vector3.zero;
            local += localVelocity * Time.unscaledDeltaTime;

            // Middle click: the zoom back to normal (not over our panels,
            // where it belongs to whatever is under it).
            if (Input.GetMouseButtonDown(2) && !(MapCommand.Instance?.Ui != null && MapCommand.Instance.Ui.PointerInside())) fovAdjust = 0f;

            Place(cam, Level(ship), snap: false);

            // Saved while it is in use too, every couple of seconds when moved:
            // ending the mission or closing the game in deck view never leaves
            // the state, so a spot saved only on leaving was lost.
            if (Time.unscaledTime >= nextSave)
            {
                nextSave = Time.unscaledTime + 2f;
                if ((local - savedLocal).sqrMagnitude > 0.25f || Mathf.Abs(pan - savedPan) > 1f || Mathf.Abs(tilt - savedTilt) > 1f)
                {
                    Save(ship, local, pan, tilt);
                    savedLocal = local; savedPan = pan; savedTilt = tilt;
                }
            }

            if (GameManager.playerInput.GetButtonTimedPressUp("Switch View", 0f, PlayerSettings.clickDelay) ||
                GameManager.playerInput.GetButtonDown("Center"))
                cam.SwitchState(cam.orbitState);
        }

        public override void FixedUpdateState(CameraStateManager cam) { }

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
            Vector3 position = ship.transform.position + level * local;
            // Head height over whatever of the ship is beneath: a ray straight
            // down onto the ship from above the camera, reaching to eye height
            // below it. Anything it meets means the camera is in the ship or
            // closer than eye height to it, and it is lifted to stand there.
            // (The ship's overall height held it high over the whole hull.)
            UnitDefinition def = ship.definition;
            float top = ship.transform.position.y + (def != null ? Mathf.Max(def.height, 20f) : 60f) + 50f;
            if (SnapToDeck && top > position.y - EyeHeight &&
                Physics.Raycast(new Vector3(position.x, top, position.z), Vector3.down, out RaycastHit onShip, top - (position.y - EyeHeight), (int)PhysicsLayers.ShipsMask) &&
                onShip.collider != null && onShip.collider.GetComponentInParent<Ship>() == ship)
                position.y = Mathf.Max(position.y, onShip.point.y + EyeHeight);
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

namespace NavalPower
{
    // The game's own third step for a ship -- fly-by to the "cockpit" view on
    // a ship with a cockpit view point -- does nothing while commanding it:
    // that state returns at once whenever flight controls are off, so the
    // camera sat fixed, could not look around, and Switch View could not
    // leave it. While the ship is under command the cycle is orbit, fly-by,
    // orbit. (The deck view hook used to take that step; with deck view on
    // its own button now, the broken step showed again.)
    [HarmonyLib.HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SwitchState))]
    internal static class ShipCockpitViewPatch
    {
        private const string Name = "Ship cockpit view";

        private static void Prefix(CameraStateManager __instance, ref CameraBaseState state)
        {
            if (!NOrders.Guard.Ok(Name)) return;
            try
            {
                if (__instance == null || state != __instance.cockpitState) return;
                if (__instance.followingUnit is Ship ship && ship == CommandState.Ship && !GameManager.flightControlsEnabled)
                    state = __instance.orbitState;
            }
            catch (System.Exception ex) { NOrders.Guard.Failed(Name, ex); }
        }
    }
}

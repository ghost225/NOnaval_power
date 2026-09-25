using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NavalPower
{
    // The cameras behind the feed windows: one live feed that follows the
    // action, and up to three pinned to a particular unit. This renders them
    // into textures and decides what the live one watches; the windows that
    // show them belong to the command UI like every other window.
    //
    // Subject priority for the live feed is what a commander would actually
    // want to watch: a weapon of ours in flight beats the target it is going
    // for, because the interesting moment is the intercept.
    //
    // When what a feed is watching is destroyed, the feed holds where it is
    // for a few seconds rather than cutting away, so the destruction itself is
    // on screen -- which is the moment the feed exists to show.
    internal sealed class TargetFeed : MonoBehaviour
    {
        // Each feed is a camera and a texture of its own, which is why the
        // count is capped rather than open-ended.
        internal const int MaxPinned = 3;

        internal sealed class Pane
        {
            internal Unit Unit;
            internal string Name;
            internal int Slot;
            internal Camera Camera;
            internal RenderTexture Texture;
            internal float LostAt = -1f;        // when what it watched was destroyed
            internal bool Lost => LostAt >= 0f;
        }

        private readonly List<Pane> pins = new List<Pane>();
        internal IReadOnlyList<Pane> Pins => pins;

        // ---- the live feed's state, for its window ---------------------------

        private Camera feed;
        private RenderTexture texture;
        internal Texture LiveTexture => texture;
        internal Camera LiveCamera => feed;

        // Set by the UI each frame: nobody is looking, so nothing is rendered.
        internal bool WantLive;

        internal bool HasSubject => (subject != null && !subject.disabled) || Lingering;
        internal string Caption { get; private set; } = "";
        internal Color CaptionColor { get; private set; } = Theme.Text;
        internal int CycleIndex { get; private set; } = -1;
        internal int CycleCount => weapons.Count;
        internal bool Held => watched != null;

        // Counts our weapons as they leave, so the UI can open the feed when
        // there is suddenly something worth watching.
        internal int WeaponsAway { get; private set; }

        private Unit subject;
        private string subjectName = "";
        private bool chasing;          // subject is a weapon of ours, not a target
        private float nextSubjectCheck;
        private float lingerUntil = -1f;
        private bool Lingering => Time.unscaledTime < lingerUntil;

        // Our weapons in the air, oldest first. Missile carries no spawn time,
        // so first sighting is recorded here -- that gives both a true age order
        // and a stable number to put on screen, rather than a label that changes
        // as rounds come and go.
        private sealed class Tracked
        {
            internal Missile Missile;
            internal int Id;
            internal float FirstSeen;
        }
        private readonly List<Tracked> weapons = new List<Tracked>();
        private int nextId = 1;
        private Missile watched;       // explicitly cycled to; null means follow the oldest

        private static float Linger => Mathf.Max(0f, Settings.FeedLinger.Value);

        // ---- pinning -----------------------------------------------------------

        internal bool IsPinned(Unit unit)
        {
            foreach (Pane pane in pins) if (pane.Unit == unit && !pane.Lost) return true;
            return false;
        }

        internal Pane Find(int slot)
        {
            foreach (Pane pane in pins) if (pane.Slot == slot) return pane;
            return null;
        }

        internal bool Pin(Unit unit, out string reason)
        {
            if (unit == null) { reason = "Nothing to pin."; return false; }
            if (IsPinned(unit)) { Unpin(unit); reason = "Feed closed."; return true; }
            int slot = FreeSlot();
            if (slot < 0) { reason = "All " + MaxPinned + " pinned feeds are in use."; return false; }

            var pane = new Pane { Unit = unit, Slot = slot, Name = unit.definition?.unitName ?? unit.name };
            Build(pane);
            pins.Add(pane);
            reason = "Watching " + pane.Name + ".";
            return true;
        }

        internal void Unpin(Unit unit)
        {
            for (int i = pins.Count - 1; i >= 0; i--)
                if (pins[i].Unit == unit) { Release(pins[i]); pins.RemoveAt(i); }
        }

        internal void Unpin(int slot)
        {
            for (int i = pins.Count - 1; i >= 0; i--)
                if (pins[i].Slot == slot) { Release(pins[i]); pins.RemoveAt(i); }
        }

        private int FreeSlot()
        {
            for (int slot = 1; slot <= MaxPinned; slot++) if (Find(slot) == null) return slot;
            return -1;
        }

        private void Build(Pane pane)
        {
            int width = Mathf.Clamp(Settings.FeedResolution.Value, 160, 1920);
            pane.Texture = new RenderTexture(width, width * 9 / 16, 24) { name = "Naval Power pinned feed " + pane.Slot };
            pane.Texture.Create();
            pane.Camera = MakeCamera("Naval Power pinned camera " + pane.Slot, pane.Texture, -11f);
        }

        private void Release(Pane pane)
        {
            if (pane.Camera != null) Destroy(pane.Camera.gameObject);
            if (pane.Texture != null) { pane.Texture.Release(); Destroy(pane.Texture); }
        }

        // ---- controls ------------------------------------------------------------

        // Step through our weapons. Falling off either end returns to following
        // the oldest, so the control always has somewhere to go.
        internal void Step(int direction)
        {
            if (weapons.Count == 0) return;
            int index = watched != null ? IndexOf(watched) : 0;
            index += direction;
            if (index < 0 || index >= weapons.Count) { watched = null; return; }
            watched = weapons[index].Missile;
            lingerUntil = -1f;                  // a deliberate choice ends a hold
            nextSubjectCheck = 0f;
        }

        // The wheel over a feed zooms that camera, proportionally, so a narrow
        // field zooms in finer steps than a wide one rather than jumping.
        internal static void Zoom(Camera camera, float delta)
        {
            if (camera == null) return;
            float step = camera.fieldOfView * 0.12f * -delta;
            camera.fieldOfView = Mathf.Clamp(camera.fieldOfView + step, 3f, 90f);
        }

        // ---- frame -----------------------------------------------------------------

        private void LateUpdate()
        {
            if (!Settings.TargetFeed.Value || !CommandState.Active) return;

            RenderPins();

            // Noticed before a new subject is chosen, or the next check would
            // cut straight to the next thing and the destruction would be missed.
            if (WantLive && subject != null && subject.disabled && lingerUntil < 0f) BeginLinger();

            // The subject is chosen whether or not the window is open, so the UI
            // can see a weapon leave and open the feed for it.
            if (Time.unscaledTime >= nextSubjectCheck)
            {
                nextSubjectCheck = Time.unscaledTime + 0.4f;
                TrackWeapons(CommandState.Ship);
                if (!Lingering) ChooseSubject();
            }

            if (!WantLive || !HasSubject) return;

            EnsureCamera();
            if (feed == null) return;
            // While holding, the camera stays exactly where it was when the
            // subject went, looking at what is left of it.
            if (!Lingering) Frame();
            feed.Render();
        }

        private void BeginLinger()
        {
            lingerUntil = Time.unscaledTime + Linger;
            Caption = (chasing ? "IMPACT  ·  " : "DESTROYED  ·  ") + subjectName;
            CaptionColor = Theme.Bad;
        }

        private void RenderPins()
        {
            for (int i = pins.Count - 1; i >= 0; i--)
            {
                Pane pane = pins[i];
                bool gone = pane.Unit == null || pane.Unit.disabled;
                if (gone && !pane.Lost) pane.LostAt = Time.unscaledTime;
                if (pane.Lost && Time.unscaledTime - pane.LostAt > Linger)
                {
                    // Held long enough to see it go; now close, rather than leave
                    // a frozen frame on screen.
                    Release(pane);
                    pins.RemoveAt(i);
                    continue;
                }
                if (pane.Camera == null) continue;
                // A destroyed unit's wreck may already be gone, so the camera
                // holds its last pose rather than being re-aimed at nothing.
                if (!pane.Lost) FrameOn(pane.Camera, pane.Unit);
                pane.Camera.Render();
            }
        }

        // ---- choosing and framing the live subject ---------------------------------

        private void TrackWeapons(Ship ship)
        {
            for (int i = weapons.Count - 1; i >= 0; i--)
                if (weapons[i].Missile == null || weapons[i].Missile.disabled) weapons.RemoveAt(i);
            if (ship == null) return;

            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Missile missile) || missile.disabled) continue;
                if (missile.owner != ship) continue;
                bool known = false;
                foreach (Tracked entry in weapons) if (entry.Missile == missile) { known = true; break; }
                if (known) continue;
                weapons.Add(new Tracked { Missile = missile, Id = nextId++, FirstSeen = Time.timeSinceLevelLoad });
                WeaponsAway++;
            }
            weapons.Sort((a, b) => a.FirstSeen.CompareTo(b.FirstSeen));   // oldest first

            if (watched != null && (watched.disabled || IndexOf(watched) < 0)) watched = null;
        }

        private int IndexOf(Missile missile)
        {
            for (int i = 0; i < weapons.Count; i++) if (weapons[i].Missile == missile) return i;
            return -1;
        }

        // A weapon of ours in the air, else whatever we have ordered engaged,
        // else the contact the cursor is over.
        private void ChooseSubject()
        {
            Ship ship = CommandState.Ship;
            Unit previous = subject;
            lingerUntil = -1f;
            if (ship == null) { subject = null; return; }

            if (weapons.Count > 0)
            {
                // The oldest is the one closest to arriving, and the one the
                // commander has been waiting on longest.
                subject = watched ?? weapons[0].Missile;
                chasing = true;
            }
            else
            {
                chasing = false;
                subject = null;
                var engaged = new List<Unit>();
                ShipWeapons.CollectTargets(ship, engaged);
                foreach (Unit target in engaged)
                    if (target != null && !target.disabled) { subject = target; break; }
                if (subject == null)
                {
                    Unit hovered = MapCommand.Instance?.HoverUnit;
                    subject = hovered != null && hovered != ship ? hovered : null;
                }
            }
            if (subject != previous && subject != null) subjectName = subject.definition?.unitName ?? subject.name;
        }

        private void Frame()
        {
            if (subject == null || subject.disabled) return;
            float size = Mathf.Max(subject.maxRadius, 4f);
            Vector3 focus = subject.transform.position;

            if (chasing)
            {
                // Over the weapon's shoulder, so the target grows in frame.
                Vector3 travel = subject.rb != null && subject.rb.velocity.sqrMagnitude > 1f
                    ? subject.rb.velocity.normalized : subject.transform.forward;
                feed.transform.position = focus - travel * (size * 6f + 18f) + Vector3.up * (size + 4f);
                feed.transform.rotation = Quaternion.LookRotation(travel, Vector3.up);
                int index = IndexOf(subject as Missile);
                CycleIndex = index;
                Caption = (index >= 0 ? "WPN " + weapons[index].Id + "  ·  " : "") + subjectName +
                    (watched != null ? "  ·  held" : "");
                CaptionColor = Theme.Weapon;
                return;
            }

            CycleIndex = -1;
            FrameOn(feed, subject);
            Caption = "ENGAGING  ·  " + subjectName;
            CaptionColor = Theme.Text;
        }

        // Shared framing: from our own side of it, looking in, which is the angle
        // that shows what is coming at it.
        private static void FrameOn(Camera camera, Unit unit)
        {
            float size = Mathf.Max(unit.maxRadius, 4f);
            Vector3 focus = unit.transform.position;
            Ship ship = CommandState.Ship;
            Vector3 fromUs = ship != null ? (focus - ship.transform.position) : unit.transform.forward;
            fromUs.y = 0f;
            if (fromUs.sqrMagnitude < 1f) fromUs = unit.transform.forward;
            fromUs.Normalize();
            float range = size * 8f + 40f;
            camera.transform.position = focus - fromUs * range + Vector3.up * (size * 2f + 12f);
            camera.transform.rotation = Quaternion.LookRotation(focus - camera.transform.position, Vector3.up);
        }

        // ---- cameras ---------------------------------------------------------------

        private void EnsureCamera()
        {
            if (feed != null) return;
            int width = Mathf.Clamp(Settings.FeedResolution.Value, 160, 1920);
            texture = new RenderTexture(width, width * 9 / 16, 24) { name = "Naval Power target feed" };
            texture.Create();
            feed = MakeCamera("Naval Power feed camera", texture, -10f);
        }

        private Camera MakeCamera(string name, RenderTexture target, float depthOffset)
        {
            Camera main = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (main == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            Camera camera = go.AddComponent<Camera>();
            // Inherit the scene's own setup -- clipping, culling, effects -- then
            // redirect it off-screen. Anything we chose ourselves would drift
            // from the game's look after an update.
            camera.CopyFrom(main);
            camera.targetTexture = target;
            camera.depth = main.depth + depthOffset;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = Settings.FeedFieldOfView.Value;
            // The game renders through URP, where a second camera left enabled
            // joins the pipeline's own stack rather than quietly filling a
            // texture. Declare it a base camera and drive it by hand: disabled
            // so URP will not draw it, rendered explicitly into our texture.
            UniversalAdditionalCameraData urp = camera.GetUniversalAdditionalCameraData();
            if (urp != null)
            {
                urp.renderType = CameraRenderType.Base;
                urp.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
                urp.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;
            }
            camera.enabled = false;
            return camera;
        }

        private void OnDestroy()
        {
            if (feed != null) Destroy(feed.gameObject);
            if (texture != null) { texture.Release(); Destroy(texture); }
            foreach (Pane pane in pins) Release(pane);
            pins.Clear();
        }
    }
}

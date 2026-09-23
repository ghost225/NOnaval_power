using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace NavalPower
{
    // A second camera on whatever the ship is currently engaging, rendered into
    // a panel beside the command bar.
    //
    // Subject priority is what the commander would actually want to watch: a
    // weapon of ours in flight beats the target it is going toward, because the
    // interesting moment is the intercept.
    internal sealed class TargetFeed : MonoBehaviour
    {
        // One live pane that follows the action, plus up to three pinned to a
        // particular unit. Each is a camera and a texture of its own, which is
        // why the count is capped rather than open-ended.
        internal const int MaxPinned = 3;

        private sealed class Pane
        {
            internal Camera Camera;
            internal RenderTexture Texture;
            internal RectTransform Panel;
            internal RawImage Image;
            internal Text Caption;
            internal Unit Pinned;           // null on the live pane
        }

        private readonly System.Collections.Generic.List<Pane> pinned = new System.Collections.Generic.List<Pane>();
        private Pane live;

        private Camera feed;
        private RenderTexture texture;
        private RectTransform panel;
        private RawImage image;
        private Text caption;
        private Font font;
        private RectTransform root;

        private float baseInset = 12f;
        private float topInset;

        // Keeps the feeds clear of the air operations bar when that is showing.
        internal void SetTopInset(float inset)
        {
            topInset = inset;
            if (panel != null) panel.anchoredPosition = new Vector2(-12f, -(baseInset + inset));
            Layout();
        }

        private Unit subject;
        private bool chasing;          // subject is a weapon of ours, not a target
        private float nextSubjectCheck;

        // Our weapons in the air, oldest first. Missile carries no spawn time,
        // so first sighting is recorded here -- that gives both a true age
        // order and a stable number to put on screen, rather than a label that
        // changes as rounds come and go.
        private sealed class Tracked
        {
            internal Missile Missile;
            internal int Id;
            internal float FirstSeen;
        }
        private readonly System.Collections.Generic.List<Tracked> weapons = new System.Collections.Generic.List<Tracked>();
        private int nextId = 1;
        private Missile watched;       // explicitly cycled to; null means follow the oldest
        private Text counter;
        private Button previous, next;

        internal void Build(RectTransform parent, Font uiFont)
        {
            font = uiFont;
            root = parent;

            panel = Box("Target feed", parent, Theme.Surface);
            panel.anchorMin = new Vector2(1, 1);
            panel.anchorMax = new Vector2(1, 1);
            panel.pivot = new Vector2(1, 1);
            panel.sizeDelta = new Vector2(Settings.FeedWidth.Value, Settings.FeedWidth.Value * 9f / 16f + 26f);
            panel.anchoredPosition = new Vector2(-12f, -12f);
            baseInset = 12f;

            RectTransform edge = Box("edge", panel, Theme.Dim(Theme.Accent, 0.6f));
            edge.anchorMin = new Vector2(0, 1); edge.anchorMax = new Vector2(1, 1);
            edge.pivot = new Vector2(0.5f, 1);
            edge.sizeDelta = new Vector2(0, 2f);
            edge.anchoredPosition = Vector2.zero;

            var view = new GameObject("View", typeof(RectTransform), typeof(RawImage));
            view.transform.SetParent(panel, false);
            var viewRect = (RectTransform)view.transform;
            viewRect.anchorMin = new Vector2(0, 0); viewRect.anchorMax = new Vector2(1, 1);
            viewRect.offsetMin = new Vector2(2f, 2f);
            viewRect.offsetMax = new Vector2(-2f, -26f);
            image = view.GetComponent<RawImage>();
            image.raycastTarget = false;

            caption = Label(panel, "", Theme.CaptionSize, TextAnchor.MiddleLeft, Theme.Text);
            caption.rectTransform.anchorMin = new Vector2(0, 1);
            caption.rectTransform.anchorMax = new Vector2(1, 1);
            caption.rectTransform.pivot = new Vector2(0, 1);
            caption.rectTransform.anchoredPosition = new Vector2(10f, -4f);
            caption.rectTransform.sizeDelta = new Vector2(-116f, 22f);

            // Stepping through our own weapons, oldest first.
            previous = Button(panel, "◀", -74f, -3f, 24f, () => Step(-1));
            counter = Label(panel, "", Theme.LabelSize, TextAnchor.MiddleCenter, Theme.TextMuted);
            counter.rectTransform.anchorMin = counter.rectTransform.anchorMax = new Vector2(1, 1);
            counter.rectTransform.pivot = new Vector2(1, 1);
            counter.rectTransform.anchoredPosition = new Vector2(-38f, -3f);
            counter.rectTransform.sizeDelta = new Vector2(36f, 20f);
            next = Button(panel, "▶", -10f, -3f, 24f, () => Step(1));

            panel.gameObject.SetActive(false);
        }

        internal bool IsPinned(Unit unit)
        {
            foreach (Pane pane in pinned) if (pane.Pinned == unit) return true;
            return false;
        }

        internal int PinnedCount => pinned.Count;

        internal bool Pin(Unit unit, out string reason)
        {
            if (unit == null) { reason = "Nothing to pin."; return false; }
            if (IsPinned(unit)) { Unpin(unit); reason = "Feed closed."; return true; }
            if (pinned.Count >= MaxPinned)
            { reason = "All " + MaxPinned + " pinned feeds are in use."; return false; }

            var pane = new Pane { Pinned = unit };
            BuildPane(pane, "Pinned feed " + (pinned.Count + 1));
            pinned.Add(pane);
            Layout();
            reason = "Watching " + (unit.definition?.unitName ?? unit.name) + ".";
            return true;
        }

        internal void Unpin(Unit unit)
        {
            for (int i = pinned.Count - 1; i >= 0; i--)
            {
                if (pinned[i].Pinned != unit) continue;
                Release(pinned[i]);
                pinned.RemoveAt(i);
            }
            Layout();
        }

        internal void UnpinAll()
        {
            foreach (Pane pane in pinned) Release(pane);
            pinned.Clear();
            Layout();
        }

        private void Release(Pane pane)
        {
            if (pane.Camera != null) Destroy(pane.Camera.gameObject);
            if (pane.Texture != null) { pane.Texture.Release(); Destroy(pane.Texture); }
            if (pane.Panel != null) Destroy(pane.Panel.gameObject);
        }

        // Stacked down the right edge, under the live pane.
        private void Layout()
        {
            float width = Settings.FeedWidth.Value;
            float height = width * 9f / 16f + 26f;
            float y = baseInset + topInset + height + 8f;
            foreach (Pane pane in pinned)
            {
                if (pane.Panel == null) continue;
                pane.Panel.sizeDelta = new Vector2(width, height);
                pane.Panel.anchoredPosition = new Vector2(-12f, -y);
                y += height + 8f;
            }
        }

        private void BuildPane(Pane pane, string name)
        {
            pane.Panel = Box(name, root, Theme.Surface);
            pane.Panel.anchorMin = new Vector2(1, 1);
            pane.Panel.anchorMax = new Vector2(1, 1);
            pane.Panel.pivot = new Vector2(1, 1);
            pane.Panel.sizeDelta = new Vector2(Settings.FeedWidth.Value, Settings.FeedWidth.Value * 9f / 16f + 26f);

            RectTransform edge = Box("edge", pane.Panel, Theme.Dim(Theme.Passive, 0.7f));
            edge.anchorMin = new Vector2(0, 1); edge.anchorMax = new Vector2(1, 1);
            edge.pivot = new Vector2(0.5f, 1);
            edge.sizeDelta = new Vector2(0, 2f);
            edge.anchoredPosition = Vector2.zero;

            var view = new GameObject("View", typeof(RectTransform), typeof(RawImage));
            view.transform.SetParent(pane.Panel, false);
            var viewRect = (RectTransform)view.transform;
            viewRect.anchorMin = Vector2.zero; viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = new Vector2(2f, 2f);
            viewRect.offsetMax = new Vector2(-2f, -26f);
            pane.Image = view.GetComponent<RawImage>();
            pane.Image.raycastTarget = false;

            pane.Caption = Label(pane.Panel, "", Theme.CaptionSize, TextAnchor.MiddleLeft, Theme.Passive);
            pane.Caption.rectTransform.anchorMin = new Vector2(0, 1);
            pane.Caption.rectTransform.anchorMax = new Vector2(1, 1);
            pane.Caption.rectTransform.pivot = new Vector2(0, 1);
            pane.Caption.rectTransform.anchoredPosition = new Vector2(10f, -4f);
            pane.Caption.rectTransform.sizeDelta = new Vector2(-20f, 22f);

            int width = Mathf.Clamp(Settings.FeedResolution.Value, 160, 1920);
            pane.Texture = new RenderTexture(width, width * 9 / 16, 24);
            pane.Texture.Create();
            pane.Image.texture = pane.Texture;

            Camera main = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (main == null) return;
            var go = new GameObject("Naval Power pinned camera");
            go.transform.SetParent(transform, false);
            pane.Camera = go.AddComponent<Camera>();
            pane.Camera.CopyFrom(main);
            pane.Camera.targetTexture = pane.Texture;
            pane.Camera.depth = main.depth - 11f;
            pane.Camera.clearFlags = CameraClearFlags.Skybox;
            pane.Camera.fieldOfView = Settings.FeedFieldOfView.Value;
            UniversalAdditionalCameraData urp = pane.Camera.GetUniversalAdditionalCameraData();
            if (urp != null) urp.renderType = CameraRenderType.Base;
            pane.Camera.enabled = false;
        }

        private void RenderPinned()
        {
            for (int i = pinned.Count - 1; i >= 0; i--)
            {
                Pane pane = pinned[i];
                if (pane.Pinned == null || pane.Pinned.disabled)
                {
                    // Whatever it was watching is gone; close the pane rather
                    // than leave a frozen frame on screen.
                    Release(pane);
                    pinned.RemoveAt(i);
                    Layout();
                    continue;
                }
                if (pane.Camera == null) continue;
                pane.Panel.gameObject.SetActive(true);
                FrameOn(pane.Camera, pane.Pinned);
                pane.Caption.text = "PINNED  ·  " + (pane.Pinned.definition?.unitName ?? pane.Pinned.name);
                pane.Camera.Render();
            }
        }

        // Shared framing: from our own side of it, looking in.
        private void FrameOn(Camera camera, Unit unit)
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

        private void EnsureCamera()
        {
            if (feed != null) return;
            Camera main = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (main == null) return;

            int width = Mathf.Clamp(Settings.FeedResolution.Value, 160, 1920);
            texture = new RenderTexture(width, width * 9 / 16, 24) { name = "Naval Power target feed" };
            texture.Create();

            var go = new GameObject("Naval Power feed camera");
            go.transform.SetParent(transform, false);
            feed = go.AddComponent<Camera>();
            // Inherit the scene's own setup -- clipping, culling, effects
            // settings -- then redirect it off-screen. Anything we chose
            // ourselves would drift from the game's look after an update.
            feed.CopyFrom(main);
            feed.targetTexture = texture;
            feed.depth = main.depth - 10f;
            feed.clearFlags = CameraClearFlags.Skybox;
            feed.fieldOfView = Settings.FeedFieldOfView.Value;

            // The game renders through URP, where a second camera left enabled
            // joins the pipeline's own stack rather than quietly filling a
            // texture. Declare it a base camera and then drive it by hand:
            // disabled so URP will not draw it, rendered explicitly once a
            // frame into our texture.
            UniversalAdditionalCameraData urp = feed.GetUniversalAdditionalCameraData();
            if (urp != null)
            {
                urp.renderType = CameraRenderType.Base;
                urp.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
                urp.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;
            }
            feed.enabled = false;

            if (image != null) image.texture = texture;
        }

        private void LateUpdate()
        {
            bool wanted = Settings.TargetFeed.Value && CommandState.Active;
            if (!wanted) { Hide(); foreach (Pane pane in pinned) if (pane.Panel != null) pane.Panel.gameObject.SetActive(false); return; }

            RenderPinned();

            if (Time.unscaledTime >= nextSubjectCheck)
            {
                nextSubjectCheck = Time.unscaledTime + 0.4f;
                ChooseSubject();
            }
            if (subject == null || subject.disabled) { Hide(); return; }

            EnsureCamera();
            if (feed == null) { Hide(); return; }

            panel.gameObject.SetActive(true);
            Frame();
            // Explicit render: the camera stays disabled so the pipeline leaves
            // it alone, and this fills the texture on our own schedule.
            feed.Render();
        }

        private void Hide()
        {
            if (panel != null) panel.gameObject.SetActive(false);
        }

        private void TrackWeapons(Ship ship)
        {
            for (int i = weapons.Count - 1; i >= 0; i--)
                if (weapons[i].Missile == null || weapons[i].Missile.disabled) weapons.RemoveAt(i);

            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Missile missile) || missile.disabled) continue;
                if (missile.owner != ship) continue;
                bool known = false;
                foreach (Tracked entry in weapons) if (entry.Missile == missile) { known = true; break; }
                if (known) continue;
                weapons.Add(new Tracked { Missile = missile, Id = nextId++, FirstSeen = Time.timeSinceLevelLoad });
            }
            weapons.Sort((a, b) => a.FirstSeen.CompareTo(b.FirstSeen));   // oldest first

            if (watched != null && (watched.disabled || IndexOf(watched) < 0)) watched = null;
        }

        private int IndexOf(Missile missile)
        {
            for (int i = 0; i < weapons.Count; i++) if (weapons[i].Missile == missile) return i;
            return -1;
        }

        // Step through our weapons. Falling off either end returns to following
        // the oldest, so the control always has somewhere to go.
        private void Step(int direction)
        {
            if (weapons.Count == 0) return;
            int index = watched != null ? IndexOf(watched) : 0;
            index += direction;
            if (index < 0 || index >= weapons.Count) { watched = null; return; }
            watched = weapons[index].Missile;
        }

        // A weapon of ours in the air, else whatever we have ordered engaged,
        // else the contact the cursor is over.
        private void ChooseSubject()
        {
            Ship ship = CommandState.Ship;
            if (ship == null) { subject = null; return; }

            TrackWeapons(ship);
            if (weapons.Count > 0)
            {
                // The oldest is the one closest to arriving, and the one the
                // commander has been waiting on longest.
                Missile own = watched ?? weapons[0].Missile;
                subject = own;
                chasing = true;
                return;
            }

            chasing = false;
            var engaged = new System.Collections.Generic.List<Unit>();
            ShipWeapons.CollectTargets(ship, engaged);
            foreach (Unit target in engaged)
                if (target != null && !target.disabled) { subject = target; return; }

            Unit hovered = MapCommand.Instance?.HoverUnit;
            subject = hovered != null && hovered != ship ? hovered : null;
        }

        private void Frame()
        {
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
                string tag = index >= 0 ? "WPN " + weapons[index].Id + "  ·  " : "";
                caption.text = tag + (subject.definition?.unitName ?? subject.name) +
                    (watched != null ? "  ·  held" : "");
                caption.color = Theme.Weapon;
                ShowCycling(index);
                return;
            }

            // Viewed from our own side of it, which is the angle that shows
            // what is coming at it.
            Ship ship = CommandState.Ship;
            Vector3 fromUs = ship != null ? (focus - ship.transform.position) : subject.transform.forward;
            fromUs.y = 0f;
            if (fromUs.sqrMagnitude < 1f) fromUs = subject.transform.forward;
            fromUs.Normalize();

            float range = size * 8f + 40f;
            feed.transform.position = focus - fromUs * range + Vector3.up * (size * 2f + 12f);
            feed.transform.rotation = Quaternion.LookRotation(focus - feed.transform.position, Vector3.up);
            caption.text = "ENGAGING  ·  " + (subject.definition?.unitName ?? subject.name);
            caption.color = Theme.Text;
            ShowCycling(-1);
        }

        private void ShowCycling(int index)
        {
            bool many = weapons.Count > 1 && index >= 0;
            previous.gameObject.SetActive(many);
            next.gameObject.SetActive(many);
            counter.gameObject.SetActive(many);
            if (many) counter.text = (index + 1) + "/" + weapons.Count;
        }

        private Button Button(RectTransform parent, string label, float x, float y, float size, System.Action action)
        {
            RectTransform rect = Box(label, parent, Theme.Control);
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(size, 20f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(() => action());
            Text text = Label(rect, label, Theme.LabelSize, TextAnchor.MiddleCenter, Theme.Text);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero; text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private void OnDestroy()
        {
            if (feed != null) Destroy(feed.gameObject);
            if (texture != null) { texture.Release(); Destroy(texture); }
        }

        // ---- local helpers, mirroring the command bar's ----------------------

        private RectTransform Box(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        private Text Label(RectTransform parent, string value, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font; text.fontSize = size; text.color = color; text.text = value; text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}

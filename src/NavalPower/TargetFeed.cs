using UnityEngine;
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
        private Camera feed;
        private RenderTexture texture;
        private RectTransform panel;
        private RawImage image;
        private Text caption;
        private Font font;

        private Unit subject;
        private bool chasing;          // subject is a weapon of ours, not a target
        private float nextSubjectCheck;

        internal void Build(RectTransform root, Font uiFont)
        {
            font = uiFont;

            panel = Box("Target feed", root, Theme.Surface);
            panel.anchorMin = new Vector2(1, 1);
            panel.anchorMax = new Vector2(1, 1);
            panel.pivot = new Vector2(1, 1);
            panel.sizeDelta = new Vector2(Settings.FeedWidth.Value, Settings.FeedWidth.Value * 9f / 16f + 26f);
            panel.anchoredPosition = new Vector2(-12f, -12f);

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
            caption.rectTransform.sizeDelta = new Vector2(-20f, 22f);

            panel.gameObject.SetActive(false);
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
            feed.fieldOfView = Settings.FeedFieldOfView.Value;
            if (image != null) image.texture = texture;
        }

        private void LateUpdate()
        {
            bool wanted = Settings.TargetFeed.Value && CommandState.Active;
            if (!wanted) { Hide(); return; }

            if (Time.unscaledTime >= nextSubjectCheck)
            {
                nextSubjectCheck = Time.unscaledTime + 0.4f;
                ChooseSubject();
            }
            if (subject == null || subject.disabled) { Hide(); return; }

            EnsureCamera();
            if (feed == null) { Hide(); return; }

            panel.gameObject.SetActive(true);
            feed.enabled = true;
            Frame();
        }

        private void Hide()
        {
            if (panel != null) panel.gameObject.SetActive(false);
            if (feed != null) feed.enabled = false;
        }

        // A weapon of ours in the air, else whatever we have ordered engaged,
        // else the contact the cursor is over.
        private void ChooseSubject()
        {
            Ship ship = CommandState.Ship;
            if (ship == null) { subject = null; return; }

            // Whichever of ours is furthest along, which is the one about to
            // matter; there is no spawn time to sort on.
            Missile own = null;
            float leading = -1f;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Missile missile) || missile.disabled) continue;
                if (missile.owner != ship) continue;
                float travelled = FastMath.Distance(missile.GlobalPosition(), ship.GlobalPosition());
                if (travelled > leading) { leading = travelled; own = missile; }
            }
            if (own != null) { subject = own; chasing = true; return; }

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
                caption.text = "WEAPON IN FLIGHT  ·  " + (subject.definition?.unitName ?? subject.name);
                caption.color = Theme.Weapon;
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

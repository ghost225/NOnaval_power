using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NavalPower
{
    // The formation editor, after Sea Power's: a polar plot centred on the
    // guide -- range rings, bearing spokes, the guide's heading as an arrow --
    // and a dot for every escort that is dragged to where it should be. Up is
    // the guide's course, or north when stations are fixed to north. A ship
    // sails for its new station the moment it is dropped.
    internal sealed partial class CommandUi
    {
        private const float PlotSize = 440f;
        private FormationPlot formationPlot;
        private float plotRangeMetres = 3000f;
        private TaskForce plotForce;

        private void FormationEditorPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            TaskForce force = TaskForces.Of(ship);
            if (force == null) { s.Title("FORMATION EDITOR"); s.Info("Not in a task force.", Theme.TextMuted); return; }
            if (force != plotForce)
            {
                plotForce = force;
                float furthest = 1000f;
                foreach (Escort escort in force.Escorts) furthest = Mathf.Max(furthest, escort.Range);
                plotRangeMetres = Mathf.Ceil(furthest * 1.25f / 500f) * 500f;
            }

            s.Title("FORMATION EDITOR  ·  " + force.Name.ToUpperInvariant() + "  ·  " +
                (force.Formation == Formation.Custom ? "custom" : force.Formation.ToString().ToLowerInvariant()));
            s.Field(force.Name, value =>
            {
                value = (value ?? "").Trim();
                if (value.Length > 0) force.Name = value;
            });
            Button[] shapes = s.Group(FormationNames, i => TaskForces.SetFormation(force, (Formation)i));
            if (force.Formation != Formation.Custom) shapes[(int)force.Formation].image.color = Theme.AccentFill;
            Button north = s.Row(force.FixedNorth ? "Up is north  ·  stations fixed to true bearings"
                : "Up is the guide's course  ·  stations turn with it", () => force.FixedNorth = !force.FixedNorth);
            if (force.FixedNorth) north.image.color = Theme.AccentFill;

            RectTransform area = s.Spacer(PlotSize);
            if (formationPlot == null || formationPlot.transform.parent != area)
            {
                if (formationPlot != null) Destroy(formationPlot.gameObject);
                var go = new GameObject("Formation plot", typeof(RectTransform), typeof(FormationPlot));
                go.transform.SetParent(area, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(PlotSize, PlotSize);
                formationPlot = go.GetComponent<FormationPlot>();
            }
            formationPlot.Show(force, plotRangeMetres);

            s.Info("Plot range  ·  " + UnitConverter.DistanceReading(plotRangeMetres) + "  ·  rings every " +
                UnitConverter.DistanceReading(plotRangeMetres / 4f), Theme.TextMuted);
            s.SliderRow(1000f, 12000f, plotRangeMetres, value => plotRangeMetres = Mathf.Round(value / 250f) * 250f);
            s.Info("Drag a ship to its station; it sails there when dropped. A preset starts over.", Theme.TextFaint);
            s.Row("Back to the task force", () => { Open("tf", TaskForcePage); s.Close(); });
        }
    }

    // Draws the rings, spokes and heading arrow, and keeps one draggable dot
    // per escort in step with the force.
    internal sealed class FormationPlot : MaskableGraphic
    {
        private TaskForce force;
        private float range = 3000f;
        private readonly List<FormationDot> dots = new List<FormationDot>();
        private readonly List<Text> ringLabels = new List<Text>();
        private Text guideLabel;

        internal float Radius => rectTransform.rect.width * 0.5f - 24f;
        internal float PixelsPerMetre => Radius / Mathf.Max(range, 1f);
        internal TaskForce Force => force;

        internal void Show(TaskForce shown, float metres)
        {
            bool changed = shown != force || !Mathf.Approximately(metres, range);
            force = shown;
            range = metres;
            raycastTarget = false;
            color = Color.white;
            if (changed) SetVerticesDirty();
            Sync();
        }

        private void Sync()
        {
            if (guideLabel == null)
            {
                guideLabel = UiKit.Label(rectTransform, "", Theme.CaptionSize, TextAnchor.UpperCenter, Theme.Text);
                guideLabel.rectTransform.sizeDelta = new Vector2(220f, 18f);
                for (int i = 1; i <= 4; i++)
                {
                    Text label = UiKit.Label(rectTransform, "", Theme.CaptionSize - 1, TextAnchor.LowerLeft, Theme.TextFaint);
                    label.rectTransform.sizeDelta = new Vector2(80f, 16f);
                    ringLabels.Add(label);
                }
            }
            guideLabel.text = force.Guide != null ? ShipNames.Of(force.Guide) : "";
            guideLabel.rectTransform.anchoredPosition = new Vector2(0f, -16f);
            for (int i = 0; i < ringLabels.Count; i++)
            {
                float r = Radius * (i + 1) / 4f;
                ringLabels[i].text = UnitConverter.DistanceReading(range * (i + 1) / 4f);
                ringLabels[i].rectTransform.anchoredPosition = new Vector2(r * 0.72f + 42f, r * 0.72f - 4f);
            }

            while (dots.Count < force.Escorts.Count)
            {
                var go = new GameObject("Station", typeof(RectTransform), typeof(Image), typeof(FormationDot));
                go.transform.SetParent(rectTransform, false);
                var dot = go.GetComponent<FormationDot>();
                dot.Build(this);
                dots.Add(dot);
            }
            for (int i = 0; i < dots.Count; i++)
            {
                bool used = i < force.Escorts.Count;
                dots[i].gameObject.SetActive(used);
                if (used) dots[i].Bind(force.Escorts[i]);
            }
        }

        // Plot position for a bearing and range, and back.
        internal Vector2 ToPlot(float bearing, float metres)
        {
            float r = Mathf.Min(metres, range) * PixelsPerMetre;
            float radians = bearing * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * r;
        }

        internal void FromPlot(Vector2 local, out float bearing, out float metres)
        {
            metres = Mathf.Clamp(local.magnitude / PixelsPerMetre, 150f, range);
            bearing = (Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg + 360f) % 360f;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (force == null) return;
            float radius = Radius;
            Color ring = Theme.Dim(Theme.Text, 0.18f), spoke = Theme.Dim(Theme.Text, 0.1f);
            for (int i = 1; i <= 4; i++) Circle(vh, radius * i / 4f, ring, i == 4 ? 1.6f : 1f);
            for (int b = 0; b < 360; b += 30)
            {
                float a = b * Mathf.Deg2Rad;
                Line(vh, Vector2.zero, new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * radius, spoke, 1f);
            }
            // The guide's heading: straight up when stations turn with it;
            // its real course when they are fixed to north.
            float heading = force.FixedNorth ? Mathf.Atan2(force.Course.x, force.Course.z) : 0f;
            Vector2 tip = new Vector2(Mathf.Sin(heading), Mathf.Cos(heading)) * radius;
            Color arrow = Theme.Accent;
            Line(vh, Vector2.zero, tip, arrow, 2f);
            Vector2 back = -tip.normalized * 18f, side = new Vector2(tip.y, -tip.x).normalized * 9f;
            Line(vh, tip, tip + back + side, arrow, 2f);
            Line(vh, tip, tip + back - side, arrow, 2f);
            Square(vh, Vector2.zero, 6f, arrow);
        }

        private void Update()
        {
            // The course swings with the guide when fixed to north; redraw then.
            if (force != null && force.FixedNorth) SetVerticesDirty();
        }

        private static void Circle(VertexHelper vh, float r, Color c, float width)
        {
            const int segments = 72;
            Vector2 last = new Vector2(0f, r);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector2 next = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r;
                Line(vh, last, next, c, width);
                last = next;
            }
        }

        private static void Square(VertexHelper vh, Vector2 at, float half, Color c)
        {
            int start = vh.currentVertCount;
            vh.AddVert(at + new Vector2(-half, -half), c, Vector2.zero);
            vh.AddVert(at + new Vector2(-half, half), c, Vector2.zero);
            vh.AddVert(at + new Vector2(half, half), c, Vector2.zero);
            vh.AddVert(at + new Vector2(half, -half), c, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.0001f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * width * 0.5f;
            int start = vh.currentVertCount;
            vh.AddVert(a - n, c, Vector2.zero);
            vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero);
            vh.AddVert(b - n, c, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }

    // One escort on the plot. Dragged, it moves the escort's station live;
    // otherwise it follows the station as the page refreshes.
    internal sealed class FormationDot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private FormationPlot plot;
        private Escort escort;
        private Image image;
        private Text label;
        private bool dragging;

        internal void Build(FormationPlot owner)
        {
            plot = owner;
            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(14f, 14f);
            image = GetComponent<Image>();
            image.raycastTarget = true;
            label = UiKit.Label(rect, "", Theme.CaptionSize, TextAnchor.UpperCenter, Theme.Text);
            label.rectTransform.anchoredPosition = new Vector2(0f, -12f);
            label.rectTransform.sizeDelta = new Vector2(200f, 16f);
        }

        internal void Bind(Escort shown)
        {
            escort = shown;
            label.supportRichText = true;
            label.text = ShipNames.Of(escort.Ship) + (ShipNames.IsNamed(escort.Ship)
                ? "  " + UiKit.Tint(ShipNames.TypeOf(escort.Ship), Theme.TextMuted) : "") + (escort.Detached ? " (detached)" : "");
            image.color = escort.Detached ? Theme.Dim(Theme.Warn, 0.8f) : escort.ThreatArc ? Theme.Warn : Theme.Accent;
            if (!dragging) ((RectTransform)transform).anchoredPosition = plot.ToPlot(escort.Bearing, escort.Range);
        }

        public void OnBeginDrag(PointerEventData data) { dragging = true; }

        public void OnDrag(PointerEventData data)
        {
            if (escort == null || plot == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(plot.rectTransform, data.position, null, out Vector2 local)) return;
            plot.FromPlot(local, out float bearing, out float metres);
            TaskForces.MoveStation(plot.Force, escort, bearing, metres);
            ((RectTransform)transform).anchoredPosition = plot.ToPlot(bearing, metres);
        }

        public void OnEndDrag(PointerEventData data)
        {
            dragging = false;
            if (escort != null)
                CommandState.Say(ShipNames.Of(escort.Ship) + " · station " + escort.Bearing.ToString("000") + "° " +
                    UnitConverter.DistanceReading(escort.Range));
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // The heading tape across the top of the screen, Sea Power fashion.
    //
    // It reads the camera, not the ship: the tape is for turning what is on
    // screen into a bearing, so it scrolls as the view turns. What the ship is
    // doing is marked on it instead -- its heading and its next waypoint --
    // along with the flight being tasked and the contact under the cursor, all
    // as bearings from the camera so each marker sits over the thing it marks.
    // Anything off the visible arc is pinned to the nearer end, dimmed.
    //
    // Built from our own elements rather than the flight HUD's compass, which
    // lives on a canvas that is off whenever you are not in a cockpit.
    internal sealed partial class CommandUi
    {
        private const float CompassWidth = 720f, CompassHeight = 30f, CompassArc = 120f;
        private const float PixelsPerDegree = CompassWidth / CompassArc;

        private RectTransform compass, compassTape;
        private Text compassReadout;
        private readonly List<Image> compassTicks = new List<Image>();
        private readonly List<Text> compassLabels = new List<Text>();
        private readonly List<Text> compassMarks = new List<Text>();

        private void BuildCompass()
        {
            compass = new GameObject("Compass", typeof(RectTransform)).GetComponent<RectTransform>();
            compass.SetParent(root.transform, false);
            compass.anchorMin = compass.anchorMax = new Vector2(0.5f, 1f);
            compass.pivot = new Vector2(0.5f, 1f);
            compass.sizeDelta = new Vector2(CompassWidth, CompassHeight + 24f);
            compass.anchoredPosition = new Vector2(0f, -6f);

            compassTape = UiKit.Box("Tape", compass, Theme.Dim(Theme.Surface, 0.72f));
            compassTape.anchorMin = compassTape.anchorMax = new Vector2(0.5f, 1f);
            compassTape.pivot = new Vector2(0.5f, 1f);
            compassTape.sizeDelta = new Vector2(CompassWidth, CompassHeight);
            compassTape.anchoredPosition = Vector2.zero;
            compassTape.GetComponent<Image>().raycastTarget = false;
            compassTape.gameObject.AddComponent<RectMask2D>();

            // Every 5° over the arc, with room for one either side.
            int ticks = Mathf.CeilToInt(CompassArc / 5f) + 2;
            for (int i = 0; i < ticks; i++)
            {
                RectTransform tick = UiKit.Box("tick", compassTape, Theme.Dim(Theme.Text, 0.7f));
                tick.anchorMin = tick.anchorMax = new Vector2(0.5f, 0f);
                tick.pivot = new Vector2(0.5f, 0f);
                Image image = tick.GetComponent<Image>();
                image.raycastTarget = false;
                compassTicks.Add(image);
            }
            int labels = Mathf.CeilToInt(CompassArc / 30f) + 2;
            for (int i = 0; i < labels; i++)
            {
                Text label = UiKit.Label(compassTape, "", Theme.CaptionSize, TextAnchor.UpperCenter, Theme.Text);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                label.rectTransform.pivot = new Vector2(0.5f, 1f);
                label.rectTransform.sizeDelta = new Vector2(48f, 16f);
                label.raycastTarget = false;
                compassLabels.Add(label);
            }

            // The lubber line: where the view is pointing.
            RectTransform lubber = UiKit.Box("lubber", compassTape, Theme.Accent);
            lubber.anchorMin = lubber.anchorMax = new Vector2(0.5f, 0f);
            lubber.pivot = new Vector2(0.5f, 0f);
            lubber.sizeDelta = new Vector2(2f, CompassHeight);
            lubber.anchoredPosition = Vector2.zero;
            lubber.GetComponent<Image>().raycastTarget = false;

            RectTransform readout = UiKit.Box("Readout", compass, Theme.TitleBar);
            readout.anchorMin = readout.anchorMax = new Vector2(0.5f, 1f);
            readout.pivot = new Vector2(0.5f, 1f);
            readout.sizeDelta = new Vector2(64f, 22f);
            readout.anchoredPosition = new Vector2(0f, -CompassHeight);
            readout.GetComponent<Image>().raycastTarget = false;
            compassReadout = UiKit.Label(readout, "", Theme.BodySize, TextAnchor.MiddleCenter, Theme.Text);
            UiKit.Fill(compassReadout.rectTransform);
            compassReadout.fontStyle = FontStyle.Bold;
            compassReadout.raycastTarget = false;

            compass.gameObject.SetActive(false);
        }

        private void RefreshCompass(bool flying)
        {
            if (compass == null) return;
            var cameras = SceneSingleton<CameraStateManager>.i;
            bool show = Settings.ShowCompass.Value && !flying && !MapFull && cameras != null && cameras.mainCamera != null;
            compass.gameObject.SetActive(show);
            if (!show) return;

            Transform view = cameras.mainCamera.transform;
            float heading = Heading(view.forward);
            compassReadout.text = Mathf.RoundToInt(heading) % 360 + "°";

            // Ticks and labels scroll; the pools are sized for the arc.
            float first = Mathf.Ceil((heading - CompassArc * 0.5f) / 5f) * 5f;
            int tick = 0, label = 0;
            for (float degrees = first; degrees <= heading + CompassArc * 0.5f && tick < compassTicks.Count; degrees += 5f)
            {
                int whole = ((Mathf.RoundToInt(degrees) % 360) + 360) % 360;
                float x = (degrees - heading) * PixelsPerDegree;
                Image mark = compassTicks[tick++];
                mark.gameObject.SetActive(true);
                bool major = whole % 10 == 0;
                mark.rectTransform.sizeDelta = new Vector2(major ? 2f : 1f, whole % 30 == 0 ? 11f : major ? 8f : 5f);
                mark.rectTransform.anchoredPosition = new Vector2(x, 0f);
                if (whole % 30 != 0 || label >= compassLabels.Count) continue;
                Text text = compassLabels[label++];
                text.gameObject.SetActive(true);
                text.text = Cardinal(whole);
                text.fontStyle = whole % 90 == 0 ? FontStyle.Bold : FontStyle.Normal;
                text.color = whole % 90 == 0 ? Theme.Accent : Theme.Text;
                text.rectTransform.anchoredPosition = new Vector2(x, -1f);
            }
            for (int i = tick; i < compassTicks.Count; i++) compassTicks[i].gameObject.SetActive(false);
            for (int i = label; i < compassLabels.Count; i++) compassLabels[i].gameObject.SetActive(false);

            int used = 0;
            Vector3 from = view.position;
            Ship ship = CommandState.Ship;
            if (ship != null)
            {
                Mark(ref used, heading, Heading(ship.transform.forward), Theme.Accent);
                NavigationSnapshot nav = NavigationOrders.GetSnapshot(ship);
                if (nav?.Waypoints != null && nav.Waypoints.Length > 0)
                    Mark(ref used, heading, Bearing(from, nav.Waypoints[0].ToLocalPosition()), Theme.Dim(Theme.Accent, 0.6f));
            }
            Flight flight = CommandState.SelectedFlight;
            if (flight?.Aircraft != null && !flight.Aircraft.disabled)
                Mark(ref used, heading, Bearing(from, flight.Aircraft.transform.position), FlightIcons.For(flight));
            Unit hovered = MapCommand.Instance?.HoverUnit;
            if (hovered != null && !hovered.disabled && KnownAt(hovered, out Vector3 at))
                Mark(ref used, heading, Bearing(from, at), Theme.Warn);
            for (int i = used; i < compassMarks.Count; i++) compassMarks[i].gameObject.SetActive(false);
        }

        // A marker along the top of the tape, pinned to the nearer end and
        // dimmed when it is off the visible arc.
        private void Mark(ref int used, float heading, float bearing, Color color)
        {
            if (used == compassMarks.Count)
            {
                Text made = UiKit.Label(compassTape, "▼", 13, TextAnchor.UpperCenter, color);
                made.rectTransform.anchorMin = made.rectTransform.anchorMax = new Vector2(0.5f, 0f);
                made.rectTransform.pivot = new Vector2(0.5f, 0f);
                made.rectTransform.sizeDelta = new Vector2(20f, 14f);
                made.raycastTarget = false;
                compassMarks.Add(made);
            }
            Text mark = compassMarks[used++];
            mark.gameObject.SetActive(true);
            float offset = Mathf.DeltaAngle(heading, bearing);
            float limit = CompassArc * 0.5f - 2f;
            bool off = Mathf.Abs(offset) > limit;
            mark.text = off ? (offset < 0f ? "◀" : "▶") : "▼";
            mark.color = off ? Theme.Dim(color, 0.55f) : color;
            mark.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(offset, -limit, limit) * PixelsPerDegree, 13f);
        }

        // Where the faction believes it is -- never better than the track.
        private static bool KnownAt(Unit unit, out Vector3 at)
        {
            at = unit.transform.position;
            FactionHQ hq = CommandState.Hq;
            if (hq == null) return false;
            if (unit.NetworkHQ == hq) return true;
            if (!hq.TryGetKnownPosition(unit, out GlobalPosition known)) return false;
            at = known.ToLocalPosition();
            return true;
        }

        private static float Heading(Vector3 forward) =>
            (Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg + 360f) % 360f;

        private static float Bearing(Vector3 from, Vector3 to) => Heading(to - from);

        private static string Cardinal(int degrees)
        {
            switch (degrees)
            {
                case 0: return "N";
                case 90: return "E";
                case 180: return "S";
                case 270: return "W";
                default: return degrees.ToString("000");
            }
        }
    }
}

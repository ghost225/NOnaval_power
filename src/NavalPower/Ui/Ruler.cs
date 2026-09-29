using NOrders;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // A ruler on the map: click a start point, and a line to the cursor reads
    // out distance and bearing; click again to fix it, again to start a new
    // one. Right-click or Esc puts it away. While it is out, clicks on the map
    // measure rather than select or give orders.
    internal static class Ruler
    {
        internal static bool On { get; private set; }
        internal static bool HasStart { get; private set; }
        internal static bool Fixed { get; private set; }
        internal static GlobalPosition Start { get; private set; }
        internal static GlobalPosition End { get; private set; }
        internal static Vector2 FixedAtScreen { get; private set; }

        internal static void Toggle()
        {
            if (On) { Stop(); return; }
            On = true;
            HasStart = Fixed = false;
            CommandState.Say("Ruler · click the map to start measuring · right-click or Esc to put it away");
        }

        internal static void Stop()
        {
            if (!On) return;
            On = HasStart = Fixed = false;
            CommandState.Say("Ruler put away");
        }

        internal static void Click(GlobalPosition at)
        {
            if (!HasStart || Fixed) { Start = at; HasStart = true; Fixed = false; return; }
            End = at;
            Fixed = true;
            FixedAtScreen = Input.mousePosition;
            CommandState.Say("Ruler · " + Reading(Start, End));
        }

        internal static bool Line(DynamicMap map, out GlobalPosition from, out GlobalPosition to)
        {
            from = Start;
            to = End;
            if (!On || !HasStart || map == null) return false;
            if (!Fixed) to = map.GetCursorCoordinates();
            return true;
        }

        internal static float Distance(GlobalPosition a, GlobalPosition b)
        {
            Vector3 d = b - a;
            d.y = 0f;
            return d.magnitude;
        }

        internal static string Reading(GlobalPosition a, GlobalPosition b)
        {
            Vector3 d = b - a;
            d.y = 0f;
            float bearing = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
            return UnitConverter.DistanceReading(d.magnitude) + "  ·  " + bearing.ToString("000") + "°  ·  back " +
                ((bearing + 180f) % 360f).ToString("000") + "°";
        }
    }

    internal sealed partial class CommandUi
    {
        private RectTransform rulerTag;
        private Text rulerText;

        private void BuildRulerTag()
        {
            rulerTag = UiKit.Box("Ruler", (RectTransform)root.transform, Theme.Dim(Theme.Surface, 0.92f));
            rulerTag.anchorMin = rulerTag.anchorMax = Vector2.zero;
            rulerTag.pivot = new Vector2(0f, 0f);
            rulerTag.sizeDelta = new Vector2(250f, 26f);
            rulerTag.GetComponent<Image>().raycastTarget = false;
            rulerText = UiKit.Label(rulerTag, "", Theme.LabelSize + 1, TextAnchor.MiddleLeft, Theme.Text);
            UiKit.Fill(rulerText.rectTransform, 8f, 0f);
            rulerTag.gameObject.SetActive(false);
        }

        // The reading beside the cursor while measuring, or beside where the
        // ruler was fixed.
        private void RefreshRuler()
        {
            if (rulerTag == null) return;
            if (Ruler.On && Input.GetKeyDown(KeyCode.Escape)) Ruler.Stop();
            var map = SceneSingleton<DynamicMap>.i;
            bool show = Ruler.On && DynamicMap.mapMaximized && Ruler.Line(map, out GlobalPosition from, out GlobalPosition to);
            rulerTag.gameObject.SetActive(show);
            if (!show) return;
            Ruler.Line(map, out from, out to);
            rulerText.text = Ruler.Reading(from, to);
            float scale = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
            Vector2 at = (Ruler.Fixed ? Ruler.FixedAtScreen : (Vector2)Input.mousePosition) / scale + new Vector2(16f, 12f);
            Vector2 room = ((RectTransform)root.transform).rect.size;
            at.x = Mathf.Min(at.x, room.x - rulerTag.sizeDelta.x - 4f);
            at.y = Mathf.Min(at.y, room.y - rulerTag.sizeDelta.y - 4f);
            rulerTag.anchoredPosition = at;
            rulerTag.SetAsLastSibling();
        }
    }
}

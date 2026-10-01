using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // A joystick, drawn: a base, a stick leaning a touch to one side, and a
    // grip at the top. For the take-the-controls button on a flight's row,
    // beside the camera's eye.
    internal sealed class StickGlyph : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float w = r.width, h = r.height;
            float stroke = Mathf.Max(1.5f, w * 0.12f);
            Vector2 bottom = new Vector2(r.center.x, r.yMin);

            // The base: a low, wide block.
            Quad(vh, new Vector2(r.xMin + w * 0.12f, r.yMin), new Vector2(r.xMax - w * 0.12f, r.yMin + h * 0.2f));
            // The stick, from the middle of the base up and a little right.
            Vector2 foot = bottom + new Vector2(0f, h * 0.2f);
            Vector2 top = new Vector2(r.center.x + w * 0.14f, r.yMin + h * 0.7f);
            Line(vh, foot, top, stroke);
            // The grip.
            Disc(vh, top + new Vector2(0f, h * 0.08f), Mathf.Min(w, h) * 0.2f);
        }

        private void Quad(VertexHelper vh, Vector2 min, Vector2 max)
        {
            int s = vh.currentVertCount;
            vh.AddVert(new Vector2(min.x, min.y), color, Vector2.zero);
            vh.AddVert(new Vector2(min.x, max.y), color, Vector2.zero);
            vh.AddVert(new Vector2(max.x, max.y), color, Vector2.zero);
            vh.AddVert(new Vector2(max.x, min.y), color, Vector2.zero);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }

        private void Line(VertexHelper vh, Vector2 a, Vector2 b, float width)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.0001f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * width * 0.5f;
            int s = vh.currentVertCount;
            vh.AddVert(a - n, color, Vector2.zero);
            vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero);
            vh.AddVert(b - n, color, Vector2.zero);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }

        private void Disc(VertexHelper vh, Vector2 centre, float radius)
        {
            int middle = vh.currentVertCount;
            vh.AddVert(centre, color, Vector2.zero);
            for (int i = 0; i <= 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                vh.AddVert(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color, Vector2.zero);
                if (i > 0) vh.AddTriangle(middle, middle + i, middle + i + 1);
            }
        }
    }
}

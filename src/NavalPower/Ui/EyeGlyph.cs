using UnityEngine;
using UnityEngine.UI;
using NOrders;

namespace NavalPower
{
    // A small eye, drawn rather than typed: the game's font has no eye or
    // camera character, and a symbol that comes out as a box says nothing.
    // Almond outline and a filled pupil, in the graphic's colour.
    internal sealed class EyeGlyph : MaskableGraphic
    {
        private const int Segments = 14;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 centre = r.center;
            float halfWidth = r.width * 0.5f, halfHeight = r.height * 0.5f;
            float stroke = Mathf.Max(1.5f, r.height * 0.13f);

            // Upper and lower lids: each a curve from corner to corner.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 previous = centre + new Vector2(-halfWidth, 0f);
                for (int i = 1; i <= Segments; i++)
                {
                    float t = -1f + 2f * i / Segments;
                    Vector2 point = centre + new Vector2(t * halfWidth, side * halfHeight * Mathf.Cos(t * Mathf.PI * 0.5f));
                    Line(vh, previous, point, color, stroke);
                    previous = point;
                }
            }

            // The pupil.
            float radius = halfHeight * 0.48f;
            int middle = vh.currentVertCount;
            vh.AddVert(centre, color, Vector2.zero);
            for (int i = 0; i <= 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                vh.AddVert(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color, Vector2.zero);
                if (i > 0) vh.AddTriangle(middle, middle + i, middle + i + 1);
            }
        }

        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.0001f) return;
            // Stretched a touch along the line, so neighbouring segments overlap
            // at the joints instead of leaving notches.
            Vector2 along = d.normalized * width * 0.3f;
            Vector2 n = new Vector2(-d.y, d.x).normalized * width * 0.5f;
            int start = vh.currentVertCount;
            vh.AddVert(a - along - n, c, Vector2.zero);
            vh.AddVert(a - along + n, c, Vector2.zero);
            vh.AddVert(b + along + n, c, Vector2.zero);
            vh.AddVert(b + along - n, c, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}

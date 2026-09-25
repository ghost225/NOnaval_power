using System;
using UnityEngine;

namespace NavalPower
{
    // Liang-Barsky line/rectangle clipping, shared by the map overlay, and the
    // projection from the world onto the map as it is drawn on screen.
    internal static class MapGeometry
    {
        // Where a world position falls on screen, on the native map as it is
        // currently panned, zoomed and sized. The map canvas is screen-space,
        // so its image's position is already in pixels.
        internal static Vector3 ToScreen(DynamicMap map, GlobalPosition position)
        {
            float factor = 900f * map.mapImage.transform.lossyScale.x / map.mapDimension;
            return map.mapImage.transform.position + new Vector3(position.x, position.z, 0f) * factor;
        }

        internal static bool ClipLine(ref Vector2 a, ref Vector2 b, Rect bounds) =>
            ClipLine(ref a.x, ref a.y, ref b.x, ref b.y, bounds.xMin, bounds.yMin, bounds.xMax, bounds.yMax);

        private static bool ClipLine(ref float ax, ref float ay, ref float bx, ref float by,
            float left, float bottom, float right, float top)
        {
            if (!Finite(ax) || !Finite(ay) || !Finite(bx) || !Finite(by) ||
                !Finite(left) || !Finite(bottom) || !Finite(right) || !Finite(top) ||
                right <= left || top <= bottom) return false;
            float dx = bx - ax, dy = by - ay, first = 0f, last = 1f;
            if (!Clip(-dx, ax - left, ref first, ref last) || !Clip(dx, right - ax, ref first, ref last) ||
                !Clip(-dy, ay - bottom, ref first, ref last) || !Clip(dy, top - ay, ref first, ref last)) return false;
            bx = ax + dx * last; by = ay + dy * last;
            ax += dx * first; ay += dy * first;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool Clip(float direction, float distance, ref float first, ref float last)
        {
            if (Math.Abs(direction) < .0001f) return distance >= 0f;
            float ratio = distance / direction;
            if (direction < 0f) { if (ratio > last) return false; first = Math.Max(first, ratio); }
            else { if (ratio < first) return false; last = Math.Min(last, ratio); }
            return true;
        }
    }
}

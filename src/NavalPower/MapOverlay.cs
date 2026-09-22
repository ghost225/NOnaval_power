using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // Draws orders onto the native map: the plotted route, the selected
    // weapon's range envelope, engagement lines to ordered targets, and a mark
    // on the hovered contact's last known position.
    internal sealed class MapOverlay : MaskableGraphic
    {
        private static readonly Color RouteColor = new Color(0.26f, 0.86f, 0.92f, 0.9f);
        private static readonly Color MaxRangeColor = new Color(0.95f, 0.45f, 0.3f, 0.8f);
        private static readonly Color MinRangeColor = new Color(1f, 0.8f, 0.24f, 0.9f);
        private static readonly Color EngageColor = new Color(1f, 0.38f, 0.32f, 0.9f);
        private static readonly Color TrackColor = new Color(1f, 0.75f, 0.3f, 0.95f);

        private DynamicMap map;
        private Rect clip;
        private readonly Vector3[] corners = new Vector3[4];
        private readonly List<Unit> engaged = new List<Unit>();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Ship ship = CommandState.Ship;
            if (ship == null || !DynamicMap.mapMaximized) return;
            map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.mapImage == null || !map.gameObject.activeInHierarchy) return;

            clip = rectTransform.rect;
            clip.yMin += 170;                      // Keep strokes clear of the command bar.
            // The terrain image is a world extent, not the visible viewport:
            // orders and ranges continue over water past its edge.
            if (map.mapBackground == null || !Intersect(map.mapBackground.rectTransform)) return;
            for (Transform parent = map.mapBackground.transform; parent != null; parent = parent.parent)
            {
                var rectMask = parent.GetComponent<RectMask2D>();
                var stencil = parent.GetComponent<Mask>();
                if (((rectMask != null && rectMask.isActiveAndEnabled) || (stencil != null && stencil.isActiveAndEnabled)) &&
                    !Intersect(parent as RectTransform)) return;
            }

            Vector2 center = Project(ship.GlobalPosition());

            WeaponCommandInfo weapon = CommandState.SelectedWeapon();
            if (weapon != null)
            {
                Circle(vh, ship.GlobalPosition(), weapon.MaxRange, MaxRangeColor);
                Circle(vh, ship.GlobalPosition(), weapon.MinRange, MinRangeColor);
            }

            engaged.Clear();
            ShipWeapons.CollectTargets(ship, engaged);
            foreach (Unit target in engaged)
            {
                if (target == null || target.disabled) continue;
                Vector2 point = Project(target.GlobalPosition());
                Line(vh, center, point, EngageColor, 1.6f);
                Diamond(vh, point, 6f, EngageColor);
            }

            Unit hovered = MapCommand.Instance?.HoverUnit;
            if (hovered != null && hovered != ship && ship.NetworkHQ != null &&
                ship.NetworkHQ.TryGetKnownPosition(hovered, out GlobalPosition known))
            {
                Vector2 point = Project(known);
                Line(vh, point + Vector2.left * 7, point + Vector2.right * 7, TrackColor, 2f);
                Line(vh, point + Vector2.down * 7, point + Vector2.up * 7, TrackColor, 2f);
            }

            NavigationSnapshot nav = NavigationOrders.GetSnapshot(ship);
            GlobalPosition[] route = nav?.Waypoints;
            if (route == null) return;
            Vector2 previous = center;
            for (int i = 0; i < route.Length && i < 64; i++)
            {
                Vector2 point = Project(route[i]);
                Line(vh, previous, point, RouteColor, 2f);
                Diamond(vh, point, 5f, RouteColor);
                previous = point;
            }
        }

        private void Update()
        {
            // The map pans and zooms under us, so the mesh is rebuilt each frame
            // while it is open rather than on order changes alone.
            if (CommandState.Active && DynamicMap.mapMaximized) SetVerticesDirty();
        }

        private bool Intersect(RectTransform boundary)
        {
            if (boundary == null) return false;
            boundary.GetWorldCorners(corners);
            var lower = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var upper = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < 4; i++)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,
                    RectTransformUtility.WorldToScreenPoint(null, corners[i]), null, out Vector2 point);
                lower = Vector2.Min(lower, point);
                upper = Vector2.Max(upper, point);
            }
            clip = Rect.MinMaxRect(Mathf.Max(clip.xMin, lower.x), Mathf.Max(clip.yMin, lower.y),
                Mathf.Min(clip.xMax, upper.x), Mathf.Min(clip.yMax, upper.y));
            return clip.width > 0f && clip.height > 0f;
        }

        // Inverse of DynamicMap.GetCursorCoordinates, using its live transform.
        private Vector2 Project(GlobalPosition position)
        {
            float factor = 900f * map.mapImage.transform.lossyScale.x / map.mapDimension;
            Vector3 screen = map.mapImage.transform.position + new Vector3(position.x, position.z, 0f) * factor;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, null, out Vector2 local);
            return local;
        }

        private void Circle(VertexHelper vh, GlobalPosition center, float radius, Color color)
        {
            if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius)) return;
            const int segments = 96;
            Vector2 previous = Project(center + new Vector3(radius, 0f, 0f));
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 point = Project(center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
                Line(vh, previous, point, color, 1.6f);
                previous = point;
            }
        }

        private void Diamond(VertexHelper vh, Vector2 point, float size, Color color)
        {
            Line(vh, point + Vector2.left * size, point + Vector2.up * size, color, 2f);
            Line(vh, point + Vector2.up * size, point + Vector2.right * size, color, 2f);
            Line(vh, point + Vector2.right * size, point + Vector2.down * size, color, 2f);
            Line(vh, point + Vector2.down * size, point + Vector2.left * size, color, 2f);
        }

        private void Line(VertexHelper vh, Vector2 from, Vector2 to, Color color, float width)
        {
            // Clip the centreline inset by half the stroke, or thick lines bleed
            // past the map edge.
            float inset = Mathf.Max(0f, width * .5f);
            Rect strokeClip = Rect.MinMaxRect(clip.xMin + inset, clip.yMin + inset, clip.xMax - inset, clip.yMax - inset);
            if (strokeClip.width <= 0f || strokeClip.height <= 0f) return;
            if (!MapGeometry.ClipLine(ref from, ref to, strokeClip)) return;
            Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized * width * 0.5f;
            int start = vh.currentVertCount;
            vh.AddVert(from - normal, color, Vector2.zero);
            vh.AddVert(from + normal, color, Vector2.zero);
            vh.AddVert(to + normal, color, Vector2.zero);
            vh.AddVert(to - normal, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}

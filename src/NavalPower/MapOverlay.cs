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
        private static readonly Color RadarColor = new Color(0.45f, 0.95f, 0.6f, 0.4f);
        private static readonly Color OwnTrackColor = new Color(0.45f, 0.95f, 0.6f, 0.85f);
        private static readonly Color DatalinkColor = new Color(0.55f, 0.7f, 1f, 0.7f);
        private static readonly Color EsmColor = new Color(1f, 0.62f, 0.2f, 0.92f);
        private static readonly Color EsmStaleColor = new Color(1f, 0.62f, 0.2f, 0.42f);

        private DynamicMap map;
        private Rect clip;
        private readonly Vector3[] corners = new Vector3[4];
        private readonly List<Unit> engaged = new List<Unit>();
        private readonly List<float> radarRanges = new List<float>();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            drewLastFrame = false;
            Ship ship = CommandState.Ship;
            if (ship == null || !DynamicMap.mapMaximized) return;
            map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.mapImage == null || !map.gameObject.activeInHierarchy) return;
            drewLastFrame = true;

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

            // What the ship is currently lighting up. Nothing is drawn under
            // EMCON, which is the point: the picture goes quiet with the ship.
            radarRanges.Clear();
            Sensors.CollectActiveRanges(ship, radarRanges);
            foreach (float range in radarRanges) Circle(vh, ship.GlobalPosition(), range, RadarColor);

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

            // Mark hostile contacts by where the track comes from: a filled
            // tick for our own sensors, a hollow one for datalink. Under EMCON
            // the map turns blue, which is the cost of going silent made visible.
            if (ship.NetworkHQ != null)
            {
                foreach (Unit unit in UnitRegistry.allUnits)
                {
                    if (unit == null || unit.disabled || unit == ship || unit is Missile) continue;
                    if (unit.NetworkHQ == null || unit.NetworkHQ == ship.NetworkHQ) continue;
                    if (!ship.NetworkHQ.TryGetKnownPosition(unit, out GlobalPosition at)) continue;
                    bool ours = TrackPicture.IsOwn(ship, unit);
                    Vector2 mark = Project(at);
                    Color color = ours ? OwnTrackColor : DatalinkColor;
                    if (ours)
                    {
                        Line(vh, mark + new Vector2(-5f, -5f), mark + new Vector2(5f, 5f), color, 2f);
                        Line(vh, mark + new Vector2(-5f, 5f), mark + new Vector2(5f, -5f), color, 2f);
                    }
                    else Diamond(vh, mark, 6f, color);
                }
            }

            // Passive emission estimates. Symbol by emitter family, sized error
            // ellipse on the one under the cursor -- these are bearings, not fixes.
            EsmContact hoveredEsm = MapCommand.Instance?.HoverEsm;
            foreach (EsmContact contact in Esm.GetContacts(ship))
            {
                Vector2 point = Project(contact.Position);
                Color color = contact.Stale ? EsmStaleColor : EsmColor;
                switch (contact.Class)
                {
                    case EmitterClass.Airborne:            // caret, pointing up
                        Line(vh, point + new Vector2(-7f, -4f), point + new Vector2(0f, 7f), color, 2f);
                        Line(vh, point + new Vector2(0f, 7f), point + new Vector2(7f, -4f), color, 2f);
                        break;
                    case EmitterClass.Land:                // square, planted
                        Line(vh, point + new Vector2(-6f, -6f), point + new Vector2(6f, -6f), color, 2f);
                        Line(vh, point + new Vector2(6f, -6f), point + new Vector2(6f, 6f), color, 2f);
                        Line(vh, point + new Vector2(6f, 6f), point + new Vector2(-6f, 6f), color, 2f);
                        Line(vh, point + new Vector2(-6f, 6f), point + new Vector2(-6f, -6f), color, 2f);
                        break;
                    default:                               // surface: hull-ish half diamond
                        Line(vh, point + new Vector2(-7f, 0f), point + new Vector2(0f, -6f), color, 2f);
                        Line(vh, point + new Vector2(0f, -6f), point + new Vector2(7f, 0f), color, 2f);
                        Line(vh, point + new Vector2(7f, 0f), point + new Vector2(-7f, 0f), color, 2f);
                        break;
                }
                // A short stub back down the measured bearing, so the geometry
                // of the estimate is visible rather than implied.
                float radians = contact.BearingDegrees * Mathf.Deg2Rad;
                Vector2 inward = new Vector2(-Mathf.Sin(radians), -Mathf.Cos(radians));
                Line(vh, point + inward * 10f, point + inward * 22f, color, 1.2f);

                if (hoveredEsm != null && hoveredEsm.Id == contact.Id)
                    Ellipse(vh, contact.Position, contact.RadialUncertaintyMetres,
                        contact.CrossRangeUncertaintyMetres, contact.BearingDegrees, color);
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

        private bool drewLastFrame;

        private void Update()
        {
            // The map pans and zooms under us, so the mesh is rebuilt each frame
            // while it is open. One further rebuild after it closes is needed to
            // clear the last mesh, or the strokes stay burnt onto the screen.
            bool live = CommandState.Active && DynamicMap.mapMaximized;
            if (live || drewLastFrame) SetVerticesDirty();
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

        // Error ellipse: long axis along the measured bearing, short across it.
        private void Ellipse(VertexHelper vh, GlobalPosition centre, float radial, float crossRange,
            float bearingDegrees, Color color)
        {
            if (radial <= 0f || crossRange <= 0f) return;
            float bearing = bearingDegrees * Mathf.Deg2Rad;
            Vector3 along = new Vector3(Mathf.Sin(bearing), 0f, Mathf.Cos(bearing));
            Vector3 across = new Vector3(along.z, 0f, -along.x);
            const int segments = 72;
            Vector2 previous = Project(centre + along * radial);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 offset = along * (Mathf.Cos(angle) * radial) + across * (Mathf.Sin(angle) * crossRange);
                Vector2 point = Project(centre + offset);
                Line(vh, previous, point, color, 1.4f);
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

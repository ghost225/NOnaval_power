using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // The map in three sizes: off, docked in a window the size of a camera
    // feed, and full screen.
    //
    // Docked is the real map, not a copy. It stays logically maximized, so
    // native pan and zoom, icon clicks, our right-click orders and every
    // overlay work exactly as they do full screen -- all of them measure from
    // the map's own on-screen rect and scale. It is drawn inside a window
    // whose body is a hole: each frame the native map is scaled and moved to
    // fill that hole, and its full-screen backdrop is hidden, so the world
    // shows around it.
    //
    // The native map zooms and pans wherever the cursor is, which full screen
    // is everywhere. Docked, that would steal every orbit and zoom of the world
    // camera, so its controls only run with the cursor over it.
    internal sealed partial class CommandUi
    {
        private bool docked;
        private Surface mapWindow;
        private RectTransform fullBar;
        private readonly List<Graphic> hiddenGraphics = new List<Graphic>();
        private readonly List<GameObject> hiddenObjects = new List<GameObject>();
        private bool reportedBackdrop;
        private bool mapDrag;

        internal bool MapDocked => docked && DynamicMap.mapMaximized;
        internal bool MapFull => DynamicMap.mapMaximized && !docked;

        // Whether the map owns this point: all of the screen when it is full,
        // only its own square when docked.
        internal bool MapCovers(Vector2 point)
        {
            if (!DynamicMap.mapMaximized) return false;
            if (!docked) return true;
            var map = SceneSingleton<DynamicMap>.i;
            return map != null && map.IsCursorInMapRectangle() && !OverOurWindows(point);
        }

        // Whether the native map's own controls should run this frame.
        internal bool MapControlsAllowed()
        {
            if (!MapDocked) return true;
            var map = SceneSingleton<DynamicMap>.i;
            if (map == null) return true;
            bool over = map.IsCursorInMapRectangle() && !OverOurWindows(Input.mousePosition);
            // A drag that starts on the map may carry on off it.
            if (Input.GetMouseButtonDown(0)) mapDrag = over;
            if (!Input.GetMouseButton(0)) mapDrag = false;
            return over || mapDrag;
        }

        private bool OverOurWindows(Vector2 point)
        {
            if (context != null && context.Contains(point)) return true;
            foreach (Surface window in windows.Values)
                if (window != mapWindow && window.Contains(point)) return true;
            return strip != null && RectTransformUtility.RectangleContainsScreenPoint(strip, point);
        }

        private static float MapSide => Settings.FeedWidth.Value;

        // ---- changing size ---------------------------------------------------

        private void ToggleMap()
        {
            if (DynamicMap.mapMaximized) CloseMap();
            else DockMap();
        }

        private void DockMap()
        {
            var map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;
            if (!DynamicMap.mapMaximized) map.Maximize();
            if (!DynamicMap.mapMaximized) return;              // not allowed to open just now
            docked = true;
            HideBackdrop(map);
            // Maximizing also raises the spectator and airbase panels, which
            // belong to the full-screen map, not to a window beside the world.
            SceneSingleton<GameplayUI>.i?.HideSpectatorPanel();
            SceneSingleton<GameplayUI>.i?.HideSelectAirbase();

            if (mapWindow == null)
            {
                mapWindow = new Surface("map", windowLayer, canvas, MapSide + 16f, growUp: true, closable: true,
                    minimizable: false);
                mapWindow.PassThrough();
                mapWindow.AddTitleButton("⛶", MaximizeMap);
                mapWindow.OnClosed = () => { if (docked) CloseMap(); };
                windows["map"] = mapWindow;
            }
            bool wasOpen = mapWindow.IsOpen;
            mapWindow.Show(MapPage);
            if (!wasOpen)
            {
                Vector2 room = windowLayer.rect.size;
                mapWindow.Place(new Vector2(16f, room.y - Surface.ReservedTop - mapWindow.Panel.sizeDelta.y));
            }
        }

        private void MaximizeMap()
        {
            var map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;
            Undock();
            // Minimizing and maximizing again is how the map puts itself back:
            // its own layout, anchor and backdrop, exactly as native.
            map.Minimize();
            map.Maximize();
        }

        private void CloseMap()
        {
            Undock();
            SceneSingleton<DynamicMap>.i?.Minimize();
        }

        private void Undock()
        {
            if (!docked) return;
            docked = false;
            RestoreBackdrop();
            var map = SceneSingleton<DynamicMap>.i;
            if (map != null) map.transform.localScale = Vector3.one;
            if (mapWindow != null && mapWindow.IsOpen)
            {
                Surface window = mapWindow;
                window.OnClosed = null;             // closing the frame is not closing the map here
                window.Close();
                window.OnClosed = () => { if (docked) CloseMap(); };
            }
        }

        private void MapPage(Surface s)
        {
            s.Title("MAP");
            s.Spacer(MapSide);
        }

        // ---- every frame -----------------------------------------------------

        private void LateUpdate()
        {
            if (root == null || !root.activeSelf) return;
            var map = SceneSingleton<DynamicMap>.i;

            // The game's own map key, or anything else, can take the map away.
            if (docked && !DynamicMap.mapMaximized) Undock();
            if (docked && map != null) FitMap(map);
            RefreshFullBar();
        }

        // Scale and move the native map so its square exactly fills the hole
        // in the window. Both canvases are screen-space overlays, so their world
        // positions are already screen pixels and can be compared directly.
        private void FitMap(DynamicMap map)
        {
            if (mapWindow == null || !mapWindow.IsOpen || mapWindow.ViewRect == null || map.mapBackground == null) return;
            var corners = new Vector3[4];
            mapWindow.ViewRect.GetWorldCorners(corners);
            float want = Mathf.Min(corners[2].x - corners[0].x, corners[2].y - corners[0].y);
            Vector3 centre = (corners[0] + corners[2]) * 0.5f;

            RectTransform background = map.mapBackground.rectTransform;
            Transform frame = map.transform;
            float have = background.rect.width * background.lossyScale.x;
            if (have > 0.01f) frame.localScale = frame.localScale * (want / have);
            Vector3 middle = background.TransformPoint(background.rect.center);
            frame.position += centre - middle;
        }

        // ---- the backdrop --------------------------------------------------------

        // Whatever the maximized map canvas draws that is not the map: the
        // dimmed backdrop, and any panels beside it. Graphics on the map's own
        // ancestors are switched off; siblings that do not hold the map are
        // hidden. Everything is put back exactly as found.
        private void HideBackdrop(DynamicMap map)
        {
            RestoreBackdrop();
            if (map.maximizedMapCanvas == null) return;
            Transform canvasRoot = map.maximizedMapCanvas.transform;
            Transform frame = map.transform;
            var names = new List<string>();

            if (frame.IsChildOf(canvasRoot))
            {
                Transform below = frame;
                for (Transform node = frame.parent; node != null; node = node.parent)
                {
                    foreach (Graphic graphic in node.GetComponents<Graphic>())
                        if (graphic.enabled) { graphic.enabled = false; hiddenGraphics.Add(graphic); names.Add(node.name + " (graphic)"); }
                    foreach (Transform child in node)
                        if (child != below && child.gameObject.activeSelf)
                        { child.gameObject.SetActive(false); hiddenObjects.Add(child.gameObject); names.Add(child.name); }
                    if (node == canvasRoot) break;
                    below = node;
                }
            }
            else
            {
                foreach (Graphic graphic in canvasRoot.GetComponents<Graphic>())
                    if (graphic.enabled) { graphic.enabled = false; hiddenGraphics.Add(graphic); names.Add(canvasRoot.name + " (graphic)"); }
                foreach (Transform child in canvasRoot)
                    if (child.gameObject.activeSelf)
                    { child.gameObject.SetActive(false); hiddenObjects.Add(child.gameObject); names.Add(child.name); }
            }

            // Said once, because which of these the game has is a guess until
            // it has been seen, and if docking hides something it should not,
            // this names it.
            if (!reportedBackdrop)
            {
                reportedBackdrop = true;
                Plugin.Log.LogInfo("[map] docked · hid " + (names.Count == 0 ? "nothing" : string.Join(", ", names)));
            }
        }

        private void RestoreBackdrop()
        {
            foreach (Graphic graphic in hiddenGraphics) if (graphic != null) graphic.enabled = true;
            foreach (GameObject hidden in hiddenObjects) if (hidden != null) hidden.SetActive(true);
            hiddenGraphics.Clear();
            hiddenObjects.Clear();
        }

        // ---- full screen: the way back to the window ---------------------------

        private void BuildFullBar()
        {
            fullBar = UiKit.Box("Map controls", (RectTransform)root.transform, Theme.Dim(Theme.Surface, 0.92f));
            fullBar.anchorMin = fullBar.anchorMax = new Vector2(0.5f, 1f);
            fullBar.pivot = new Vector2(0.5f, 1f);
            fullBar.sizeDelta = new Vector2(212f, 36f);
            fullBar.anchoredPosition = new Vector2(0f, -8f);
            UiKit.Button(fullBar, "▭  DOCK MAP", 4, 4, 150, 28, DockMap);
            UiKit.Button(fullBar, "✕", 158, 4, 50, 28, CloseMap);
            fullBar.gameObject.SetActive(false);
        }

        private void RefreshFullBar()
        {
            if (fullBar == null) return;
            bool show = CommandState.Active && !PilotSeat.Active && MapFull;
            fullBar.gameObject.SetActive(show);
            if (show) fullBar.SetAsLastSibling();
        }
    }
}

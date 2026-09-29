using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using NOrders;

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
        private readonly List<GameObject> hiddenPanels = new List<GameObject>();
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

        private RectTransform resizeGrip;

        internal bool OverResizeGrip(Vector2 point) =>
            resizeGrip != null && resizeGrip.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(resizeGrip, point);

        private bool OverOurWindows(Vector2 point)
        {
            if (OverResizeGrip(point)) return true;
            if (context != null && context.Contains(point)) return true;
            foreach (Surface window in windows.Values)
                if (window != mapWindow && window.Contains(point)) return true;
            return strip != null && RectTransformUtility.RectangleContainsScreenPoint(strip, point);
        }

        // The docked map's size, set with its resize grip and remembered.
        private float dockSide = -1f;
        private float MapSide
        {
            get
            {
                if (dockSide < 0f)
                {
                    try { dockSide = PlayerPrefs.GetFloat("NavalPower.mapDock", Settings.FeedWidth.Value); }
                    catch { dockSide = Settings.FeedWidth.Value; }
                }
                return dockSide;
            }
        }

        internal void ResizeDock(float change, bool done)
        {
            if (mapWindow == null) return;
            float most = Mathf.Max(300f, windowLayer.rect.height - Surface.ReservedBottom - Surface.ReservedTop - 60f);
            dockSide = Mathf.Clamp(MapSide + change, 240f, most);
            mapWindow.SetWidth(dockSide + 16f);
            if (done) try { PlayerPrefs.SetFloat("NavalPower.mapDock", dockSide); PlayerPrefs.Save(); } catch { }
        }

        // ---- changing size ---------------------------------------------------

        // The MAP tool opens the map full screen, as M does; docking it beside
        // the world is the full-screen bar's DOCK MAP.
        private void ToggleMap()
        {
            if (DynamicMap.mapMaximized) CloseMap();
            else SceneSingleton<DynamicMap>.i?.Maximize();
        }

        private void DockMap()
        {
            var map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;
            if (!DynamicMap.mapMaximized) map.Maximize();
            if (!DynamicMap.mapMaximized) return;              // not allowed to open just now
            docked = true;
            HideBackdrop(map);
            HideSidePanels();
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
                mapWindow.AddTitleButton("↔", Ruler.Toggle);
                AddResizeGrip(mapWindow.Panel);
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

        private bool fullFitted;

        private void LateUpdate()
        {
            var map = SceneSingleton<DynamicMap>.i;
            // Out of command with the map still stretched to the screen: put it
            // back to the game's own layout.
            if ((root == null || !root.activeSelf) && fullFitted) ReleaseFull(map);
            if (root == null || !root.activeSelf) return;

            // The game's own map key, or anything else, can take the map away.
            if (docked && !DynamicMap.mapMaximized) Undock();
            if (docked && map != null) { if (fullFitted) Unstretch(map); FitMap(map); }

            // Undocked in command, the map fills the screen edge to edge rather
            // than the game's centred square; its top and bottom run off-screen.
            bool full = MapFull && CommandState.Active && !PilotSeat.Active;
            if (full && map != null) { FitToScreen(map); fullFitted = true; }
            else if (fullFitted && !docked) ReleaseFull(map);
            RefreshFullBar();
            CommandChrome();
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
            Fit(map, centre, want);
        }

        // Widen the map's window to the whole screen without scaling the map.
        // The terrain and icons are placed by the map's own display factor,
        // not by the size of its panel, so enlarging the panel shows more of
        // the map at its normal scale: icons stay their normal size and zoom
        // works as the game's does, with open space round the map when fully
        // zoomed out. (Scaling the map up instead made everything huge and
        // left no room to zoom out.)
        private Vector2 nativeSize, nativeBackground;

        private void FitToScreen(DynamicMap map)
        {
            if (map.mapBackground == null) return;
            var root = (RectTransform)map.transform;
            if (!fullFitted) { nativeSize = root.sizeDelta; nativeBackground = map.mapBackground.rectTransform.sizeDelta; }
            if (root.localScale != Vector3.one) root.localScale = Vector3.one;
            float units = Mathf.Max(root.lossyScale.x, 0.0001f);
            Vector2 size = new Vector2(Screen.width, Screen.height) / units;
            if ((root.sizeDelta - size).sqrMagnitude > 1f) root.sizeDelta = size;
            RectTransform background = map.mapBackground.rectTransform;
            Vector2 withMargin = size + new Vector2(20f, 20f);
            if ((background.sizeDelta - withMargin).sqrMagnitude > 1f) background.sizeDelta = withMargin;
            Vector3 centre = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, root.position.z);
            if ((root.position - centre).sqrMagnitude > 1f) root.position = centre;
        }

        // Docking straight from full screen: give the map back its own square
        // panel before the dock scales it into the window.
        private void Unstretch(DynamicMap map)
        {
            fullFitted = false;
            if (map.mapBackground == null) return;
            ((RectTransform)map.transform).sizeDelta = nativeSize;
            map.mapBackground.rectTransform.sizeDelta = nativeBackground;
        }

        private void ReleaseFull(DynamicMap map)
        {
            fullFitted = false;
            if (map == null) return;
            // Minimizing and maximizing is how the map lays itself out afresh.
            if (DynamicMap.mapMaximized) { map.Minimize(); map.Maximize(); }
            else map.transform.localScale = Vector3.one;
        }

        private static void Fit(DynamicMap map, Vector3 centre, float want)
        {
            RectTransform background = map.mapBackground.rectTransform;
            Transform frame = map.transform;
            float have = background.rect.width * background.lossyScale.x;
            if (have > 0.01f) frame.localScale = frame.localScale * (want / have);
            Vector3 middle = background.TransformPoint(background.rect.center);
            frame.position += centre - middle;
        }

        // The option buttons down either side of the full-screen map are not on
        // the map's canvas at all -- they are VirtualMFD, on the gameplay canvas,
        // switched on by the map's maximize event. Docked, the map is still
        // maximized, so they stayed up beside a map no longer there. They are
        // put away through the game's own map-closed handler, and held off
        // until the map is undocked, when its own events decide again.
        private void HideSidePanels()
        {
            foreach (VirtualMFD panel in Resources.FindObjectsOfTypeAll<VirtualMFD>())
            {
                if (!panel.gameObject.scene.IsValid() || !panel.gameObject.activeSelf) continue;
                panel.VirtualMFD_onMapMinimized();
                panel.gameObject.SetActive(false);
                hiddenPanels.Add(panel.gameObject);
            }
        }

        private void RestoreSidePanels()
        {
            foreach (GameObject panel in hiddenPanels) if (panel != null) panel.SetActive(true);
            hiddenPanels.Clear();
        }

        // In command mode the game's own map chrome belongs to a pilot, not a
        // commander: the option panels down either side of the map (VirtualMFD)
        // and the "select aircraft" panel at the bottom are kept away, however
        // the map was opened -- M, the MAP tool, or docked. They come back when
        // command ends, as the game left them.
        private float nextChromeSweep;

        private void CommandChrome()
        {
            bool commanding = CommandState.Active && !PilotSeat.Active;
            if (!commanding)
            {
                if (hiddenPanels.Count > 0) RestoreSidePanels();
                return;
            }
            if (Time.unscaledTime < nextChromeSweep) return;
            nextChromeSweep = Time.unscaledTime + 0.25f;
            if (DynamicMap.mapMaximized) HideSidePanels();
            SceneSingleton<GameplayUI>.i?.HideSelectAirbase();
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
                Diag.Ui("[map] docked · hid " + (names.Count == 0 ? "nothing" : string.Join(", ", names)));
            }
        }

        private void RestoreBackdrop()
        {
            foreach (Graphic graphic in hiddenGraphics) if (graphic != null) graphic.enabled = true;
            foreach (GameObject hidden in hiddenObjects) if (hidden != null) hidden.SetActive(true);
            hiddenGraphics.Clear();
            hiddenObjects.Clear();
        }

        // A grip in the docked map's bottom-right corner: drag it to resize.
        private void AddResizeGrip(RectTransform panel)
        {
            var go = new GameObject("Resize grip", typeof(RectTransform), typeof(Image), typeof(MapResizeGrip));
            go.transform.SetParent(panel, false);
            var grip = resizeGrip = (RectTransform)go.transform;
            grip.anchorMin = grip.anchorMax = new Vector2(1f, 0f);
            grip.pivot = new Vector2(1f, 0f);
            grip.sizeDelta = new Vector2(22f, 22f);
            grip.anchoredPosition = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.004f);   // catches the drag, draws nothing
            go.GetComponent<MapResizeGrip>().Owner = this;
            // Three diagonal ticks, the usual corner-grip mark.
            for (int i = 0; i < 3; i++)
            {
                RectTransform tick = UiKit.Box("tick", grip, Theme.Dim(Theme.Accent, 0.9f));
                tick.anchorMin = tick.anchorMax = new Vector2(1f, 0f);
                tick.pivot = new Vector2(0.5f, 0.5f);
                float length = 6f + i * 5f;
                tick.sizeDelta = new Vector2(length, 2f);
                tick.anchoredPosition = new Vector2(-4f - length * 0.35f, 4f + length * 0.35f);
                tick.localRotation = Quaternion.Euler(0f, 0f, 45f);
                tick.GetComponent<Image>().raycastTarget = false;
            }
        }

        // ---- full screen: the way back to the window ---------------------------

        private void BuildFullBar()
        {
            fullBar = UiKit.Box("Map controls", (RectTransform)root.transform, Theme.Dim(Theme.Surface, 0.92f));
            fullBar.anchorMin = fullBar.anchorMax = new Vector2(0.5f, 1f);
            fullBar.pivot = new Vector2(0.5f, 1f);
            fullBar.sizeDelta = new Vector2(316f, 36f);
            fullBar.anchoredPosition = new Vector2(0f, -8f);
            UiKit.Button(fullBar, "↔  RULER", 4, 4, 100, 28, Ruler.Toggle);
            UiKit.Button(fullBar, "▭  DOCK MAP", 108, 4, 150, 28, DockMap);
            UiKit.Button(fullBar, "✕", 262, 4, 50, 28, CloseMap);
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

    internal sealed class MapResizeGrip : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        internal CommandUi Owner;

        public void OnDrag(PointerEventData data)
        {
            if (Owner == null) return;
            Canvas canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
            // Right and down both grow it: a square map follows the larger pull.
            float change = (data.delta.x - data.delta.y) * 0.5f / scale;
            Owner.ResizeDock(change, done: false);
        }

        public void OnEndDrag(PointerEventData data) => Owner?.ResizeDock(0f, done: true);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // The command interface: a slim status strip along the bottom, a row of
    // tools at its right end, and the windows those tools open.
    //
    // Modelled on Sea Power rather than on a dashboard. The strip says what the
    // ship is doing at a glance; everything else is a window you open when you
    // want it and close when you are done, and several can be open at once so
    // orders can be stacked without re-opening anything. A window opens as a
    // drop-up above its tool, and dragging its title bar turns it into a free
    // window that remembers where it was left.
    //
    // The pages the windows show live in ShipWindows, AirWindows and
    // ContextMenus. This file is the frame they sit in.
    internal sealed partial class CommandUi : MonoBehaviour
    {
        private Font font;
        private Canvas canvas;
        private TargetFeed feedView;
        private GameObject root;
        private RectTransform strip, windowLayer, menuLayer, hover, seatBar;
        private Text stripName, stripNav, stripStatus, stripState, hoverText, seatLabel;
        private float nextRefresh;

        private const float StripHeight = 36f;

        // ---- the outside world's view of the UI ------------------------------

        internal bool PopupOpen => context != null && context.IsOpen;

        // While a flight's window is open the map tasks that flight rather than
        // the ship, so a route can be laid down leg by leg.
        internal bool Pinned => windows.TryGetValue("flight", out Surface s) && s.IsOpen;

        internal bool ZoomedAFeed(float delta) => feedView != null && feedView.HandleScroll(delta);

        // Windows refresh on their own timer and reuse their rows rather than
        // rebuilding them, so there is nothing left to do here.
        internal void RefreshPinned() { }

        internal bool Contains(Transform candidate) =>
            root != null && candidate != null && (candidate == root.transform || candidate.IsChildOf(root.transform));

        internal bool PointerInside()
        {
            if (root == null || !root.activeSelf) return false;
            Vector2 point = Input.mousePosition;
            if (feedView != null && feedView.PointerOverAnyFeed()) return true;
            if (seatBar != null && seatBar.gameObject.activeSelf &&
                RectTransformUtility.RectangleContainsScreenPoint(seatBar, point)) return true;
            if (strip != null && strip.gameObject.activeSelf &&
                RectTransformUtility.RectangleContainsScreenPoint(strip, point)) return true;
            if (context != null && context.Contains(point)) return true;
            foreach (Surface window in windows.Values)
                if (window.Contains(point)) return true;
            return false;
        }

        // The right-click menu only; standing windows stay open.
        internal void ClosePopup() => context?.Close();

        // Leaving command: nothing should go on tasking a flight, and a menu
        // about the old ship means nothing on the next. Other windows are left
        // as they were, and come back when command resumes.
        internal void Tidy()
        {
            context?.Close();
            if (windows.TryGetValue("flight", out Surface flight)) flight.Close();
        }

        // ---- frame -----------------------------------------------------------

        private void Update()
        {
            if (!CommandState.Active && !PilotSeat.Active)
            { if (root != null) root.SetActive(false); return; }
            Ensure();
            root.SetActive(true);

            // In the cockpit the bridge's surfaces are not ours to show: the
            // only thing this UI still owns is the way back out of the seat.
            bool flying = PilotSeat.Active;
            RefreshSeatBar(flying);
            strip.gameObject.SetActive(!flying);
            windowLayer.gameObject.SetActive(!flying);
            menuLayer.gameObject.SetActive(!flying);
            if (flying) { hover.gameObject.SetActive(false); return; }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.2f;
                RefreshStrip();
                foreach (Surface window in windows.Values) window.Render();
                context.Render();
            }
            RefreshHover();
        }

        // ---- the status strip and its tools ----------------------------------

        private sealed class Tool
        {
            internal string Key, Label;
            internal Button Button;
            internal Text Text;
        }
        private readonly List<Tool> tools = new List<Tool>();

        private void BuildStrip()
        {
            strip = UiKit.Box("Status strip", (RectTransform)root.transform, Theme.Surface);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(1, 0);
            strip.pivot = new Vector2(0.5f, 0);
            strip.sizeDelta = new Vector2(0, StripHeight);
            strip.anchoredPosition = Vector2.zero;

            RectTransform edge = UiKit.Box("edge", strip, Theme.Dim(Theme.Accent, 0.5f));
            edge.anchorMin = new Vector2(0, 1); edge.anchorMax = new Vector2(1, 1);
            edge.pivot = new Vector2(0.5f, 1);
            edge.sizeDelta = new Vector2(0, 1.5f);
            edge.anchoredPosition = Vector2.zero;

            stripName = StripText(12, 290, Theme.BodySize + 1, Theme.Text);
            stripNav = StripText(310, 300, Theme.CaptionSize, Theme.TextMuted);
            stripStatus = StripText(620, 440, Theme.CaptionSize, Theme.TextMuted);
            stripState = StripText(1070, 270, Theme.CaptionSize, Theme.TextMuted);

            string[,] defs =
            {
                { "nav", "NAV" }, { "wpn", "WPN" }, { "sns", "SNS" }, { "roe", "ROE" }, { "dmg", "DMG" },
                { "air", "AIR" }, { "rpl", "RPL" }, { "map", "MAP" }, { "exit", "EXIT" }
            };
            int count = defs.GetLength(0);
            const float width = 60f, gap = 4f;
            for (int i = 0; i < count; i++)
            {
                var tool = new Tool { Key = defs[i, 0], Label = defs[i, 1] };
                tool.Button = UiKit.Button(strip, tool.Label, 0, 0, width, StripHeight - 8f, () => Use(tool));
                var rect = (RectTransform)tool.Button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
                rect.pivot = new Vector2(1, 0.5f);
                rect.anchoredPosition = new Vector2(-(12f + (count - 1 - i) * (width + gap)), 0f);
                tool.Text = tool.Button.GetComponentInChildren<Text>();
                tool.Text.fontSize = Theme.CaptionSize;
                tool.Text.supportRichText = true;
                tools.Add(tool);
            }
        }

        private Text StripText(float x, float width, int size, Color color)
        {
            Text text = UiKit.Label(strip, "", size, TextAnchor.MiddleLeft, color);
            text.rectTransform.anchorMin = new Vector2(0, 0); text.rectTransform.anchorMax = new Vector2(0, 1);
            text.rectTransform.pivot = new Vector2(0, 0.5f);
            text.rectTransform.anchoredPosition = new Vector2(x, 0f);
            text.rectTransform.sizeDelta = new Vector2(width, 0f);
            text.supportRichText = true;
            return text;
        }

        private void Use(Tool tool)
        {
            switch (tool.Key)
            {
                case "exit":
                    MapCommand.Instance?.LeaveForNativeFlow();
                    return;
                case "map":
                    var map = SceneSingleton<DynamicMap>.i;
                    if (map == null) return;
                    if (DynamicMap.mapMaximized) map.Minimize(); else map.Maximize();
                    return;
                default:
                    ToggleWindow(tool.Key, tool);
                    return;
            }
        }

        private void RefreshStrip()
        {
            Ship ship = CommandState.Ship;
            if (ship == null) return;

            stripName.text = ship.definition?.unitName ?? ship.name;

            NavigationSnapshot nav = NavigationOrders.GetSnapshot(ship);
            float heading = (ship.transform.eulerAngles.y + 360f) % 360f;
            stripNav.text = (nav != null
                ? "Spd " + Speed(nav.ActualSpeedKnots) + "  /  " + Speed(nav.OrderedSpeedKnots) + "     "
                : "") + "Crs " + heading.ToString("000") + "°";

            // A fresh order's confirmation outranks the standing status, for as
            // long as it is fresh.
            string said = CommandState.Feedback;
            stripStatus.text = said != null ? UiKit.Tint(said, Theme.Accent)
                : CommandState.SelectedFlight != null
                    ? UiKit.Tint("Tasking " + CommandState.SelectedFlight.Name, Theme.Accent) +
                      "  ·  right-click the map to order it"
                : CommandState.Armed
                    ? "Right-click a contact to engage with " + (CommandState.SelectedWeapon()?.Name ?? "")
                : WeaponOrders.GetStatus(ship);

            bool silent = Sensors.IsSilent(ship);
            EngagementMode roe = EngagementPolicy.GetMode(ship);
            DamageSnapshot damage = DamageControl.GetSnapshot(ship);
            float reserve = damage.DamageControlPoolMax > 0.01f
                ? Mathf.Clamp01(damage.DamageControlPool / damage.DamageControlPoolMax) * 100f : 100f;
            stripState.text =
                UiKit.Tint(silent ? "EMCON" : "RADIATING", silent ? Theme.Good : Theme.Warn) + "   " +
                UiKit.Tint(EngagementPolicy.Describe(roe).ToUpperInvariant(),
                    roe == EngagementMode.WeaponsFree ? Theme.Bad
                    : roe == EngagementMode.WeaponsTight ? Theme.Warn : Theme.Good) + "   " +
                UiKit.Tint("DC " + reserve.ToString("0") + "%", Theme.Scale(reserve));

            foreach (Tool tool in tools)
            {
                bool open = tool.Key == "map" ? DynamicMap.mapMaximized
                    : windows.TryGetValue(tool.Key, out Surface window) && window.IsOpen;
                tool.Button.image.color = open ? Theme.AccentFill : Theme.Control;
                if (tool.Key == "air") tool.Text.text = AirToolLabel();
            }
        }

        private static string Speed(float knots) =>
            UnitConverter.SpeedReadingGround(knots * CommandableShip.MetresPerSecondPerKnot);

        // The air picture in a word: how many are up, and whether any of them
        // needs you now.
        private static string AirToolLabel()
        {
            List<Flight> airborne = FlightOrders.All();
            if (airborne.Count == 0) return "AIR";
            bool trouble = false, busy = false;
            foreach (Flight flight in airborne)
            {
                if (flight.Threat == FlightThreat.Missile || flight.FuelPercent < 25f) trouble = true;
                else if (flight.Interrupted) busy = true;
            }
            string label = "AIR " + airborne.Count;
            return trouble ? UiKit.Tint(label, Theme.Bad) : busy ? UiKit.Tint(label, Theme.Warn) : label;
        }

        // ---- windows ---------------------------------------------------------

        private readonly Dictionary<string, Surface> windows = new Dictionary<string, Surface>();
        private Surface context;

        private Surface Window(string key)
        {
            if (windows.TryGetValue(key, out Surface existing)) return existing;
            float width;
            switch (key)
            {
                case "air": width = 580f; break;
                case "dmg": width = 540f; break;
                case "sns": width = 500f; break;
                case "deck": width = 480f; break;
                case "flight": width = 460f; break;
                case "wpn": width = 460f; break;
                default: width = 400f; break;
            }
            var window = new Surface(key, windowLayer, canvas, width, growUp: true, closable: true);
            if (key == "flight") window.OnClosed = () =>
            {
                CommandState.SelectedFlight = null;
                CommandState.AwaitingCargoZone = null;
            };
            windows[key] = window;
            return window;
        }

        private void ToggleWindow(string key, Tool from)
        {
            Surface window = Window(key);
            if (window.IsOpen) { window.Close(); return; }
            Open(key, PageFor(key), from);
        }

        // Opens a window on a page. A window already open just changes page; a
        // closed one opens where it was last left, or above its tool.
        private Surface Open(string key, Action<Surface> page, Tool from = null)
        {
            Surface window = Window(key);
            bool wasOpen = window.IsOpen;
            window.Show(page);
            if (!wasOpen) window.Place(DropUp(window, from ?? ToolFor(key)));
            return window;
        }

        private Tool ToolFor(string key)
        {
            foreach (Tool tool in tools) if (tool.Key == key) return tool;
            return null;
        }

        // Straight up from the tool that opened it, right edges aligned;
        // windows with no tool of their own line up along the left.
        private Vector2 DropUp(Surface window, Tool from)
        {
            float x = 16f;
            if (from != null)
            {
                var corners = new Vector3[4];
                ((RectTransform)from.Button.transform).GetWorldCorners(corners);
                float scale = canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
                x = corners[2].x / scale - window.Width;
            }
            return new Vector2(x, Surface.ReservedBottom);
        }

        private Action<Surface> PageFor(string key)
        {
            switch (key)
            {
                case "nav": return NavigationPage;
                case "wpn": return WeaponsPage;
                case "sns": return SensorsPage;
                case "roe": return EngagementPage;
                case "dmg": return DamagePage;
                case "rpl": return ReplenishmentPage;
                case "air": return AirPage;
                case "deck": return DeckPage;
                default: return s => s.Title(key);
            }
        }

        // ---- construction ----------------------------------------------------

        private void Ensure()
        {
            if (root != null) return;

            foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
                if (text.font != null) { font = text.font; break; }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (ArgumentException) { }
                if (font == null) try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (ArgumentException) { }
            }
            if (font == null) throw new InvalidOperationException("No native UI font is available.");
            UiKit.Font = font;

            root = new GameObject("Naval Power", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0f;

            Surface.ReservedBottom = StripHeight + 8f;
            Surface.ReservedTop = 12f;

            // Drawing order is creation order: map strokes at the back, then the
            // camera feeds, the strip, standing windows, the right-click menu,
            // and the hover card over everything.
            BuildOverlay();
            feedView = gameObject.AddComponent<TargetFeed>();
            feedView.Build((RectTransform)root.transform, font);
            BuildStrip();
            windowLayer = Layer("Windows");
            menuLayer = Layer("Menus");
            context = new Surface("context", menuLayer, canvas, 420f, growUp: false, closable: true);
            context.OnClosed = () => contextTarget = null;
            BuildSeatBar();
            BuildHover();
        }

        private RectTransform Layer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            var rect = (RectTransform)go.transform;
            UiKit.Fill(rect);
            return rect;
        }

        // Drawn first so map strokes sit behind everything else.
        private void BuildOverlay()
        {
            var go = new GameObject("Map orders", typeof(RectTransform), typeof(MapOverlay));
            go.transform.SetParent(root.transform, false);
            UiKit.Fill((RectTransform)go.transform);
            go.GetComponent<MapOverlay>().raycastTarget = false;
        }

        // ---- hover card ------------------------------------------------------

        private void BuildHover()
        {
            hover = UiKit.Box("Hover", (RectTransform)root.transform, Theme.SurfaceRaised);
            RectTransform edge = UiKit.Box("edge", hover, Theme.Dim(Theme.Accent, 0.65f));
            edge.anchorMin = new Vector2(0, 0); edge.anchorMax = new Vector2(0, 1);
            edge.pivot = new Vector2(0, 0.5f);
            edge.sizeDelta = new Vector2(2f, 0f);
            edge.anchoredPosition = Vector2.zero;
            hover.anchorMin = hover.anchorMax = new Vector2(0, 0);
            hover.pivot = new Vector2(0, 1);
            hover.GetComponent<Image>().raycastTarget = false;
            hoverText = UiKit.Label(hover, "", 15, TextAnchor.UpperLeft);
            UiKit.Fill(hoverText.rectTransform, 12f, 9f);
            hoverText.verticalOverflow = VerticalWrapMode.Overflow;
            hover.gameObject.SetActive(false);
        }

        private void RefreshHover()
        {
            Unit unit = MapCommand.Instance?.HoverUnit;
            EsmContact estimate = MapCommand.Instance?.HoverEsm;
            if (estimate == null && (unit == null || unit == CommandState.Ship))
            { hover.gameObject.SetActive(false); return; }
            hover.gameObject.SetActive(true);
            hover.SetAsLastSibling();
            hoverText.text = estimate != null
                ? TrackReadout.DescribeEsm(CommandState.Ship, estimate)
                : TrackReadout.Describe(CommandState.Ship, unit, CommandState.SelectedWeapon());
            Vector2 size = new Vector2(Mathf.Max(260f, hoverText.preferredWidth + 24f), hoverText.preferredHeight + 18f);
            hover.sizeDelta = size;
            float scale = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
            Vector2 pixels = size * scale;
            Vector2 point = Input.mousePosition;
            // Flip toward the screen centre so the card never leaves the view.
            float x = point.x + 18f, y = point.y - 18f;
            if (x + pixels.x > Screen.width) x = point.x - 18f - pixels.x;
            if (y - pixels.y < 0f) y = point.y + 18f + pixels.y;
            hover.position = new Vector2(x, y);
        }

        // ---- the seat bar ----------------------------------------------------

        // Stands in for the strip while flying: the controls for what the
        // session is currently about, which is one aircraft and the two ways of
        // giving it back.
        private void BuildSeatBar()
        {
            seatBar = UiKit.Box("Pilot seat bar", (RectTransform)root.transform, Theme.Surface);
            seatBar.anchorMin = new Vector2(0, 1);
            seatBar.anchorMax = new Vector2(1, 1);
            seatBar.pivot = new Vector2(0.5f, 1);
            seatBar.sizeDelta = new Vector2(0, Theme.AirBarHeight);
            seatBar.anchoredPosition = Vector2.zero;

            RectTransform edge = UiKit.Box("edge", seatBar, Theme.Dim(Theme.Accent, 0.5f));
            edge.anchorMin = new Vector2(0, 0); edge.anchorMax = new Vector2(1, 0);
            edge.pivot = new Vector2(0.5f, 0);
            edge.sizeDelta = new Vector2(0, 2f);
            edge.anchoredPosition = Vector2.zero;

            seatLabel = UiKit.Label(seatBar, "", Theme.CaptionSize, TextAnchor.MiddleLeft, Theme.Accent);
            UiKit.Place(seatLabel.rectTransform, 16, 14, 1240, 22);

            UiKit.Button(seatBar, "RETURN CONTROL  ·  back to its task area", 1272, 8, 316, 32,
                () => PilotSeat.Release(recoverToShip: false));
            UiKit.Button(seatBar, "DROP CONTROL  ·  recover to base", 1600, 8, 304, 32,
                () => PilotSeat.Release(recoverToShip: true));

            seatBar.gameObject.SetActive(false);
        }

        private void RefreshSeatBar(bool flying)
        {
            if (seatBar == null) return;
            // Only over the map. In the cockpit proper there is no cursor to
            // click it with, and a bar that cannot be clicked is just something
            // sitting on top of the HUD.
            flying = flying && DynamicMap.mapMaximized;
            seatBar.gameObject.SetActive(flying);
            Flight flight = PilotSeat.Flying;
            if (!flying || flight == null) return;
            seatLabel.text = "YOU HAVE THE CONTROLS  ·  " + flight.Name +
                "  ·  " + flight.FuelPercent.ToString("0") + "% fuel  ·  " + flight.StoresSummary +
                "  ·  " + Settings.ResumeCommand.Value.MainKey + " returns control";
        }

        private void OnDestroy() { if (root != null) Destroy(root); }
    }
}

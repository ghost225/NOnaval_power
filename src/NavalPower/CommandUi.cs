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
        private RectTransform strip, eventLine, windowLayer, menuLayer, hover, seatBar;
        private Text stripName, stripNav, stripStatus, stripState, hoverText, seatLabel;
        private RawImage hoverPicture;
        private Unit peekCandidate;
        private float peekSince;
        private float nextRefresh;

        private const float StripHeight = 36f, EventHeight = 24f;

        // ---- the outside world's view of the UI ------------------------------

        internal bool PopupOpen => context != null && context.IsOpen;

        // While a flight's window is open the map tasks that flight rather than
        // the ship, so a route can be laid down leg by leg.
        internal bool Pinned => windows.TryGetValue("flight", out Surface s) && s.IsOpen;

        // The wheel over a feed's picture zooms that camera rather than
        // scrolling the window or zooming the world.
        internal bool ZoomedAFeed(float delta)
        {
            if (feedView == null || Mathf.Abs(delta) < 0.01f) return false;
            Vector2 point = Input.mousePosition;
            if (Over(windows, "cam", point)) { TargetFeed.Zoom(feedView.LiveCamera, delta); return true; }
            for (int slot = 1; slot <= TargetFeed.MaxPinned; slot++)
                if (Over(windows, "pin" + slot, point))
                {
                    TargetFeed.Zoom(feedView.Find(slot)?.Camera, delta);
                    return true;
                }
            return false;
        }

        private static bool Over(Dictionary<string, Surface> all, string key, Vector2 point) =>
            all.TryGetValue(key, out Surface s) && s.IsOpen && s.ViewRect != null &&
            RectTransformUtility.RectangleContainsScreenPoint(s.ViewRect, point, null);

        // Windows refresh on their own timer and reuse their rows rather than
        // rebuilding them, so there is nothing left to do here.
        internal void RefreshPinned() { }

        internal bool Contains(Transform candidate) =>
            root != null && candidate != null && (candidate == root.transform || candidate.IsChildOf(root.transform));

        internal bool PointerInside()
        {
            if (root == null || !root.activeSelf) return false;
            Vector2 point = Input.mousePosition;
            if (seatBar != null && seatBar.gameObject.activeSelf &&
                RectTransformUtility.RectangleContainsScreenPoint(seatBar, point)) return true;
            if (strip != null && strip.gameObject.activeSelf &&
                RectTransformUtility.RectangleContainsScreenPoint(strip, point)) return true;
            if (context != null && context.Contains(point)) return true;
            foreach (Surface window in windows.Values)
                if (window.Contains(point)) return true;
            return fullBar != null && fullBar.gameObject.activeSelf &&
                RectTransformUtility.RectangleContainsScreenPoint(fullBar, point);
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
            eventLine.gameObject.SetActive(!flying);
            windowLayer.gameObject.SetActive(!flying);
            menuLayer.gameObject.SetActive(!flying);
            RefreshCompass(flying);
            if (flying) { hover.gameObject.SetActive(false); return; }

            WatchFeeds();
            CycleTaskForce();
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.2f;
                RefreshStrip();
                foreach (Surface window in windows.Values) window.Render();
                context.Render();
            }
            RefreshHover();
            RefreshFlightLabels();
        }

        // ---- flight labels on the map ----------------------------------------

        // A flight's callsign beside its map icon, the way a player's name reads
        // beside theirs -- readable at a glance rather than on hover. Drawn
        // every frame while the map is up, since the map pans and zooms under
        // them; a handful of labels costs nothing.
        private RectTransform labelLayer;
        private readonly List<Text> flightLabels = new List<Text>();

        private void RefreshFlightLabels()
        {
            var map = SceneSingleton<DynamicMap>.i;
            List<Flight> flights = DynamicMap.mapMaximized && map != null && map.mapImage != null
                ? FlightOrders.All() : null;
            int used = 0;
            if (flights != null)
            {
                foreach (Flight flight in flights)
                {
                    if (flight.Aircraft == null) continue;
                    // A wing reads as one label, on its lead; wingmen in
                    // formation go unlabelled, the way a flight of players would.
                    if (flight.Mode == FlightMode.Formation && Wings.IsWingman(flight)) continue;
                    int wingSize = Wings.IsLead(flight) ? Wings.Members(flight.Wing).Count : 1;
                    Vector3 at = MapGeometry.ToScreen(map, flight.Aircraft.GlobalPosition());
                    // Only where the map is actually showing: panned off its
                    // visible area, a label would float over the world.
                    if (!OnMap(map, at)) continue;
                    if (used == flightLabels.Count)
                    {
                        Text made = UiKit.Label(labelLayer, "", Theme.CaptionSize, TextAnchor.MiddleLeft, Theme.Text);
                        made.rectTransform.pivot = new Vector2(0f, 0.5f);
                        made.rectTransform.sizeDelta = new Vector2(220f, 20f);
                        var shadow = made.gameObject.AddComponent<Shadow>();
                        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
                        flightLabels.Add(made);
                    }
                    Text label = flightLabels[used++];
                    label.gameObject.SetActive(true);
                    label.text = wingSize > 1 ? flight.Wing + " (" + wingSize + ")" : flight.Name;
                    label.color = CommandState.SelectedFlight == flight
                        ? Color.Lerp(FlightIcons.For(flight), Color.white, 0.4f) : FlightIcons.For(flight);
                    label.rectTransform.position = at + new Vector3(14f, 0f, 0f);
                }
            }
            for (int i = used; i < flightLabels.Count; i++) flightLabels[i].gameObject.SetActive(false);
        }

        private static bool OnMap(DynamicMap map, Vector3 screen) =>
            map.mapBackground != null &&
            RectTransformUtility.RectangleContainsScreenPoint(map.mapBackground.rectTransform, screen, null);

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

            stripName = StripText(strip, 12, 290, Theme.BodySize + 1, Theme.Text);
            stripNav = StripText(strip, 310, 340, Theme.CaptionSize, Theme.TextMuted);
            stripState = StripText(strip, 660, 380, Theme.CaptionSize, Theme.TextMuted);

            // Events -- order confirmations, what is being tasked, what a weapon
            // is waiting for -- get a line of their own above the strip. Sharing
            // the strip, a long confirmation ran over the EMCON and rules text.
            eventLine = UiKit.Box("Event line", (RectTransform)root.transform, Theme.Dim(Theme.Surface, 0.78f));
            eventLine.anchorMin = new Vector2(0, 0); eventLine.anchorMax = new Vector2(1, 0);
            eventLine.pivot = new Vector2(0.5f, 0);
            eventLine.sizeDelta = new Vector2(0, EventHeight);
            eventLine.anchoredPosition = new Vector2(0f, StripHeight);
            eventLine.GetComponent<Image>().raycastTarget = false;
            stripStatus = StripText(eventLine, 12, 1880, Theme.CaptionSize, Theme.TextMuted);

            string[,] defs =
            {
                { "nav", "NAV" }, { "wpn", "WPN" }, { "sns", "SNS" }, { "roe", "ROE" }, { "dmg", "DMG" }, { "tf", "TF" },
                { "air", "AIR" }, { "cam", "CAM" }, { "rpl", "RPL" }, { "map", "MAP" }, { "exit", "EXIT" }
            };
            int count = defs.GetLength(0);
            for (int i = 0; i < count; i++)
            {
                var tool = new Tool { Key = defs[i, 0], Label = defs[i, 1] };
                tool.Button = UiKit.Button(strip, tool.Label, 0, 0, ToolWidth, StripHeight - 8f, () => Use(tool));
                var rect = (RectTransform)tool.Button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
                rect.pivot = new Vector2(1, 0.5f);
                tool.Text = tool.Button.GetComponentInChildren<Text>();
                tool.Text.fontSize = Theme.CaptionSize;
                tool.Text.supportRichText = true;
                tools.Add(tool);
            }
        }

        private const float ToolWidth = 60f, ToolGap = 4f;

        // The tools a ship has that an airfield does not: it cannot steer,
        // fire, radiate, flood or take on stores.
        private static bool ShipOnly(string key) =>
            key == "nav" || key == "wpn" || key == "sns" || key == "roe" || key == "dmg" || key == "rpl" || key == "tf";

        // Right-aligned, closing up over whatever this post does not have.
        private void LayoutTools(bool ship)
        {
            float x = 12f;
            for (int i = tools.Count - 1; i >= 0; i--)
            {
                Tool tool = tools[i];
                bool shown = ship || !ShipOnly(tool.Key);
                tool.Button.gameObject.SetActive(shown);
                if (!shown) continue;
                ((RectTransform)tool.Button.transform).anchoredPosition = new Vector2(-x, 0f);
                x += ToolWidth + ToolGap;
            }
            if (ship) return;
            foreach (KeyValuePair<string, Surface> window in windows)
                if (ShipOnly(window.Key) && window.Value.IsOpen) window.Value.Close();
        }

        // Name, status and state one after another at their real widths, up
        // to the tools. Fixed slots assumed short text, and an airfield's
        // hangar list ran straight through the state beside it.
        private void FlowStrip()
        {
            int shown = 0;
            foreach (Tool tool in tools) if (tool.Button.gameObject.activeSelf) shown++;
            float limit = strip.rect.width - 12f - shown * (ToolWidth + ToolGap) - 16f;
            float x = Flow(stripName, 12f, limit, Theme.BodySize + 1) + 36f;
            x = Flow(stripNav, x, limit, Theme.CaptionSize) + 36f;
            Flow(stripState, x, limit, Theme.CaptionSize);
        }

        private static float Flow(Text text, float x, float limit, int size)
        {
            float room = Mathf.Max(0f, limit - x);
            text.rectTransform.anchoredPosition = new Vector2(x, 0f);
            text.rectTransform.sizeDelta = new Vector2(room, 0f);
            UiKit.Fit(text, size);
            float width = Mathf.Min(text.preferredWidth + 2f, room);
            text.rectTransform.sizeDelta = new Vector2(width, 0f);
            return x + width;
        }

        private static Text StripText(RectTransform parent, float x, float width, int size, Color color)
        {
            Text text = UiKit.Label(parent, "", size, TextAnchor.MiddleLeft, color);
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
                    MapCommand.Instance?.Dismiss();
                    return;
                case "cam":
                    ToggleLiveFeed();
                    return;
                case "map":
                    ToggleMap();
                    return;
                default:
                    ToggleWindow(tool.Key, tool);
                    return;
            }
        }

        private void RefreshStrip()
        {
            Ship ship = CommandState.Ship;
            LayoutTools(ship != null);
            if (ship == null) { RefreshFieldStrip(); return; }

            stripName.text = ShipNames.Of(ship) +
                (ShipNames.IsNamed(ship) ? "  " + UiKit.Tint(ShipNames.TypeOf(ship), Theme.TextMuted) : "") + ForceTag(ship);

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
            FlowStrip();

            foreach (Tool tool in tools)
            {
                bool open = tool.Key == "map" ? DynamicMap.mapMaximized
                    : windows.TryGetValue(tool.Key, out Surface window) && window.IsOpen;
                tool.Button.image.color = open ? Theme.AccentFill : Theme.Control;
                if (tool.Key == "air") tool.Text.text = AirToolLabel();
            }
        }

        // An airfield's strip: which field, what its hangars are doing, and
        // what is in the air.
        private void RefreshFieldStrip()
        {
            Airbase field = CommandState.Base;
            if (field == null) return;
            stripName.text = Airfields.NameOf(field);
            Airfields.Hangars(field, out int ready, out int busy);
            int traffic = DeckTraffic.Movements(field).Count;
            stripNav.text = ready + " free" + (busy > 0 ? ", " + busy + " working" : "") + "  ·  " + Airfields.Inventory(field) +
                (traffic > 0 ? "  ·  " + traffic + " in the pattern" : "");

            string said = CommandState.Feedback;
            stripStatus.text = said != null ? UiKit.Tint(said, Theme.Accent)
                : CommandState.SelectedFlight != null
                    ? UiKit.Tint("Tasking " + CommandState.SelectedFlight.Name, Theme.Accent) +
                      "  ·  right-click the map to order it"
                : "Open AIR to launch or task flights  ·  the view flies with the movement keys";

            List<Flight> airborne = FlightOrders.All();
            int trouble = 0;
            foreach (Flight flight in airborne) if (flight.Threat == FlightThreat.Missile || flight.FuelPercent < 25f) trouble++;
            stripState.text = UiKit.Tint("AIRFIELD", Theme.Passive) + "   " +
                airborne.Count + " airborne" +
                (trouble > 0 ? "   " + UiKit.Tint(trouble + " need attention", Theme.Bad) : "");
            FlowStrip();

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
                case "tf": width = 560f; break;
                case "cam": case "pin1": case "pin2": case "pin3": width = FeedWindowWidth; break;
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
            // Closing a pinned feed's window is how a pin is taken down.
            if (key.StartsWith("pin") && int.TryParse(key.Substring(3), out int slot))
                window.OnClosed = () => feedView?.Unpin(slot);
            // Closed by hand, the live feed stays closed until asked for again,
            // rather than springing back open with the next launch.
            if (key == "cam") window.OnClosed = () => liveClosedByHand = true;
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
                case "tf": return TaskForcePage;
                case "deck": return DeckPage;
                case "cam": return LiveFeedPage;
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

            Surface.ReservedBottom = StripHeight + EventHeight + 8f;
            Surface.ReservedTop = 12f;

            // Drawing order is creation order: map strokes at the back, then the
            // camera feeds, the strip, standing windows, the right-click menu,
            // and the hover card over everything.
            BuildOverlay();
            labelLayer = Layer("Flight labels");
            // The feeds' cameras; their windows are made like any other.
            feedView = gameObject.AddComponent<TargetFeed>();
            BuildStrip();
            BuildCompass();
            windowLayer = Layer("Windows");
            menuLayer = Layer("Menus");
            context = new Surface("context", menuLayer, canvas, 420f, growUp: false, closable: true);
            context.OnClosed = () => contextTarget = null;
            BuildSeatBar();
            BuildFullBar();
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
            RectTransform words = hoverText.rectTransform;
            words.anchorMin = new Vector2(0, 1); words.anchorMax = new Vector2(1, 1);
            words.pivot = new Vector2(0.5f, 1);
            words.anchoredPosition = new Vector2(0f, -9f);
            hoverText.verticalOverflow = VerticalWrapMode.Overflow;

            var picture = new GameObject("Peek", typeof(RectTransform), typeof(RawImage));
            picture.transform.SetParent(hover, false);
            var frame = (RectTransform)picture.transform;
            frame.anchorMin = new Vector2(0, 0); frame.anchorMax = new Vector2(1, 0);
            frame.pivot = new Vector2(0.5f, 0);
            frame.anchoredPosition = new Vector2(0f, 6f);
            hoverPicture = picture.GetComponent<RawImage>();
            hoverPicture.raycastTarget = false;
            hoverPicture.gameObject.SetActive(false);
            hover.gameObject.SetActive(false);
        }

        private void RefreshHover()
        {
            Unit unit = MapCommand.Instance?.HoverUnit;
            EsmContact estimate = MapCommand.Instance?.HoverEsm;
            if (estimate == null && (unit == null || unit == CommandState.Ship))
            {
                hover.gameObject.SetActive(false);
                if (feedView != null) feedView.Peek = null;
                peekCandidate = null;
                return;
            }
            hover.gameObject.SetActive(true);
            hover.SetAsLastSibling();
            hoverText.text = estimate != null
                ? TrackReadout.DescribeEsm(CommandState.Ship, estimate)
                : TrackReadout.Describe(CommandState.Ship, unit, CommandState.SelectedWeapon());

            // A peek once the cursor has rested for a moment, so sweeping across
            // a crowded map does not flash a picture at every icon it crosses.
            if (unit != peekCandidate) { peekCandidate = unit; peekSince = Time.unscaledTime; }
            // Only on a current track: a stale one has no picture to give.
            bool peeking = estimate == null && unit != null && Settings.FeedHoverPeek.Value &&
                Settings.TargetFeed.Value && Time.unscaledTime - peekSince > 0.3f && TrackReadout.IsCurrent(unit) &&
                !MapDocked;                 // a peek bigger than the docked map would cover it
            if (feedView != null) feedView.Peek = peeking ? unit : null;
            bool picture = peeking && feedView != null && feedView.PeekTexture != null;

            float textHeight = hoverText.preferredHeight;
            float width = Mathf.Max(260f, hoverText.preferredWidth + 24f, picture ? 340f : 0f);
            float pictureHeight = picture ? (width - 12f) * 9f / 16f : 0f;
            hoverText.rectTransform.sizeDelta = new Vector2(-24f, textHeight);
            hoverPicture.gameObject.SetActive(picture);
            if (picture)
            {
                hoverPicture.texture = feedView.PeekTexture;
                hoverPicture.rectTransform.sizeDelta = new Vector2(-12f, pictureHeight);
            }
            Vector2 size = new Vector2(width, textHeight + 18f + (picture ? pictureHeight + 8f : 0f));
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

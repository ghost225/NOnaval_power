using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // Command bar plus right-click popup menus. Targeting happens on the native
    // map through MapCommand; this surface carries persistent controls, the
    // hover readout, and the context menus that map clicks open.
    internal sealed class CommandUi : MonoBehaviour
    {
        private static readonly Color Background = new Color(0.075f, 0.095f, 0.12f, 0.96f);
        private static readonly Color PopupBackground = new Color(0.06f, 0.08f, 0.10f, 0.98f);
        private static readonly Color ButtonColor = new Color(0.15f, 0.19f, 0.23f, 1f);
        private static readonly Color SelectedColor = new Color(0.13f, 0.42f, 0.48f, 1f);
        private static readonly Color DangerColor = new Color(0.46f, 0.20f, 0.17f, 1f);
        private static readonly Color TextColor = new Color(0.89f, 0.93f, 0.94f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.68f, 0.72f, 1f);

        private Font font;
        private GameObject root;
        private RectTransform bar, popup, popupContent, hover, weaponRow;
        private Text shipLabel, statusLabel, speedLabel, feedbackLabel, hoverText;
        private Slider speedSlider;
        private bool updatingSlider;
        private float nextRefresh;

        private Unit contextTarget;
        private bool contextAppend;
        private string popupKey;

        private readonly List<Button> weaponButtons = new List<Button>();
        private readonly List<Text> weaponLabels = new List<Text>();
        private readonly List<string> weaponKeys = new List<string>();
        private readonly List<Button> quantityButtons = new List<Button>();
        private readonly List<Button> roeButtons = new List<Button>();
        private static readonly int[] Counts = { 1, 2, 4, 8, 16 };

        internal bool PopupOpen => popup != null && popup.gameObject.activeSelf;

        internal bool Contains(Transform candidate) =>
            root != null && candidate != null && (candidate == root.transform || candidate.IsChildOf(root.transform));

        internal bool PointerInside()
        {
            if (root == null || !root.activeSelf) return false;
            if (bar != null && RectTransformUtility.RectangleContainsScreenPoint(bar, Input.mousePosition)) return true;
            return PopupOpen && RectTransformUtility.RectangleContainsScreenPoint(popup, Input.mousePosition);
        }

        private void Update()
        {
            if (!CommandState.Active) { if (root != null) root.SetActive(false); return; }
            Ensure();
            root.SetActive(true);
            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.2f; Refresh(); }
            RefreshHover();
        }

        // ---- refresh ------------------------------------------------------

        private void Refresh()
        {
            Ship ship = CommandState.Ship;
            if (ship == null) return;

            shipLabel.text = ship.definition?.unitName ?? ship.name;
            statusLabel.text = WeaponOrders.GetStatus(ship);

            NavigationSnapshot nav = NavigationOrders.GetSnapshot(ship);
            if (nav != null)
            {
                speedLabel.text = "Actual " + nav.ActualSpeedKnots.ToString("0.0") +
                    " kt  /  Ordered " + nav.OrderedSpeedKnots.ToString("0.0") + " kt";
                updatingSlider = true;
                speedSlider.minValue = nav.MinimumSpeedKnots;
                speedSlider.maxValue = Mathf.Max(nav.MinimumSpeedKnots + 0.1f, nav.MaximumSpeedKnots);
                speedSlider.value = Mathf.Clamp(nav.OrderedSpeedKnots, speedSlider.minValue, speedSlider.maxValue);
                updatingSlider = false;
            }

            string say = CommandState.Feedback;
            feedbackLabel.text = say ?? ((nav != null ? nav.Status + "  ·  " : "") +
                (CommandState.Armed
                    ? "Right-click a contact to engage with " + (CommandState.SelectedWeapon()?.Name ?? "")
                    : "Right-click the map for a waypoint · shift to append · right-click a contact for options"));

            RefreshWeapons();
            RefreshQuantities();
            EngagementMode mode = EngagementPolicy.GetMode(ship);
            for (int i = 0; i < roeButtons.Count; i++)
                roeButtons[i].image.color = (EngagementMode)i == mode ? SelectedColor : ButtonColor;
        }

        private void RefreshWeapons()
        {
            WeaponCommandInfo[] weapons = WeaponOrders.GetWeapons(CommandState.Ship);
            while (weaponButtons.Count < weapons.Length)
            {
                int index = weaponButtons.Count;
                Button button = MakeButton(weaponRow, "", index * 250, 0, 244, 38, () => SelectWeapon(index, null));
                weaponButtons.Add(button);
                weaponLabels.Add(button.GetComponentInChildren<Text>());
            }
            weaponKeys.Clear();
            for (int i = 0; i < weaponButtons.Count; i++)
            {
                bool live = i < weapons.Length;
                weaponButtons[i].gameObject.SetActive(live);
                if (!live) continue;
                WeaponCommandInfo weapon = weapons[i];
                weaponKeys.Add(weapon.Key);
                weaponLabels[i].text = weapon.Name + "\n" + weapon.Readiness +
                    (weapon.Continuous ? " · continuous" : " · " + weapon.Ammo);
                weaponButtons[i].image.color = weapon.Key == CommandState.SelectedKey ? SelectedColor : ButtonColor;
            }
        }

        private void RefreshQuantities()
        {
            WeaponCommandInfo selected = CommandState.SelectedWeapon();
            bool relevant = selected != null && !selected.Continuous;
            for (int i = 0; i < quantityButtons.Count; i++)
            {
                quantityButtons[i].gameObject.SetActive(relevant);
                quantityButtons[i].image.color = Counts[i] == CommandState.Quantity ? SelectedColor : ButtonColor;
            }
        }

        private void RefreshHover()
        {
            Unit unit = MapCommand.Instance?.HoverUnit;
            if (unit == null || unit == CommandState.Ship) { hover.gameObject.SetActive(false); return; }
            hover.gameObject.SetActive(true);
            hoverText.text = TrackReadout.Describe(CommandState.Ship, unit, CommandState.SelectedWeapon());
            Vector2 size = new Vector2(Mathf.Max(260f, hoverText.preferredWidth + 24f), hoverText.preferredHeight + 18f);
            hover.sizeDelta = size;
            Vector2 point = Input.mousePosition;
            // Flip toward the screen centre so the card never leaves the view.
            float x = point.x + 18f, y = point.y - 18f;
            if (x + size.x > Screen.width) x = point.x - 18f - size.x;
            if (y - size.y < 0f) y = point.y + 18f + size.y;
            hover.position = new Vector2(x, y);
        }

        // ---- actions ------------------------------------------------------

        private void SelectWeapon(int index, Unit immediateTarget)
        {
            if (index >= weaponKeys.Count) return;
            string key = weaponKeys[index];
            CommandState.SelectedKey = CommandState.SelectedKey == key && immediateTarget == null ? null : key;
            if (CommandState.SelectedKey != null && immediateTarget != null)
            {
                WeaponOrders.Attack(CommandState.Ship, CommandState.SelectedKey, immediateTarget,
                    CommandState.Quantity, out string reason, contextAppend);
                CommandState.Say(reason);
                ClosePopup();
            }
            else
            {
                CommandState.Say(CommandState.SelectedKey == null
                    ? "Weapon deselected" : "Right-click a contact to engage");
            }
            Refresh();
        }

        // ---- popup menus --------------------------------------------------

        internal void OpenContext(Vector2 screenPosition, Unit target, bool append)
        {
            contextTarget = target;
            contextAppend = append;
            popupKey = "context";
            string title = target != null
                ? (target.definition?.unitName ?? target.name)
                : (CommandState.Ship?.definition?.unitName ?? "Ship");
            StartPopup(title, screenPosition, 5);
            Row("Engage with…", 1, WeaponMenu);
            Row("Navigate / speed…", 2, NavigationMenu);
            Row("Engagement permissions…", 3, RoeMenu);
            Row("Cease fire", 4, () =>
            {
                WeaponOrders.CeaseFire(CommandState.Ship, out string reason);
                CommandState.SelectedKey = null;
                CommandState.Say(reason);
                ClosePopup();
            });
            Row("Close", 5, ClosePopup);
        }

        private void WeaponMenu()
        {
            WeaponCommandInfo[] weapons = WeaponOrders.GetWeapons(CommandState.Ship);
            StartPopup(contextTarget != null
                ? "Engage " + (contextTarget.definition?.unitName ?? contextTarget.name)
                : "Select weapon", null, weapons.Length + 1);
            for (int i = 0; i < weapons.Length; i++)
            {
                WeaponCommandInfo weapon = weapons[i];
                int index = i;
                bool capable = contextTarget == null || WeaponOrders.Opportunity(
                    WeaponOrders.StationsFor(CommandState.Ship, weapon.Key).FirstOrDefault()?.WeaponInfo, contextTarget) > 0.01f;
                Button row = Row(weapon.Name + "  ·  " + weapon.Readiness +
                    (weapon.Continuous ? " · continuous" : " · " + weapon.Ammo + " remaining") +
                    (capable ? "" : "  ·  ineffective"), i + 1, () =>
                    {
                        weaponKeys.Clear();
                        foreach (WeaponCommandInfo w in WeaponOrders.GetWeapons(CommandState.Ship)) weaponKeys.Add(w.Key);
                        SelectWeapon(index, contextTarget);
                    });
                if (!capable) row.GetComponentInChildren<Text>().color = MutedColor;
            }
            Row("Close", weapons.Length + 1, ClosePopup);
        }

        private void NavigationMenu()
        {
            StartPopup("Navigate", null, 8);
            string[] presets = { "All stop", "Ahead 1/3", "Ahead 2/3", "Ahead full", "Ahead flank", "Back 1/3" };
            float[] fractions = { 0f, 1f / 3f, 2f / 3f, 0.9f, 1f, -1f / 3f };
            for (int i = 0; i < presets.Length; i++)
            {
                float fraction = fractions[i];
                Row(presets[i], i + 1, () =>
                {
                    NavigationOrders.SetOrderedSpeedKnots(CommandState.Ship,
                        CommandableShip.MaximumSpeedKnots(CommandState.Ship) * fraction, out string reason);
                    CommandState.Say(reason);
                    ClosePopup();
                });
            }
            Row("Clear route", 7, () =>
            {
                NavigationOrders.ClearWaypoints(CommandState.Ship, out string reason);
                CommandState.Say(reason);
                ClosePopup();
            });
            Row("Close", 8, ClosePopup);
        }

        private void RoeMenu()
        {
            StartPopup("Engagement permissions", null, 4);
            for (int i = 0; i < 3; i++)
            {
                var mode = (EngagementMode)i;
                Row(EngagementPolicy.Describe(mode), i + 1, () =>
                {
                    EngagementPolicy.SetMode(CommandState.Ship, mode, out string reason);
                    CommandState.Say(reason);
                    ClosePopup();
                    Refresh();
                });
            }
            Row("Close", 4, ClosePopup);
        }

        private void StartPopup(string title, Vector2? screenPosition, int rows)
        {
            Ensure();
            foreach (Transform child in popupContent) Destroy(child.gameObject);
            popup.gameObject.SetActive(true);
            float height = rows * 38f + 46f;
            popup.sizeDelta = new Vector2(392, height);
            if (screenPosition.HasValue)
            {
                float x = Mathf.Clamp(screenPosition.Value.x, 0, Screen.width - 392);
                float y = Mathf.Clamp(screenPosition.Value.y, height, Screen.height);
                popup.position = new Vector2(x, y);
            }
            Text header = Label(popup, title, 17, TextAnchor.MiddleLeft);
            Place(header.rectTransform, 12, 8, 368, 30);
        }

        private Button Row(string label, int row, Action action) =>
            MakeButton(popupContent, label, 8, (row - 1) * 38, 376, 34, action);

        internal void ClosePopup()
        {
            if (popup == null) return;
            popup.gameObject.SetActive(false);
            contextTarget = null;
            popupKey = null;
        }

        private void TogglePopup(string key, Action open)
        {
            if (PopupOpen && popupKey == key) { ClosePopup(); return; }
            ClosePopup();
            popupKey = key;
            open();
        }

        // ---- construction -------------------------------------------------

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

            root = new GameObject("Naval Power", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0f;

            BuildOverlay();
            BuildBar();
            BuildPopup();
            BuildHover();
        }

        // Drawn first so map strokes sit behind the bar and popups.
        private void BuildOverlay()
        {
            var go = new GameObject("Map orders", typeof(RectTransform), typeof(MapOverlay));
            go.transform.SetParent(root.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            go.GetComponent<MapOverlay>().raycastTarget = false;
        }

        private void BuildBar()
        {
            bar = Box("Command bar", (RectTransform)root.transform, Background);
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0);
            bar.pivot = new Vector2(0.5f, 0);
            bar.sizeDelta = new Vector2(0, 166);
            bar.anchoredPosition = Vector2.zero;

            shipLabel = Label(bar, "", 20, TextAnchor.MiddleLeft);
            Place(shipLabel.rectTransform, 16, 8, 560, 28);
            statusLabel = Label(bar, "", 16, TextAnchor.MiddleLeft, MutedColor);
            Place(statusLabel.rectTransform, 588, 8, 900, 28);
            Button exit = MakeButton(bar, "Exit command", 1740, 8, 164, 30,
                () => MapCommand.Instance?.LeaveForNativeFlow());

            speedLabel = Label(bar, "", 17, TextAnchor.MiddleLeft);
            Place(speedLabel.rectTransform, 16, 44, 330, 30);
            speedSlider = MakeSlider(bar, 352, 52, 300, 18);
            speedSlider.onValueChanged.AddListener(value =>
            {
                if (updatingSlider || !CommandState.Active) return;
                NavigationOrders.SetOrderedSpeedKnots(CommandState.Ship, Mathf.Round(value * 10f) / 10f, out string reason);
                CommandState.Say(reason);
            });

            string[] presets = { "All stop", "1/3", "2/3", "Full", "Flank" };
            float[] fractions = { 0f, 1f / 3f, 2f / 3f, 0.9f, 1f };
            for (int i = 0; i < presets.Length; i++)
            {
                float fraction = fractions[i];
                MakeButton(bar, presets[i], 672 + i * 96, 44, 90, 30, () =>
                {
                    NavigationOrders.SetOrderedSpeedKnots(CommandState.Ship,
                        CommandableShip.MaximumSpeedKnots(CommandState.Ship) * fraction, out string reason);
                    CommandState.Say(reason);
                });
            }
            MakeButton(bar, "Clear route", 1164, 44, 140, 30, () =>
            {
                NavigationOrders.ClearWaypoints(CommandState.Ship, out string reason);
                CommandState.Say(reason);
            });

            for (int i = 0; i < 3; i++)
            {
                var mode = (EngagementMode)i;
                roeButtons.Add(MakeButton(bar, EngagementPolicy.Describe(mode), 1316 + i * 152, 44, 146, 30, () =>
                {
                    EngagementPolicy.SetMode(CommandState.Ship, mode, out string reason);
                    CommandState.Say(reason);
                    Refresh();
                }));
            }
            Button cease = MakeButton(bar, "CEASE FIRE", 1772, 44, 132, 30, () =>
            {
                WeaponOrders.CeaseFire(CommandState.Ship, out string reason);
                CommandState.SelectedKey = null;
                CommandState.Say(reason);
            });
            cease.image.color = DangerColor;

            weaponRow = Box("Weapons", bar, new Color(0, 0, 0, 0));
            Place(weaponRow, 16, 82, 1500, 38);

            for (int i = 0; i < Counts.Length; i++)
            {
                int count = Counts[i];
                quantityButtons.Add(MakeButton(bar, count == 1 ? "Single" : "x" + count,
                    1540 + i * 74, 82, 70, 38, () => { CommandState.Quantity = count; Refresh(); }));
            }

            feedbackLabel = Label(bar, "", 16, TextAnchor.MiddleLeft, MutedColor);
            Place(feedbackLabel.rectTransform, 16, 128, 1880, 28);
        }

        private void BuildPopup()
        {
            popup = Box("Popup", (RectTransform)root.transform, PopupBackground);
            popup.anchorMin = popup.anchorMax = new Vector2(0, 0);
            popup.pivot = new Vector2(0, 0);
            popupContent = Box("Popup content", popup, new Color(0, 0, 0, 0));
            popupContent.anchorMin = new Vector2(0, 1); popupContent.anchorMax = new Vector2(1, 1);
            popupContent.pivot = new Vector2(0, 1);
            popupContent.anchoredPosition = new Vector2(0, -42);
            popupContent.sizeDelta = new Vector2(0, 0);
            popup.gameObject.SetActive(false);
        }

        private void BuildHover()
        {
            hover = Box("Hover", (RectTransform)root.transform, PopupBackground);
            hover.anchorMin = hover.anchorMax = new Vector2(0, 0);
            hover.pivot = new Vector2(0, 1);
            hoverText = Label(hover, "", 15, TextAnchor.UpperLeft);
            hoverText.rectTransform.anchorMin = Vector2.zero;
            hoverText.rectTransform.anchorMax = Vector2.one;
            hoverText.rectTransform.offsetMin = new Vector2(12, 9);
            hoverText.rectTransform.offsetMax = new Vector2(-12, -9);
            hoverText.verticalOverflow = VerticalWrapMode.Overflow;
            hover.gameObject.SetActive(false);
        }

        // ---- uGUI helpers -------------------------------------------------

        private RectTransform Box(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        private Text Label(RectTransform parent, string value, int size, TextAnchor alignment) =>
            Label(parent, value, size, alignment, TextColor);

        private Text Label(RectTransform parent, string value, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font; text.fontSize = size; text.color = color; text.text = value; text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private Button MakeButton(RectTransform parent, string label, float x, float y, float width, float height, Action action)
        {
            RectTransform rect = Box(string.IsNullOrEmpty(label) ? "Button" : label, parent, ButtonColor);
            Place(rect, x, y, width, height);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.pressedColor = new Color(0.65f, 0.85f, 0.9f);
            button.colors = colors;
            button.onClick.AddListener(() => { if (CommandState.Active) action(); });
            Text text = Label(rect, label, 15, TextAnchor.MiddleCenter);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(6, 0); text.rectTransform.offsetMax = new Vector2(-6, 0);
            return button;
        }

        private Slider MakeSlider(RectTransform parent, float x, float y, float width, float height)
        {
            RectTransform rect = Box("Ordered speed", parent, new Color(0.25f, 0.3f, 0.34f));
            Place(rect, x, y, width, height);
            var slider = rect.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            var handleArea = new GameObject("Handle area", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(rect, false);
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(6, 0); handleArea.offsetMax = new Vector2(-6, 0);
            RectTransform handle = Box("Handle", handleArea, new Color(0.3f, 0.8f, 0.86f));
            handle.sizeDelta = new Vector2(13, 26);
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            return slider;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private void OnDestroy() { if (root != null) Destroy(root); }
    }
}

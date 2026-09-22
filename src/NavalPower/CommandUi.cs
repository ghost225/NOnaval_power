using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // Screen-space command bar. Deliberately does not touch the native map or
    // world input yet: targets are picked from a contact list, which avoids
    // arbitrating mouse input with the camera and map controls while the order
    // engine is still being validated.
    internal sealed class CommandUi : MonoBehaviour
    {
        private const int ContactRows = 10;

        private static readonly Color Background = new Color(0.075f, 0.095f, 0.12f, 0.96f);
        private static readonly Color ButtonColor = new Color(0.15f, 0.19f, 0.23f, 1f);
        private static readonly Color SelectedColor = new Color(0.13f, 0.42f, 0.48f, 1f);
        private static readonly Color DangerColor = new Color(0.46f, 0.20f, 0.17f, 1f);
        private static readonly Color TextColor = new Color(0.89f, 0.93f, 0.94f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.68f, 0.72f, 1f);

        private Ship ship;
        private Font font;
        private GameObject root;
        private RectTransform bar, contactPanel, weaponRow;
        private Text shipLabel, statusLabel, speedLabel, feedbackLabel, contactHeader;
        private Slider speedSlider;
        private bool updatingSlider;

        private string selectedKey;
        private int quantity = 1;
        private float nextRefresh;
        private string feedback = "";
        private float feedbackUntil;

        private readonly List<Button> weaponButtons = new List<Button>();
        private readonly List<Text> weaponLabels = new List<Text>();
        private readonly List<string> weaponKeys = new List<string>();
        private readonly List<Button> quantityButtons = new List<Button>();
        private readonly List<Button> contactButtons = new List<Button>();
        private readonly List<Text> contactLabels = new List<Text>();
        private readonly List<Unit> contactUnits = new List<Unit>();

        private void Update()
        {
            Ship current = MissionManager.IsRunning ? Plugin.CommandedShip() : null;
            if (current != ship)
            {
                ship = current;
                selectedKey = null;
                quantity = 1;
            }
            if (ship == null) { if (root != null) root.SetActive(false); return; }

            Ensure();
            root.SetActive(true);
            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.2f; Refresh(); }
        }

        private void Say(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            feedback = message;
            feedbackUntil = Time.unscaledTime + 6f;
        }

        // ---- construction -------------------------------------------------

        private void Ensure()
        {
            if (root != null) return;

            // Reuse the game's own uGUI font; Unity 2022 ships LegacyRuntime.ttf.
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

            BuildBar();
            BuildContacts();
        }

        private void BuildBar()
        {
            bar = Box("Command bar", (RectTransform)root.transform, Background);
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0);
            bar.pivot = new Vector2(0.5f, 0);
            bar.sizeDelta = new Vector2(0, 166);
            bar.anchoredPosition = Vector2.zero;

            shipLabel = Label(bar, "", 20, TextAnchor.MiddleLeft);
            Place(shipLabel.rectTransform, 16, 8, 620, 28);
            statusLabel = Label(bar, "", 16, TextAnchor.MiddleLeft, MutedColor);
            Place(statusLabel.rectTransform, 648, 8, 900, 28);
            Button cease = MakeButton(bar, "CEASE FIRE", 1740, 8, 164, 30, () =>
            {
                WeaponOrders.CeaseFire(ship, out string reason);
                selectedKey = null;
                Say(reason);
            });
            cease.image.color = DangerColor;

            // Speed row.
            speedLabel = Label(bar, "", 17, TextAnchor.MiddleLeft);
            Place(speedLabel.rectTransform, 16, 44, 330, 30);
            speedSlider = MakeSlider(bar, 352, 52, 300, 18);
            speedSlider.onValueChanged.AddListener(value =>
            {
                if (updatingSlider || ship == null) return;
                float knots = Mathf.Round(value * 10f) / 10f;
                NavigationOrders.SetOrderedSpeedKnots(ship, knots, out string reason);
                Say(reason);
            });

            string[] presets = { "All stop", "1/3", "2/3", "Full", "Flank" };
            float[] fractions = { 0f, 1f / 3f, 2f / 3f, 0.9f, 1f };
            for (int i = 0; i < presets.Length; i++)
            {
                float fraction = fractions[i];
                MakeButton(bar, presets[i], 672 + i * 96, 44, 90, 30, () =>
                {
                    NavigationOrders.SetOrderedSpeedKnots(ship, CommandableShip.MaximumSpeedKnots(ship) * fraction, out string reason);
                    Say(reason);
                });
            }
            MakeButton(bar, "Waypoint ahead", 1164, 44, 158, 30, () =>
            {
                Vector3 bow = ship.transform.forward; bow.y = 0f; bow.Normalize();
                NavigationOrders.ReplaceWaypoint(ship, ship.GlobalPosition() + bow * 5000f, out string reason);
                Say(reason);
            });
            MakeButton(bar, "Clear route", 1330, 44, 140, 30, () =>
            {
                NavigationOrders.ClearWaypoints(ship, out string reason);
                Say(reason);
            });
            MakeButton(bar, "Release speed", 1478, 44, 150, 30, () =>
            {
                NavigationOrders.ReleaseSpeed(ship, out string reason);
                Say(reason);
            });

            // Weapon row is rebuilt whenever the fitted weapons change.
            weaponRow = Box("Weapons", bar, new Color(0, 0, 0, 0));
            Place(weaponRow, 16, 82, 1500, 38);

            int[] counts = { 1, 2, 4, 8, 16 };
            for (int i = 0; i < counts.Length; i++)
            {
                int count = counts[i];
                quantityButtons.Add(MakeButton(bar, count == 1 ? "Single" : "x" + count,
                    1540 + i * 74, 82, 70, 38, () => { quantity = count; Refresh(); }));
            }

            feedbackLabel = Label(bar, "", 16, TextAnchor.MiddleLeft, MutedColor);
            Place(feedbackLabel.rectTransform, 16, 128, 1880, 28);
        }

        private void BuildContacts()
        {
            contactPanel = Box("Contacts", (RectTransform)root.transform, Background);
            contactPanel.anchorMin = new Vector2(1, 0); contactPanel.anchorMax = new Vector2(1, 1);
            contactPanel.pivot = new Vector2(1, 0);
            contactPanel.sizeDelta = new Vector2(430, -320);
            contactPanel.anchoredPosition = new Vector2(-12, 178);

            contactHeader = Label(contactPanel, "CONTACTS", 16, TextAnchor.MiddleLeft, MutedColor);
            Place(contactHeader.rectTransform, 12, 8, 400, 26);

            for (int i = 0; i < ContactRows; i++)
            {
                int index = i;
                Button button = MakeButton(contactPanel, "", 8, 40 + i * 40, 414, 36, () => AttackContact(index));
                contactButtons.Add(button);
                contactLabels.Add(button.GetComponentInChildren<Text>());
            }
        }

        // ---- refresh ------------------------------------------------------

        private void Refresh()
        {
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

            feedbackLabel.text = Time.unscaledTime < feedbackUntil ? feedback
                : (nav != null ? nav.Status : "") + "  ·  select a weapon, then a contact to engage";

            RefreshWeapons();
            RefreshQuantities();
            RefreshContacts();
        }

        private void RefreshWeapons()
        {
            WeaponCommandInfo[] weapons = WeaponOrders.GetWeapons(ship);
            while (weaponButtons.Count < weapons.Length)
            {
                int index = weaponButtons.Count;
                Button button = MakeButton(weaponRow, "", index * 250, 0, 244, 38, () => SelectWeapon(index));
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
                weaponButtons[i].image.color = weapon.Key == selectedKey ? SelectedColor : ButtonColor;
            }
        }

        private void RefreshQuantities()
        {
            WeaponCommandInfo selected = WeaponOrders.GetWeapons(ship).FirstOrDefault(w => w.Key == selectedKey);
            bool relevant = selected != null && !selected.Continuous;
            int[] counts = { 1, 2, 4, 8, 16 };
            for (int i = 0; i < quantityButtons.Count; i++)
            {
                quantityButtons[i].gameObject.SetActive(relevant);
                quantityButtons[i].image.color = counts[i] == quantity ? SelectedColor : ButtonColor;
            }
        }

        private void RefreshContacts()
        {
            contactUnits.Clear();
            WeaponInfo info = selectedKey != null
                ? WeaponOrders.StationsFor(ship, selectedKey).FirstOrDefault()?.WeaponInfo
                : null;

            var found = new List<(Unit Unit, float Range, float Opportunity)>();
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit == ship || unit.disabled || unit is Missile) continue;
                if (unit.NetworkHQ == null || unit.NetworkHQ == ship.NetworkHQ) continue;
                if (ship.NetworkHQ == null || !ship.NetworkHQ.TryGetKnownPosition(unit, out _)) continue;
                found.Add((unit, FastMath.Distance(ship.GlobalPosition(), unit.GlobalPosition()),
                    info != null ? WeaponOrders.Opportunity(info, unit) : 1f));
            }

            // With a weapon selected, rank by what it can actually hurt; with
            // none, the list is just the nearest contacts.
            IEnumerable<(Unit Unit, float Range, float Opportunity)> ordered = info != null
                ? found.OrderByDescending(c => c.Opportunity > 0.01f).ThenBy(c => c.Range)
                : found.OrderBy(c => c.Range);

            contactHeader.text = info != null
                ? "CONTACTS · engaging with " + info.weaponName
                : "CONTACTS · no weapon selected";

            int row = 0;
            foreach (var contact in ordered.Take(ContactRows))
            {
                contactUnits.Add(contact.Unit);
                bool capable = info == null || contact.Opportunity > 0.01f;
                contactLabels[row].text = (contact.Unit.definition?.unitName ?? contact.Unit.name) +
                    "\n" + (contact.Range / 1000f).ToString("0.0") + " km" +
                    (info != null ? (capable ? " · effective " + contact.Opportunity.ToString("0.00") : " · ineffective") : "");
                contactLabels[row].color = capable ? TextColor : MutedColor;
                contactButtons[row].gameObject.SetActive(true);
                row++;
            }
            for (int i = row; i < ContactRows; i++) contactButtons[i].gameObject.SetActive(false);
        }

        // ---- actions ------------------------------------------------------

        private void SelectWeapon(int index)
        {
            if (index >= weaponKeys.Count) return;
            selectedKey = weaponKeys[index] == selectedKey ? null : weaponKeys[index];
            Say(selectedKey == null ? "Weapon deselected" : "Select a contact to engage");
            Refresh();
        }

        private void AttackContact(int index)
        {
            if (ship == null || index >= contactUnits.Count) return;
            if (selectedKey == null) { Say("Select a weapon first."); return; }
            WeaponOrders.Attack(ship, selectedKey, contactUnits[index], quantity, out string reason);
            Say(reason);
            Refresh();
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
            button.onClick.AddListener(() => { if (ship != null) action(); });
            Text text = Label(rect, label, 15, TextAnchor.MiddleCenter);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(4, 0); text.rectTransform.offsetMax = new Vector2(-4, 0);
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

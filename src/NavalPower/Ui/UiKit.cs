using System;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // The handful of uGUI constructions every surface is made of. They were
    // private to the command bar; windows need the same ones, and one copy
    // keeps every panel looking like the same interface.
    internal static class UiKit
    {
        internal static Font Font;

        internal static RectTransform Box(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        internal static Text Label(RectTransform parent, string value, int size, TextAnchor alignment) =>
            Label(parent, value, size, alignment, Theme.Text);

        internal static Text Label(RectTransform parent, string value, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Font; text.fontSize = size; text.color = color; text.text = value; text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        // One line of text that has to stay inside its box: stepped down from
        // its normal size until it fits, to a floor a few sizes smaller.
        internal static void Fit(Text text, int size, int shrink = 3)
        {
            text.fontSize = size;
            float room = text.rectTransform.rect.width;
            if (room <= 1f) return;
            while (text.fontSize > size - shrink && text.preferredWidth > room) text.fontSize--;
        }

        internal static Button Button(RectTransform parent, string label, float x, float y,
            float width, float height, Action action) =>
            Button(parent, label, x, y, width, height, action, TextAnchor.MiddleCenter);

        internal static Button Button(RectTransform parent, string label, float x, float y,
            float width, float height, Action action, TextAnchor alignment)
        {
            RectTransform rect = Box(string.IsNullOrEmpty(label) ? "Button" : label, parent, Theme.Control);
            Place(rect, x, y, width, height);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.22f, 1.22f, 1.22f);
            colors.pressedColor = new Color(0.78f, 0.92f, 0.96f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            // Our surfaces are live in exactly two situations: commanding, and
            // flying one of our flights.
            if (action != null)
                button.onClick.AddListener(() => { if (CommandState.Active || PilotSeat.Active) action(); });
            Text text = Label(rect, label, 15, alignment);
            Fill(text.rectTransform, 10f, 0f);
            return button;
        }

        internal static Slider Slider(RectTransform parent, float x, float y, float width, float height)
        {
            RectTransform rect = Box("Slider", parent, new Color(0.25f, 0.3f, 0.34f));
            Place(rect, x, y, width, height);
            var slider = rect.gameObject.AddComponent<Slider>();
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            var area = new GameObject("Handle area", typeof(RectTransform)).GetComponent<RectTransform>();
            area.SetParent(rect, false);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(6, 0); area.offsetMax = new Vector2(-6, 0);
            RectTransform handle = Box("Handle", area, new Color(0.3f, 0.8f, 0.86f));
            handle.sizeDelta = new Vector2(13, height + 10f);
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            return slider;
        }

        // Top-left placement in parent units, which is how every layout here
        // is written.
        internal static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        internal static void Fill(RectTransform rect, float insetX = 0f, float insetY = 0f)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(insetX, insetY); rect.offsetMax = new Vector2(-insetX, -insetY);
        }

        internal static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);

        // Rich text for a word in a status colour, since the strip shows several
        // states in one line and colour must never carry meaning on its own.
        internal static string Tint(string text, Color color) => "<color=#" + Hex(color) + ">" + text + "</color>";
    }
}

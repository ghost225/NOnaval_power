using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NavalPower
{
    // A panel with a title bar and a scrolling column of rows. Every window is
    // one, and so is the right-click menu; they differ only in where they open
    // and whether they stay open after an order.
    //
    // A surface shows a *page*: a function that describes its rows by calling
    // Row, Info, Group and so on. The page is run again on every refresh, and
    // row n is the same object each time -- its text, colour and action are
    // replaced, the button itself is not. Only when the structure changes (a
    // flight lands and the list is a row shorter) are rows made or removed.
    //
    // That is what lets a window update live without eating clicks. Unity
    // completes a click on release, against the object that was pressed; the
    // old menus were rebuilt on a timer, and a rebuild between press and
    // release destroyed the button being clicked.
    internal sealed class Surface
    {
        internal const float Pitch = 38f, RowHeight = 34f, HeaderHeight = 40f;

        // Space the shell keeps for itself, in canvas units: the control strip
        // along the bottom, a margin along the top.
        internal static float ReservedBottom = 44f, ReservedTop = 12f;

        internal readonly string Key;
        internal readonly RectTransform Panel;
        internal readonly float Width;
        internal Action OnClosed;

        private readonly RectTransform parent, content;
        private readonly Canvas canvas;
        private readonly ScrollRect scroll;
        private readonly Text header;
        private readonly bool growUp;

        private Action<Surface> page;
        private bool pageChanged;
        private string title;
        private int cursor;

        private enum Kind { Button, Info, Group, Slider, Field }

        private sealed class RowView
        {
            internal Kind Kind;
            internal int Parts;
            internal RectTransform Rect;
            internal Button Button;
            internal Text Text;
            internal Action Action;
            internal Button[] Buttons;
            internal Text[] Texts;
            internal Action<int> GroupAction;
            internal Slider Slider;
            internal SliderHold Hold;
            internal Action<float> OnSlide;
            internal bool Updating;
            internal InputField Input;
            internal Action<string> OnCommit;
        }
        private readonly List<RowView> rows = new List<RowView>();

        internal bool IsOpen => Panel.gameObject.activeSelf;

        internal Surface(string key, RectTransform parent, Canvas canvas, float width, bool growUp, bool closable)
        {
            Key = key;
            Width = width;
            this.parent = parent;
            this.canvas = canvas;
            this.growUp = growUp;

            Panel = UiKit.Box("Surface " + key, parent, Theme.SurfaceRaised);
            Panel.anchorMin = Panel.anchorMax = Vector2.zero;
            Panel.pivot = growUp ? Vector2.zero : new Vector2(0, 1);
            Panel.gameObject.AddComponent<SurfaceFocus>().Owner = this;

            RectTransform bar = UiKit.Box("Title bar", Panel, Theme.Dim(Theme.Accent, 0.10f));
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.sizeDelta = new Vector2(0, HeaderHeight - 4f);
            bar.anchoredPosition = Vector2.zero;
            bar.gameObject.AddComponent<SurfaceDrag>().Owner = this;

            header = UiKit.Label(bar, "", Theme.CaptionSize + 1, TextAnchor.MiddleLeft, Theme.Accent);
            UiKit.Fill(header.rectTransform, 12f, 0f);
            header.rectTransform.offsetMax = new Vector2(closable ? -40f : -12f, 0f);
            header.supportRichText = true;

            if (closable)
            {
                Button close = UiKit.Button(bar, "✕", 0, 0, 30, 28, Close);
                var rect = (RectTransform)close.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
                rect.pivot = new Vector2(1, 0.5f);
                rect.anchoredPosition = new Vector2(-5f, 0f);
                close.image.color = Theme.Dim(Theme.Text, 0.06f);
            }

            RectTransform viewport = UiKit.Box("Viewport", Panel, new Color(0f, 0f, 0f, 0.004f));
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero; viewport.offsetMax = new Vector2(0f, -HeaderHeight);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = UiKit.Box("Content", viewport, new Color(0, 0, 0, 0));
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            scroll = Panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;
            scroll.inertia = false;

            Panel.gameObject.SetActive(false);
        }

        // ---- lifecycle ------------------------------------------------------

        internal void Show(Action<Surface> newPage)
        {
            page = newPage;
            pageChanged = true;
            if (!IsOpen) Panel.gameObject.SetActive(true);
            BringToFront();
            Render();
        }

        internal void Render()
        {
            if (page == null || !IsOpen) return;
            cursor = 0;
            title = null;
            page(this);
            for (int i = rows.Count - 1; i >= cursor; i--) Drop(i);
            header.text = title ?? "";
            Size();
            if (pageChanged) { content.anchoredPosition = Vector2.zero; pageChanged = false; }
        }

        internal void Close()
        {
            if (!IsOpen) return;
            Panel.gameObject.SetActive(false);
            OnClosed?.Invoke();
        }

        internal void BringToFront() => Panel.SetAsLastSibling();

        internal bool Contains(Vector2 screenPoint) =>
            IsOpen && RectTransformUtility.RectangleContainsScreenPoint(Panel, screenPoint, null);

        // ---- the page's vocabulary ------------------------------------------

        internal void Title(string text) => title = text;

        internal Button Row(string label, Action action)
        {
            RowView view = Take(Kind.Button, 1);
            view.Text.text = label;
            view.Action = action;
            view.Button.image.color = Theme.Control;
            view.Text.color = Theme.Text;
            view.Button.interactable = true;
            return view.Button;
        }

        internal void Info(string text) => Info(text, Theme.TextFaint);

        internal void Info(string text, Color color)
        {
            RowView view = Take(Kind.Info, 1);
            view.Text.text = text;
            view.Text.color = color;
        }

        // Several short choices side by side -- speed presets, salvo sizes --
        // rather than a row each.
        internal Button[] Group(string[] labels, Action<int> action)
        {
            RowView view = Take(Kind.Group, labels.Length);
            view.GroupAction = action;
            for (int i = 0; i < labels.Length; i++)
            {
                view.Texts[i].text = labels[i];
                view.Texts[i].color = Theme.Text;
                view.Buttons[i].image.color = Theme.Control;
            }
            return view.Buttons;
        }

        internal Slider SliderRow(float min, float max, float value, Action<float> onChanged)
        {
            RowView view = Take(Kind.Slider, 1);
            view.OnSlide = onChanged;
            // Never pull the handle out from under the hand dragging it.
            if (!view.Hold.Held)
            {
                view.Updating = true;
                view.Slider.minValue = min;
                view.Slider.maxValue = Mathf.Max(min + 0.01f, max);
                view.Slider.value = Mathf.Clamp(value, view.Slider.minValue, view.Slider.maxValue);
                view.Updating = false;
            }
            return view.Slider;
        }

        // A line of text to type into, committed on Enter or on clicking away.
        internal InputField Field(string current, Action<string> onCommit)
        {
            RowView view = Take(Kind.Field, 1);
            view.OnCommit = onCommit;
            // Never overwrite what is being typed.
            if (!view.Input.isFocused && view.Input.text != current) view.Input.SetTextWithoutNotify(current ?? "");
            return view.Input;
        }

        // ---- rows -----------------------------------------------------------

        private RowView Take(Kind kind, int parts)
        {
            int index = cursor++;
            if (index < rows.Count && rows[index].Kind == kind && rows[index].Parts == parts) return rows[index];
            // The shape changed here, so everything from here down is rebuilt.
            for (int i = rows.Count - 1; i >= index; i--) Drop(i);
            RowView made = Make(kind, parts, index);
            rows.Add(made);
            return made;
        }

        private void Drop(int index)
        {
            RowView view = rows[index];
            view.Rect.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(view.Rect.gameObject);
            rows.RemoveAt(index);
        }

        private RowView Make(Kind kind, int parts, int index)
        {
            float y = index * Pitch + 4f, inner = Width - 16f;
            var view = new RowView { Kind = kind, Parts = parts };
            switch (kind)
            {
                case Kind.Button:
                    view.Button = UiKit.Button(content, "", 8, y, inner, RowHeight, () => view.Action?.Invoke(),
                        TextAnchor.MiddleLeft);
                    view.Rect = (RectTransform)view.Button.transform;
                    view.Text = view.Button.GetComponentInChildren<Text>();
                    view.Text.supportRichText = true;
                    break;

                case Kind.Info:
                    view.Rect = UiKit.Box("Info", content, new Color(0, 0, 0, 0));
                    UiKit.Place(view.Rect, 8, y, inner, RowHeight);
                    view.Rect.GetComponent<Image>().raycastTarget = false;
                    view.Text = UiKit.Label(view.Rect, "", Theme.LabelSize + 1, TextAnchor.MiddleLeft, Theme.TextFaint);
                    UiKit.Fill(view.Text.rectTransform, 4f, 0f);
                    view.Text.supportRichText = true;
                    break;

                case Kind.Group:
                    view.Rect = UiKit.Box("Group", content, new Color(0, 0, 0, 0));
                    UiKit.Place(view.Rect, 8, y, inner, RowHeight);
                    view.Rect.GetComponent<Image>().raycastTarget = false;
                    view.Buttons = new Button[parts];
                    view.Texts = new Text[parts];
                    float each = (inner - (parts - 1) * 4f) / parts;
                    for (int i = 0; i < parts; i++)
                    {
                        int part = i;
                        view.Buttons[i] = UiKit.Button(view.Rect, "", i * (each + 4f), 0, each, RowHeight,
                            () => view.GroupAction?.Invoke(part));
                        view.Texts[i] = view.Buttons[i].GetComponentInChildren<Text>();
                    }
                    break;

                case Kind.Field:
                    view.Rect = UiKit.Box("Field", content, Theme.Dim(Theme.Text, 0.10f));
                    UiKit.Place(view.Rect, 8, y, inner, RowHeight);
                    Text typed = UiKit.Label(view.Rect, "", 16, TextAnchor.MiddleLeft, Theme.Text);
                    UiKit.Fill(typed.rectTransform, 10f, 0f);
                    typed.supportRichText = false;
                    Text hint = UiKit.Label(view.Rect, "Type a name, then Enter", 15, TextAnchor.MiddleLeft, Theme.TextFaint);
                    UiKit.Fill(hint.rectTransform, 10f, 0f);
                    view.Input = view.Rect.gameObject.AddComponent<InputField>();
                    view.Input.textComponent = typed;
                    view.Input.placeholder = hint;
                    view.Input.characterLimit = 32;
                    view.Input.lineType = InputField.LineType.SingleLine;
                    view.Input.targetGraphic = view.Rect.GetComponent<Image>();
                    view.Input.gameObject.AddComponent<InputCapture>();
                    view.Input.onEndEdit.AddListener(value => view.OnCommit?.Invoke(value));
                    break;

                case Kind.Slider:
                    view.Rect = UiKit.Box("Slider row", content, new Color(0, 0, 0, 0));
                    UiKit.Place(view.Rect, 8, y, inner, RowHeight);
                    view.Rect.GetComponent<Image>().raycastTarget = false;
                    view.Slider = UiKit.Slider(view.Rect, 6, 11, inner - 12f, 12f);
                    view.Hold = view.Slider.gameObject.AddComponent<SliderHold>();
                    view.Slider.onValueChanged.AddListener(value =>
                    {
                        if (view.Updating || !CommandState.Active) return;
                        view.OnSlide?.Invoke(value);
                    });
                    break;
            }
            return view;
        }

        // ---- size and place -------------------------------------------------

        private float Scale => canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
        private Vector2 Room => parent.rect.size;

        private void Size()
        {
            float wanted = cursor * Pitch + 8f;
            float room = Mathf.Max(160f, Room.y - ReservedBottom - ReservedTop);
            float height = Mathf.Min(wanted + HeaderHeight, room);
            Panel.sizeDelta = new Vector2(Width, height);
            content.sizeDelta = new Vector2(0, wanted);
            Clamp();
        }

        // Where it opens: wherever it was last left, or the default it was
        // given -- the drop-up position above its button.
        internal void Place(Vector2 fallback)
        {
            Panel.anchoredPosition = TryRecall(out Vector2 saved) ? saved : fallback;
            Clamp();
        }

        internal void Clamp()
        {
            Vector2 room = Room, size = Panel.sizeDelta, at = Panel.anchoredPosition;
            at.x = Mathf.Clamp(at.x, 0f, Mathf.Max(0f, room.x - size.x));
            if (growUp)
                at.y = Mathf.Clamp(at.y, ReservedBottom, Mathf.Max(ReservedBottom, room.y - ReservedTop - size.y));
            else
                at.y = Mathf.Clamp(at.y, ReservedBottom + size.y, Mathf.Max(ReservedBottom + size.y, room.y - ReservedTop));
            Panel.anchoredPosition = at;
        }

        internal void Nudge(Vector2 screenDelta)
        {
            Panel.anchoredPosition += screenDelta / Scale;
            Clamp();
        }

        // Positions are remembered as fractions of the screen, so a window put
        // at the right edge stays there after a resolution change.
        internal void Remember()
        {
            Vector2 room = Room;
            if (room.x < 1f || room.y < 1f) return;
            PlayerPrefs.SetFloat(Pref("x"), Panel.anchoredPosition.x / room.x);
            PlayerPrefs.SetFloat(Pref("y"), Panel.anchoredPosition.y / room.y);
        }

        private bool TryRecall(out Vector2 at)
        {
            at = default;
            if (!PlayerPrefs.HasKey(Pref("x"))) return false;
            Vector2 room = Room;
            at = new Vector2(PlayerPrefs.GetFloat(Pref("x")) * room.x, PlayerPrefs.GetFloat(Pref("y")) * room.y);
            return true;
        }

        private string Pref(string axis) => "NavalPower.window." + Key + "." + axis;
    }

    // Dragging the title bar moves the window; pressing anywhere on it raises it.
    internal sealed class SurfaceDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
    {
        internal Surface Owner;
        public void OnPointerDown(PointerEventData e) => Owner?.BringToFront();
        public void OnBeginDrag(PointerEventData e) { }
        public void OnDrag(PointerEventData e) => Owner?.Nudge(e.delta);
        public void OnEndDrag(PointerEventData e) => Owner?.Remember();
    }

    internal sealed class SurfaceFocus : MonoBehaviour, IPointerDownHandler
    {
        internal Surface Owner;
        public void OnPointerDown(PointerEventData e) => Owner?.BringToFront();
    }

    // Whether a slider is being held, so a refresh never yanks its handle.
    internal sealed class SliderHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        internal bool Held;
        public void OnPointerDown(PointerEventData e) => Held = true;
        public void OnPointerUp(PointerEventData e) => Held = false;
        private void OnDisable() => Held = false;
    }

    // While a field has focus the game's own bindings are switched off, the way
    // its chat box does it, or every letter typed would also be a command: a
    // throttle change, a view switch, the pause menu on Escape. Only the maps
    // this switched off are switched back on, and not until a moment after the
    // field lets go, so the Enter that committed the text is not also taken by
    // the game.
    internal sealed class InputCapture : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private readonly List<Rewired.ControllerMap> disabled = new List<Rewired.ControllerMap>();
        private bool captured, pauseWas;
        private float releaseAt = -1f;

        public void OnSelect(BaseEventData e)
        {
            releaseAt = -1f;
            if (captured) return;
            captured = true;
            try
            {
                Rewired.Player player = Rewired.ReInput.players.GetPlayer(0);
                foreach (Rewired.ControllerType type in new[] { Rewired.ControllerType.Keyboard, Rewired.ControllerType.Mouse })
                    foreach (Rewired.ControllerMap map in player.controllers.maps.GetAllMaps(type))
                        if (map.enabled) { map.enabled = false; disabled.Add(map); }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[ui] could not suspend game input: " + ex.Message); }
            CursorManager.SetFlag(CursorFlags.Chat, true);
            pauseWas = GameplayUI.AllowPauseKeybind;
            GameplayUI.AllowPauseKeybind = false;
        }

        public void OnDeselect(BaseEventData e) => releaseAt = Time.unscaledTime + 0.1f;

        private void Update()
        {
            if (releaseAt >= 0f && Time.unscaledTime >= releaseAt) Release();
        }

        private void OnDisable() => Release();

        private void Release()
        {
            releaseAt = -1f;
            if (!captured) return;
            captured = false;
            foreach (Rewired.ControllerMap map in disabled) if (map != null) map.enabled = true;
            disabled.Clear();
            CursorManager.SetFlag(CursorFlags.Chat, false);
            GameplayUI.AllowPauseKeybind = pauseWas;
        }
    }
}

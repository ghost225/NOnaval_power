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
        private readonly RectTransform viewport;
        private readonly Text minimizeLabel;
        private bool collapsed;

        internal bool Collapsed => collapsed;

        private Action<Surface> page;
        private bool pageChanged;
        private string title;
        private int cursor;
        private float cursorY;

        // The picture row, when the page has one -- a camera feed -- so the
        // wheel over it can zoom that camera rather than scroll the window.
        internal RectTransform ViewRect { get; private set; }

        private enum Kind { Button, Info, Group, Slider, Field, View, Spacer }

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
            internal RawImage Image;
            internal float Height = RowHeight;
        }
        private readonly List<RowView> rows = new List<RowView>();

        internal bool IsOpen => Panel.gameObject.activeSelf;

        private readonly RectTransform titleBar;
        private int titleButtons;
        private bool passThrough;

        internal Surface(string key, RectTransform parent, Canvas canvas, float width, bool growUp, bool closable)
            : this(key, parent, canvas, width, growUp, closable, minimizable: closable && growUp) { }

        internal Surface(string key, RectTransform parent, Canvas canvas, float width, bool growUp, bool closable,
            bool minimizable)
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

            // A solid bar, a shade lighter than the body, with the accent as a
            // hairline underneath rather than as the colour of the words: the
            // pale theme colour was hard to read as text.
            RectTransform bar = titleBar = UiKit.Box("Title bar", Panel, Theme.TitleBar);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.sizeDelta = new Vector2(0, HeaderHeight - 4f);
            bar.anchoredPosition = Vector2.zero;
            bar.gameObject.AddComponent<SurfaceDrag>().Owner = this;

            RectTransform rule = UiKit.Box("rule", bar, Theme.Dim(Theme.Accent, 0.55f));
            rule.anchorMin = new Vector2(0, 0); rule.anchorMax = new Vector2(1, 0);
            rule.pivot = new Vector2(0.5f, 0);
            rule.sizeDelta = new Vector2(0, 1.5f);
            rule.anchoredPosition = Vector2.zero;
            rule.GetComponent<Image>().raycastTarget = false;

            header = UiKit.Label(bar, "", Theme.CaptionSize + 1, TextAnchor.MiddleLeft, Theme.Text);
            header.fontStyle = FontStyle.Bold;
            UiKit.Fill(header.rectTransform, 12f, 0f);
            // Standing windows can be folded down to their title bar as well as
            // closed; the right-click menu only closes.
            header.supportRichText = true;
            if (closable) AddTitleButton("✕", Close);
            if (minimizable) minimizeLabel = AddTitleButton("–", ToggleCollapsed).GetComponentInChildren<Text>();

            viewport = UiKit.Box("Viewport", Panel, new Color(0f, 0f, 0f, 0.004f));
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

        // Buttons in the title bar, placed right to left in the order added,
        // with the title giving way to them.
        internal Button AddTitleButton(string label, Action action)
        {
            Button button = UiKit.Button(titleBar, label, 0, 0, 30, 28, action);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(1, 0.5f);
            rect.anchoredPosition = new Vector2(-5f - titleButtons * 34f, 0f);
            button.image.color = Theme.Control;
            Text glyph = button.GetComponentInChildren<Text>();
            glyph.fontSize = 16;
            glyph.fontStyle = FontStyle.Bold;
            glyph.color = Theme.Text;
            UiKit.Fill(glyph.rectTransform);
            // Close turns red under the cursor, so it is unmistakable which of
            // the two title buttons is about to be pressed.
            if (label == "✕")
            {
                ColorBlock colors = button.colors;
                colors.highlightedColor = new Color(1.9f, 0.7f, 0.65f);
                button.colors = colors;
            }
            titleButtons++;
            header.rectTransform.offsetMax = new Vector2(-12f - titleButtons * 34f, 0f);
            return button;
        }

        // A window whose body is a hole: nothing drawn behind its rows and no
        // clicks taken there, so whatever lies underneath -- the native map --
        // shows through and keeps its own input. Only the title bar is ours.
        internal void PassThrough()
        {
            passThrough = true;
            Panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            Panel.GetComponent<Image>().raycastTarget = false;
            viewport.GetComponent<Image>().raycastTarget = false;
            scroll.enabled = false;
        }

        // Folded down to the title bar, which drops to where the window's
        // bottom edge was -- out of the way, still labelled, one click from
        // coming back. The page keeps refreshing, so the title stays live.
        internal void ToggleCollapsed() => SetCollapsed(!collapsed);

        private void SetCollapsed(bool fold)
        {
            collapsed = fold;
            viewport.gameObject.SetActive(!fold);
            if (minimizeLabel != null) minimizeLabel.text = fold ? "▢" : "–";
            PlayerPrefs.SetInt(Pref("folded"), fold ? 1 : 0);
            if (IsOpen) Size();
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
            cursorY = 0f;
            ViewRect = null;
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
            IsOpen && RectTransformUtility.RectangleContainsScreenPoint(passThrough ? titleBar : Panel, screenPoint, null);

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
            RowView view;
            if (index < rows.Count && rows[index].Kind == kind && rows[index].Parts == parts)
            {
                view = rows[index];
            }
            else
            {
                // The shape changed here, so everything from here down is rebuilt.
                for (int i = rows.Count - 1; i >= index; i--) Drop(i);
                view = Make(kind, parts, index);
                rows.Add(view);
            }
            // Laid out by accumulated height rather than a fixed pitch, so a
            // picture can sit among ordinary rows.
            view.Rect.anchoredPosition = new Vector2(8f, -(cursorY + 4f));
            cursorY += view.Height + (Pitch - RowHeight);
            return view;
        }

        // Empty space of a given height, drawn as nothing and taking no clicks:
        // the hole a pass-through window leaves for what shows through it.
        internal RectTransform Spacer(float height)
        {
            RowView view = Take(Kind.Spacer, Mathf.RoundToInt(height));
            ViewRect = view.Rect;
            return view.Rect;
        }

        // A picture -- a camera feed's texture -- the full width of the window.
        internal RawImage View(Texture texture, float height)
        {
            RowView view = Take(Kind.View, Mathf.RoundToInt(height));
            view.Image.texture = texture;
            ViewRect = view.Rect;
            return view.Image;
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

                case Kind.View:
                    view.Height = parts;
                    view.Rect = UiKit.Box("View", content, Color.black);
                    UiKit.Place(view.Rect, 8, y, inner, parts);
                    var picture = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
                    picture.transform.SetParent(view.Rect, false);
                    UiKit.Fill((RectTransform)picture.transform, 1f, 1f);
                    view.Image = picture.GetComponent<RawImage>();
                    view.Image.raycastTarget = false;
                    break;

                case Kind.Spacer:
                    view.Height = parts;
                    view.Rect = UiKit.Box("Spacer", content, new Color(0, 0, 0, 0));
                    UiKit.Place(view.Rect, 8, y, inner, parts);
                    view.Rect.GetComponent<Image>().raycastTarget = false;
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
            float wanted = cursorY + 8f;
            float room = Mathf.Max(160f, Room.y - ReservedBottom - ReservedTop);
            float height = collapsed ? HeaderHeight - 4f : Mathf.Min(wanted + HeaderHeight, room);
            Panel.sizeDelta = new Vector2(Width, height);
            content.sizeDelta = new Vector2(0, wanted);
            Clamp();
        }

        // Where it opens: wherever it was last left, or the default it was
        // given -- the drop-up position above its button.
        internal void Place(Vector2 fallback)
        {
            if (minimizeLabel != null && PlayerPrefs.GetInt(Pref("folded"), 0) == 1 && !collapsed) SetCollapsed(true);
            Panel.anchoredPosition = TryRecall(out Vector2 saved) ? saved : fallback;
            Clamp();
        }

        // Put exactly here, ignoring where it was last left -- for a window that
        // belongs beside another one rather than wherever it was dragged.
        internal void PlaceAt(Vector2 at)
        {
            Panel.anchoredPosition = at;
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

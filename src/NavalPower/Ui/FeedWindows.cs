using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NavalPower
{
    // Camera feeds as windows. The live feed follows the action; each pinned
    // feed watches one unit. They are ordinary windows -- dragged anywhere,
    // closed with their ✕, remembered where they were left -- rather than
    // panels welded to the right edge, and closing a pinned feed's window is
    // how that pin comes down.
    internal sealed partial class CommandUi
    {
        private int weaponsSeen;
        private bool liveClosedByHand;
        private Ship feedShip;

        private static float FeedPicture => Settings.FeedWidth.Value;
        private static float FeedWindowWidth => FeedPicture + 16f;
        private static float FeedHeight => FeedPicture * 9f / 16f;

        // Keeps the windows in step with the feeds: a pin with no window gets
        // one, a window whose pin has gone closes, and a weapon leaving the
        // rails opens the live feed if it is not already up.
        private void WatchFeeds()
        {
            if (feedView == null) return;
            if (CommandState.Ship != feedShip)
            {
                feedShip = CommandState.Ship;
                liveClosedByHand = false;
                weaponsSeen = feedView.WeaponsAway;
            }

            feedView.WantLive = windows.TryGetValue("cam", out Surface live) && live.IsOpen;
            if (feedView.WeaponsAway != weaponsSeen)
            {
                weaponsSeen = feedView.WeaponsAway;
                if (Settings.FeedAutoOpen.Value && Settings.TargetFeed.Value && !liveClosedByHand && !feedView.WantLive)
                    OpenFeed("cam", LiveFeedPage, 0);
            }

            for (int slot = 1; slot <= TargetFeed.MaxPinned; slot++)
            {
                string key = "pin" + slot;
                bool open = windows.TryGetValue(key, out Surface window) && window.IsOpen;
                bool pinned = feedView.Find(slot) != null;
                int shown = slot;
                if (pinned && !open) OpenFeed(key, s => PinPage(s, shown), slot);
                else if (!pinned && open) window.Close();
            }
        }

        private void ToggleLiveFeed()
        {
            if (windows.TryGetValue("cam", out Surface live) && live.IsOpen) { live.Close(); return; }
            liveClosedByHand = false;
            OpenFeed("cam", LiveFeedPage, 0);
        }

        // Down the right edge, live feed first and pins below it, the way the
        // feeds always sat -- until moved, after which they open where left.
        private void OpenFeed(string key, Action<Surface> page, int slot)
        {
            Surface window = Window(key);
            bool wasOpen = window.IsOpen;
            window.Show(page);
            if (wasOpen) return;
            float height = window.Panel.sizeDelta.y;
            Vector2 room = windowLayer.rect.size;
            window.Place(new Vector2(room.x - window.Width - 12f,
                room.y - Surface.ReservedTop - height - slot * (height + 8f)));
        }

        private void LiveFeedPage(Surface s)
        {
            if (!Settings.TargetFeed.Value)
            {
                s.Title("CAMERA");
                s.Info("The target feed is switched off in the settings.", Theme.TextMuted);
                return;
            }
            bool live = feedView.HasSubject && feedView.LiveTexture != null;
            s.Title(live ? UiKit.Tint(feedView.Caption, feedView.CaptionColor) : "CAMERA  ·  nothing engaged");
            if (!live)
            {
                s.Info("Fire a weapon, order an engagement, or hover a contact", Theme.TextMuted);
                return;
            }
            Pannable(s.View(feedView.LiveTexture, FeedHeight), () => feedView.LivePan, pan => feedView.LivePan = pan);

            // Stepping through our own weapons, oldest first.
            if (feedView.CycleCount > 1 && feedView.CycleIndex >= 0)
            {
                var steps = s.Group(new[]
                {
                    "◀  older", (feedView.CycleIndex + 1) + " / " + feedView.CycleCount, "newer  ▶"
                }, i =>
                {
                    if (i == 0) feedView.Step(-1);
                    else if (i == 2) feedView.Step(1);
                });
                steps[1].image.color = Theme.Dim(Theme.Text, 0.04f);
            }
        }

        // Drag in the picture to swing the camera round what it watches; the
        // title bar still moves the window. Double-click puts it back.
        private static void Pannable(RawImage picture, Func<Vector2> get, Action<Vector2> set)
        {
            if (picture == null) return;
            picture.raycastTarget = true;
            var pan = picture.GetComponent<FeedPan>() ?? picture.gameObject.AddComponent<FeedPan>();
            pan.Get = get;
            pan.Set = set;
        }

        private void PinPage(Surface s, int slot)
        {
            TargetFeed.Pane pane = feedView.Find(slot);
            if (pane == null) { s.Close(); return; }
            s.Title(pane.Lost && pane.TrackedAtLoss ? UiKit.Tint("DESTROYED  ·  " + pane.Name, Theme.Bad)
                : !pane.Showing ? UiKit.Tint("CONTACT LOST  ·  " + pane.Name + "  ·  no current track", Theme.Warn)
                : UiKit.Tint("PINNED  ·  " + pane.Name, Theme.Passive));
            // Black rather than the last frame while the track is stale, and the
            // same size, so the window does not jump when the track comes back.
            Pannable(s.View(pane.Showing ? pane.Texture : Texture2D.blackTexture, FeedHeight), () => pane.Pan, pan => pane.Pan = pan);
        }
    }

    internal sealed class FeedPan : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerClickHandler
    {
        internal Func<Vector2> Get;
        internal Action<Vector2> Set;

        public void OnBeginDrag(PointerEventData data) { }

        public void OnDrag(PointerEventData data)
        {
            if (Get == null || Set == null) return;
            Vector2 pan = Get();
            pan.x = (pan.x + data.delta.x * 0.35f) % 360f;
            pan.y = Mathf.Clamp(pan.y - data.delta.y * 0.25f, -30f, 70f);
            Set(pan);
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (data.clickCount >= 2) Set?.Invoke(Vector2.zero);
        }
    }
}

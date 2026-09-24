using UnityEngine;

namespace NavalPower
{
    // The air operations bar takes the strip across the top of the screen, which
    // is where the game already puts its chat and kill feed. Rather than move
    // ours somewhere worse, the feed is nudged down by exactly the height of the
    // bar for as long as the bar is up, and put back the moment it is not.
    //
    // Its position is read once, when we first move it, so whatever the game or
    // another mod set is what it returns to.
    internal static class NativeChat
    {
        private static RectTransform feed;
        private static Vector2 resting;
        private static bool moved;

        internal static void MakeRoom(bool wanted)
        {
            if (wanted == moved) return;
            if (!Find()) return;
            if (wanted)
            {
                resting = feed.anchoredPosition;
                feed.anchoredPosition = resting - new Vector2(0f, Theme.AirBarHeight + 8f);
            }
            else
            {
                feed.anchoredPosition = resting;
            }
            moved = wanted;
        }

        internal static void Restore() => MakeRoom(false);

        private static bool Find()
        {
            if (feed != null) return true;
            MessageUI messages = SceneSingleton<MessageUI>.i;
            if (messages == null)
            {
                // It lives on the gameplay canvas, which is not always awake.
                foreach (MessageUI candidate in Resources.FindObjectsOfTypeAll<MessageUI>())
                    if (candidate.gameObject.scene.IsValid()) { messages = candidate; break; }
            }
            feed = messages != null ? messages.transform as RectTransform : null;
            if (feed == null) moved = false;      // nothing found; try again later
            return feed != null;
        }

        // A scene change invalidates everything we found.
        internal static void Forget()
        {
            feed = null;
            moved = false;
        }
    }
}

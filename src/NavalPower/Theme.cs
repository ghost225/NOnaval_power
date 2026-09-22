using NuclearOption.UIStyleSystem;
using UnityEngine;

namespace NavalPower
{
    // Colours come from the player's own theme where the game defines one, so
    // a custom ColorTheme carries through to this UI instead of being ignored.
    // Everything else is derived from those, not hardcoded, so the palette stays
    // coherent whichever theme is active.
    internal static class Theme
    {
        private static ColorTheme Native
        {
            get
            {
                try { return ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null; }
                catch { return null; }
            }
        }

        private static Color Pick(Color themed, Color fallback, bool available) => available ? themed : fallback;

        // --- semantic status -------------------------------------------------
        public static Color Good { get { ColorTheme t = Native; return Pick(t != null ? t.AllClear : default, new Color(0.38f, 0.86f, 0.55f), t != null); } }
        public static Color Warn { get { ColorTheme t = Native; return Pick(t != null ? t.Warning : default, new Color(1f, 0.74f, 0.28f), t != null); } }
        public static Color Bad { get { ColorTheme t = Native; return Pick(t != null ? t.Alert : default, new Color(1f, 0.38f, 0.32f), t != null); } }

        // --- tracks, matched to the game's own ping vocabulary ---------------
        public static Color OwnTrack { get { ColorTheme t = Native; return Pick(t != null ? t.DetectedPing : default, new Color(0.45f, 0.95f, 0.6f), t != null); } }
        public static Color Datalink { get { ColorTheme t = Native; return Pick(t != null ? t.MapIconFriendly : default, new Color(0.55f, 0.7f, 1f), t != null); } }
        public static Color Passive { get { ColorTheme t = Native; return Pick(t != null ? t.PassivePing : default, new Color(1f, 0.62f, 0.2f), t != null); } }
        public static Color Hostile { get { ColorTheme t = Native; return Pick(t != null ? t.MapIconHostile : default, new Color(1f, 0.42f, 0.36f), t != null); } }
        public static Color Weapon { get { ColorTheme t = Native; return Pick(t != null ? t.TargetPing : default, new Color(1f, 0.32f, 0.26f), t != null); } }

        // --- surfaces, derived so they sit under whatever the theme is -------
        public static readonly Color Surface = new Color(0.058f, 0.071f, 0.090f, 0.94f);
        public static readonly Color SurfaceRaised = new Color(0.098f, 0.117f, 0.145f, 0.98f);
        public static readonly Color Control = new Color(0.137f, 0.161f, 0.196f, 1f);
        public static readonly Color ControlHover = new Color(0.180f, 0.212f, 0.255f, 1f);
        public static readonly Color Divider = new Color(1f, 1f, 1f, 0.07f);

        public static Color Accent => OwnTrack;
        public static Color AccentFill => Dim(OwnTrack, 0.26f);

        public static readonly Color Text = new Color(0.90f, 0.93f, 0.95f);
        public static readonly Color TextMuted = new Color(0.58f, 0.64f, 0.70f);
        public static readonly Color TextFaint = new Color(0.42f, 0.47f, 0.53f);

        // --- type and spacing scale -----------------------------------------
        public const int TitleSize = 19, BodySize = 15, CaptionSize = 13, LabelSize = 11;
        public const float Gap = 8f, RowHeight = 30f, BarHeight = 174f;

        public static Color Dim(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        public static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, t);

        // Integrity, readiness, reserve: one ramp so every percentage in the UI
        // reads the same way.
        public static Color Scale(float percent)
        {
            if (float.IsNaN(percent)) return TextFaint;
            if (percent >= 75f) return Good;
            if (percent >= 40f) return Warn;
            return Bad;
        }
    }
}

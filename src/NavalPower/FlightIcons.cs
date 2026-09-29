using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using NOrders;

namespace NavalPower
{
    // Recolours the game's own map icon for our flights rather than drawing a
    // second symbol on top of it. The native icon already carries the right
    // shape and position; only its meaning needs changing.
    internal static class FlightIcons
    {
        private sealed class Tint
        {
            internal Color Original;
            internal Color Applied;
        }

        private static readonly Dictionary<UnitMapIcon, Tint> tinted = new Dictionary<UnitMapIcon, Tint>();
        private static readonly List<UnitMapIcon> stale = new List<UnitMapIcon>();

        // A fixed palette, not the theme's, and never red: red on the map is
        // the enemy, and an evading flight of ours drawn in the theme's alert
        // colour was being mistaken for a contact and engaged.
        internal static readonly Color Own = new Color(0.36f, 0.90f, 0.46f);         // green: on task
        internal static readonly Color Attention = new Color(1.00f, 0.88f, 0.20f);   // yellow: evading, or needs you
        internal static readonly Color Fighting = new Color(1.00f, 0.56f, 0.12f);    // orange: striking, engaging
        internal static readonly Color Cargo = new Color(0.30f, 0.72f, 1.00f);       // bright blue
        internal static readonly Color Jamming = new Color(0.76f, 0.52f, 1.00f);     // violet
        internal static readonly Color Homeward = new Color(0.45f, 0.78f, 0.70f);    // muted teal: returning

        internal static Color For(Flight flight)
        {
            if (flight == null) return Own;
            // Evading, or needing you, outranks the standing task on the map too.
            if (flight.Threat == FlightThreat.Missile || flight.Attention != null) return Attention;
            switch (flight.Mode)
            {
                case FlightMode.Strike:
                case FlightMode.Engage:
                case FlightMode.Egress: return Fighting;
                case FlightMode.Cargo: return Cargo;
                case FlightMode.Jam: return Jamming;
                case FlightMode.ReturnToBase: return Homeward;
                default: return flight.Interrupted ? Fighting : Own;
            }
        }

        internal static void Refresh(bool show)
        {
            stale.Clear();
            foreach (KeyValuePair<UnitMapIcon, Tint> entry in tinted) stale.Add(entry.Key);

            if (show)
            {
                foreach (Flight flight in FlightOrders.All())
                {
                    if (flight.Aircraft == null) continue;
                    if (!DynamicMap.TryGetMapIcon(flight.Aircraft, out UnitMapIcon icon) || icon == null) continue;
                    Image image = icon.iconImage;
                    if (image == null) continue;

                    Color want = For(flight);
                    // Selected flights read brighter so the one taking orders is
                    // obvious without a second marker.
                    if (CommandState.SelectedFlight == flight) want = Color.Lerp(want, Color.white, 0.35f);

                    if (!tinted.TryGetValue(icon, out Tint tint))
                    {
                        tint = new Tint { Original = image.color };
                        tinted[icon] = tint;
                    }
                    else stale.Remove(icon);

                    // The native icon recolours itself on theme and selection
                    // changes, so reapply whenever it has drifted back.
                    if (image.color != tint.Applied)
                    {
                        if (image.color != want) tint.Original = image.color;
                        image.color = want;
                        tint.Applied = want;
                    }
                    else if (tint.Applied != want)
                    {
                        image.color = want;
                        tint.Applied = want;
                    }
                }
            }

            // Anything no longer ours gets its own colour back.
            foreach (UnitMapIcon icon in stale)
            {
                if (icon != null && icon.iconImage != null && tinted.TryGetValue(icon, out Tint tint))
                    icon.iconImage.color = tint.Original;
                tinted.Remove(icon);
            }
        }

        internal static void Clear()
        {
            foreach (KeyValuePair<UnitMapIcon, Tint> entry in tinted)
                if (entry.Key != null && entry.Key.iconImage != null) entry.Key.iconImage.color = entry.Value.Original;
            tinted.Clear();
        }
    }
}

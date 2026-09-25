using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

        internal static Color For(Flight flight)
        {
            if (flight == null) return Theme.Accent;
            // Evading or fighting outranks the standing task on the map too.
            if (flight.Threat == FlightThreat.Missile) return Theme.Bad;
            if (flight.Interrupted) return Theme.Warn;
            switch (flight.Mode)
            {
                case FlightMode.Strike: return Theme.Weapon;
                case FlightMode.Jam: return Theme.Passive;
                case FlightMode.Cargo: return Theme.Datalink;
                case FlightMode.Egress: return Theme.Warn;
                case FlightMode.Engage: return Theme.Bad;
                case FlightMode.ReturnToBase: return Theme.Warn;
                case FlightMode.Station: return Theme.OwnTrack;
                default: return Theme.Accent;
            }
        }

        internal static void Refresh(Ship ship)
        {
            stale.Clear();
            foreach (KeyValuePair<UnitMapIcon, Tint> entry in tinted) stale.Add(entry.Key);

            if (ship != null)
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

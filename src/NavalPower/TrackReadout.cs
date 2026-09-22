using NuclearOption.Networking;
using UnityEngine;

namespace NavalPower
{
    // Tactical readout for one contact: what it is, where it was last seen,
    // how stale that is, and whether the selected weapon can reach it.
    // Reports only what the faction's own tracking database already holds --
    // hovering a contact reveals nothing the HQ has not observed.
    internal static class TrackReadout
    {
        internal static string Describe(Ship ship, Unit contact, WeaponCommandInfo weapon)
        {
            if (ship == null || contact == null) return "";
            FactionHQ hq = ship.NetworkHQ;
            string name = contact.definition?.unitName ?? contact.name;
            bool friendly = hq != null && contact.NetworkHQ == hq;

            GlobalPosition position;
            bool known = friendly;
            if (friendly) position = contact.GlobalPosition();
            else if (hq != null && hq.TryGetKnownPosition(contact, out position)) known = true;
            else position = contact.GlobalPosition();

            TrackingInfo track = hq != null ? hq.GetTrackingData(contact.persistentID) : null;
            bool observed = friendly || (track != null && track.Observed());

            var text = new System.Text.StringBuilder();
            text.Append(name).Append(friendly ? "  ·  FRIENDLY" : observed ? "  ·  TRACKED" : "  ·  STALE TRACK");

            if (!known)
            {
                text.Append("\nNo known position.");
                return text.ToString();
            }

            Vector3 offset = position - ship.GlobalPosition();
            float range = offset.magnitude;
            Vector3 flat = offset; flat.y = 0f;
            float bearing = (Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg + 360f) % 360f;

            text.Append("\nBearing ").Append(bearing.ToString("000")).Append("°  ·  range ")
                .Append((range / 1852f).ToString("0.0")).Append(" nm (").Append((range / 1000f).ToString("0.0")).Append(" km)");

            if (contact is Aircraft || contact.radarAlt > 50f)
                text.Append("\nAltitude ").Append((contact.radarAlt * 3.28084f).ToString("0")).Append(" ft");
            text.Append("\nSpeed ").Append((Mathf.Abs(contact.speed) / CommandableShip.MetresPerSecondPerKnot).ToString("0"))
                .Append(" kt");

            if (!friendly && track != null)
            {
                float age = Time.timeSinceLevelLoad - track.lastSpottedTime;
                text.Append(observed
                    ? "\nObserved now"
                    : "\nLast seen " + age.ToString("0") + " s ago · position estimated");
            }

            if (weapon != null)
            {
                WeaponInfo info = WeaponOrders.StationsFor(ship, weapon.Key) is var stations && stations.Length > 0
                    ? stations[0].WeaponInfo : null;
                float opportunity = WeaponOrders.Opportunity(info, contact);
                text.Append("\n").Append(weapon.Name).Append(": ");
                if (opportunity <= 0.01f) text.Append("cannot engage this target type");
                else if (range > weapon.MaxRange) text.Append("beyond range (max ")
                    .Append((weapon.MaxRange / 1852f).ToString("0.0")).Append(" nm)");
                else if (range < weapon.MinRange) text.Append("inside minimum range");
                else text.Append("in range · effectiveness ").Append(opportunity.ToString("0.00"));
            }

            return text.ToString();
        }
    }
}

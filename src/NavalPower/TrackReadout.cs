using NuclearOption.Networking;
using UnityEngine;
using NOrders;

namespace NavalPower
{
    // Tactical readout for one contact: what it is, where it was last seen,
    // how stale that is, and whether the selected weapon can reach it.
    // Reports only what the faction's own tracking database already holds --
    // hovering a contact reveals nothing the HQ has not observed.
    internal static class TrackReadout
    {
        internal static string DescribeEsm(Ship ship, EsmContact contact)
        {
            var text = new System.Text.StringBuilder();
            text.Append("ESM ").Append(contact.Id).Append("  ·  ").Append(contact.Type);
            if (contact.Stale) text.Append("  ·  STALE");
            text.Append("\nBearing ").Append(contact.BearingDegrees.ToString("000")).Append("°  ·  estimated range ")
                .Append(UnitConverter.DistanceReading(contact.RangeMetres));
            text.Append("\nLast heard ").Append(contact.AgeSeconds.ToString("0")).Append(" s ago");
            text.Append("\nUncertainty ±").Append(UnitConverter.DistanceReading(contact.RadialUncertaintyMetres))
                .Append(" along bearing · ±").Append(UnitConverter.DistanceReading(contact.CrossRangeUncertaintyMetres))
                .Append(" across");
            text.Append("\nPassive bearing only · not a fire-control track");
            return text.ToString();
        }

        // Whether the faction holds this contact right now: its own, or seen
        // by some sensor or datalink within the last few seconds. A picture of
        // a stale track would show where it really is, not where it was last
        // seen, which is knowledge the faction does not have.
        internal static bool IsCurrent(Unit contact)
        {
            FactionHQ hq = CommandState.Hq;
            if (contact == null || contact.disabled || hq == null) return false;
            if (contact.NetworkHQ == hq) return true;
            TrackingInfo track = hq.GetTrackingData(contact.persistentID);
            return track != null && track.Observed();
        }

        // Fit to target. A structure cannot move, so once found it stays a
        // target where it was seen, however long since anyone looked -- the
        // game's own AI already treats its known position as accurate. What
        // still needs eyes on it now is a camera: IsCurrent, not this.
        internal static bool IsTargetable(Unit contact)
        {
            if (IsCurrent(contact)) return true;
            FactionHQ hq = CommandState.Hq;
            if (contact == null || contact.disabled || hq == null) return false;
            if (Fixed(contact)) return hq.GetTrackingData(contact.persistentID) != null;
            return Holds(contact);
        }

        // Not seen for a while, but still where it was last seen: the
        // cockpit's own test for a valid (not outdated) marker -- within 20 m
        // of its last known position. A parked vehicle, a dug-in launcher or
        // a ship at anchor stays lockable from the cockpit long after the
        // 4 s of IsCurrent; it was shown stale here and could not be tasked.
        internal static bool Holds(Unit contact)
        {
            FactionHQ hq = CommandState.Hq;
            if (contact == null || contact.disabled || hq == null || contact.NetworkHQ == hq) return false;
            return hq.GetTrackingData(contact.persistentID) != null && hq.IsTargetPositionAccurate(contact, CockpitAccuracy);
        }

        private const float CockpitAccuracy = 20f;   // HUDUnitMarker's own threshold

        internal static bool Fixed(Unit contact) => contact is Building;

        // From the ship when there is one, else from the airfield being
        // commanded -- which has the faction's picture but no sensors of its own.
        internal static string Describe(Ship ship, Unit contact, WeaponCommandInfo weapon)
        {
            if (!CommandState.Active || contact == null) return "";
            FactionHQ hq = ship != null ? ship.NetworkHQ : CommandState.Hq;
            string name = contact is Ship named && ShipNames.IsNamed(named)
                ? ShipNames.Of(named) + "  ·  " + ShipNames.TypeOf(contact)
                : contact.definition?.unitName ?? contact.name;
            bool friendly = hq != null && contact.NetworkHQ == hq;

            GlobalPosition position;
            bool known = friendly;
            if (friendly) position = contact.GlobalPosition();
            else if (hq != null && hq.TryGetKnownPosition(contact, out position)) known = true;
            else position = contact.GlobalPosition();

            TrackingInfo track = hq != null ? hq.GetTrackingData(contact.persistentID) : null;
            bool observed = friendly || (track != null && track.Observed());
            bool holds = !observed && !Fixed(contact) && Holds(contact);

            var text = new System.Text.StringBuilder();
            text.Append(name).Append(friendly ? "  ·  FRIENDLY" : observed ? "  ·  TRACKED"
                : Fixed(contact) && track != null ? "  ·  FIXED TARGET" : holds ? "  ·  HELD" : "  ·  STALE TRACK");
            if (!friendly && ship != null) text.Append("\n").Append(TrackPicture.Describe(ship, contact));

            if (!known)
            {
                text.Append("\nNo known position.");
                return text.ToString();
            }

            Vector3 offset = position - (ship != null ? ship.GlobalPosition() : CommandState.PostPosition);
            float range = offset.magnitude;
            Vector3 flat = offset; flat.y = 0f;
            float bearing = (Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg + 360f) % 360f;

            text.Append("\nBearing ").Append(bearing.ToString("000")).Append("°  ·  range ")
                .Append(UnitConverter.DistanceReading(range));

            if (contact is Aircraft || contact.radarAlt > 50f)
                text.Append("\nAltitude ").Append(UnitConverter.AltitudeReading(contact.radarAlt));
            text.Append("\nSpeed ").Append(UnitConverter.SpeedReading(Mathf.Abs(contact.speed)));

            if (!friendly && track != null)
            {
                float age = Time.timeSinceLevelLoad - track.lastSpottedTime;
                text.Append(observed
                    ? "\nObserved now"
                    : Fixed(contact)
                        ? "\nLast seen " + age.ToString("0") + " s ago · it does not move · no camera without eyes on it"
                    : holds
                        ? "\nLast seen " + age.ToString("0") + " s ago · not moved since · no camera without eyes on it"
                        : "\nLast seen " + age.ToString("0") + " s ago · position estimated");
            }

            if (weapon != null && ship != null)
            {
                WeaponInfo info = WeaponOrders.StationsFor(ship, weapon.Key) is var stations && stations.Length > 0
                    ? stations[0].WeaponInfo : null;
                float opportunity = WeaponOrders.Opportunity(info, contact);
                text.Append("\n").Append(weapon.Name).Append(": ");
                if (opportunity <= 0.01f) text.Append("cannot engage this target type");
                else if (range > weapon.MaxRange) text.Append("beyond range (max ")
                    .Append(UnitConverter.DistanceReading(weapon.MaxRange)).Append(")");
                else if (range < weapon.MinRange) text.Append("inside minimum range");
                else text.Append("in range · effectiveness ").Append(opportunity.ToString("0.00"));
            }

            return text.ToString();
        }
    }
}

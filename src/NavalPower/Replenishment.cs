using System.Collections.Generic;
using UnityEngine;

namespace NavalPower
{
    public sealed class RearmSnapshot
    {
        public bool Requested;
        public bool Moving;
        public string NearestName;
        public float NearestRange = float.PositiveInfinity;
        public bool InRange;
        public int StationsShort;
        public string Reason;
    }

    // Rearming is the game's own: Unit.RequestRearm registers with the
    // faction's RearmMissionController, which matches the request to a Rearmer
    // in range and processes it. Nothing here reimplements that -- it asks on
    // the ship's behalf and says why the answer is no.
    //
    // Note the native precondition in Unit.SearchForRearm: speed at or below
    // 25, and effectively at surface level. A ship under way will not be
    // serviced, which is worth telling the commander rather than leaving them
    // to wonder.
    public static class Replenishment
    {
        private const float MaxSpeedForService = 25f;

        public static RearmSnapshot Status(Ship ship)
        {
            var result = new RearmSnapshot();
            if (ship == null) { result.Reason = "No ship."; return result; }

            result.Requested = ship.HasRequestedRearm;
            result.Moving = Mathf.Abs(ship.speed) > MaxSpeedForService;

            foreach (WeaponStation station in ship.weaponStations)
                if (station != null && station.Ammo < station.FullAmmo) result.StationsShort++;

            foreach (Rearmer rearmer in Object.FindObjectsOfType<Rearmer>())
            {
                if (rearmer == null || rearmer.Unit == null || rearmer.Unit.disabled) continue;
                if (rearmer.Unit.NetworkHQ != ship.NetworkHQ) continue;
                float range = FastMath.Distance(ship.GlobalPosition(), rearmer.GetPosition());
                if (range >= result.NearestRange) continue;
                result.NearestRange = range;
                result.NearestName = rearmer.Unit.definition?.unitName ?? rearmer.Unit.name;
                result.InRange = range <= rearmer.Range;
            }

            result.Reason =
                result.StationsShort == 0 ? "Magazines full."
                : result.NearestName == null ? "No friendly rearming point."
                : result.Moving ? "Too fast to be serviced · come to under " +
                    UnitConverter.SpeedReadingGround(MaxSpeedForService)
                : !result.InRange ? "Nearest is " + result.NearestName + " at " +
                    UnitConverter.DistanceReading(result.NearestRange) + " · out of its reach"
                : result.Requested ? "Requested from " + result.NearestName
                : "Ready to request from " + result.NearestName;
            return result;
        }

        public static bool Request(Ship ship, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            RearmSnapshot status = Status(ship);
            if (status.StationsShort == 0) { reason = "Magazines are already full."; return false; }
            if (status.NearestName == null) { reason = "Nothing in the faction can rearm this ship."; return false; }
            if (ship.HasRequestedRearm) { reason = "Already requested; waiting on " + status.NearestName + "."; return false; }

            ship.RequestRearm();
            reason = status.Moving
                ? "Rearm requested · slow below " + UnitConverter.SpeedReadingGround(MaxSpeedForService) + " to be serviced"
                : status.InRange
                    ? "Rearm requested from " + status.NearestName
                    : "Rearm requested · close on " + status.NearestName + ", " +
                      UnitConverter.DistanceReading(status.NearestRange) + " away";
            Plugin.Log.LogInfo("[rearm] " + (ship.definition?.unitName ?? ship.name) + ": " + reason);
            return true;
        }
    }
}

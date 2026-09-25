using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace NavalPower
{
    // A land airbase as something to command.
    //
    // There is no airbase unit. Airbase is a NetworkBehaviour, not a Unit, so
    // the camera cannot follow it and nothing about "follow a ship to command
    // it" carries over. What it does have is everything air operations need:
    // its own hangars, the same TrySpawnAircraft a carrier deck uses, and a
    // centre to put the camera over. A ship's deck is an Airbase too -- one
    // attached to the hull -- which is why air operations are written against
    // Airbase throughout, and why an attached one is never commanded here:
    // that is the ship's, and commanding the ship already covers it.
    internal static class Airfields
    {
        internal static Ship ShipOf(Airbase airbase) =>
            airbase != null && airbase.TryGetAttachedUnit(out Unit unit) ? unit as Ship : null;

        internal static string NameOf(Airbase airbase)
        {
            if (airbase == null) return "unknown";
            Ship ship = ShipOf(airbase);
            if (ship != null) return ship.definition?.unitName ?? ship.name;
            string name = airbase.SavedAirbase?.DisplayName;
            return string.IsNullOrEmpty(name) ? airbase.name : name;
        }

        internal static GlobalPosition PositionOf(Airbase airbase) =>
            airbase.center != null ? airbase.center.GlobalPosition() : airbase.transform.GlobalPosition();

        internal static bool CanCommand(Airbase airbase, out string reason)
        {
            reason = null;
            if (airbase == null) { reason = "No airbase."; return false; }
            if (airbase.AttachedAirbase) { reason = "A ship's deck is commanded from the ship."; return false; }
            if (!MissionManager.IsRunning || airbase.disabled) { reason = "This airbase is out of action."; return false; }
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null)
            { reason = "A local player is required."; return false; }
            if (player.HQ == null || airbase.CurrentHQ != player.HQ)
            { reason = "You can command only your own faction's airbases."; return false; }
            // Launches spend the faction's airframes and money; that is the host's.
            if (!airbase.IsServer) { reason = "Airbase command requires the mission host."; return false; }
            return true;
        }

        // Every land base the local faction holds, nearest the camera first.
        internal static List<Airbase> Friendly()
        {
            var result = new List<Airbase>();
            if (!GameManager.GetLocalHQ(out FactionHQ hq) || hq == null) return result;
            foreach (Airbase airbase in hq.GetAirbases())
                if (airbase != null && !airbase.AttachedAirbase && !airbase.disabled) result.Add(airbase);
            var cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras != null)
            {
                Vector3 from = cameras.transform.position;
                result.Sort((a, b) => (PositionOf(a).ToLocalPosition() - from).sqrMagnitude
                    .CompareTo((PositionOf(b).ToLocalPosition() - from).sqrMagnitude));
            }
            return result;
        }

        internal static void Hangars(Airbase airbase, out int ready, out int busy)
        {
            ready = 0; busy = 0;
            if (airbase == null) return;
            foreach (Hangar hangar in airbase.hangars)
            {
                if (hangar == null || !hangar.IsFunctional()) continue;
                if (hangar.Available) ready++; else busy++;
            }
        }
    }
}

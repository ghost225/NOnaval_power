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

        // Whether a hangar can be used at all. The game's own airbase panel
        // counts every hangar that is not disabled; the sea-level test is only
        // for a deck, whose hangars go under with the ship. Applied to land it
        // dropped hangars on low coastal fields whose pivots sit near the datum.
        internal static bool Serviceable(Hangar hangar) =>
            hangar != null && !hangar.Disabled &&
            (hangar.parentAirbase == null || !hangar.parentAirbase.AttachedAirbase || hangar.IsFunctional());

        // Serviceable hangars, split by whether one is free to build right now:
        // a hangar is busy while its doors cycle and an aircraft rolls out.
        internal static void Hangars(Airbase airbase, out int ready, out int busy)
        {
            ready = 0; busy = 0;
            if (airbase == null) return;
            foreach (Hangar hangar in airbase.hangars)
            {
                if (!Serviceable(hangar)) continue;
                if (hangar.Available) ready++; else busy++;
            }
        }

        // What the field has, the way the game's own tooltip counts it: by kind.
        internal static string Inventory(Airbase airbase)
        {
            if (airbase == null) return "";
            var counts = new SortedDictionary<string, int>();
            foreach (Hangar hangar in airbase.hangars)
            {
                if (!Serviceable(hangar)) continue;
                string kind = KindOf(hangar);
                counts.TryGetValue(kind, out int n);
                counts[kind] = n + 1;
            }
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> entry in counts)
                parts.Add(entry.Value + " " + entry.Key + (entry.Value == 1 ? "" : "s"));
            return parts.Count == 0 ? "no hangars" : string.Join(" · ", parts.ToArray());
        }

        private static string KindOf(Hangar hangar)
        {
            UnitDefinition definition = hangar.attachedUnit != null ? hangar.attachedUnit.definition : null;
            switch (definition?.code)
            {
                case "HPAD": return "helipad";
                case "REV": return "revetment";
                case "HGR-M": return "hangar";
                case "HGR-H": return "shelter";
                case "SHP": return "deck spot";
                default: return definition != null ? definition.unitName.ToLowerInvariant() : "hangar";
            }
        }

        // Every hangar the field holds, and every one nearby that it does not:
        // said once per field taken, so a count that looks short can be read
        // against what is actually there.
        internal static void Report(Airbase airbase)
        {
            if (airbase == null) return;
            var lines = new System.Text.StringBuilder();
            lines.Append("[field] ").Append(NameOf(airbase)).Append(" · ").Append(airbase.hangars.Count)
                .Append(" hangar(s) registered · ").Append(Inventory(airbase));
            foreach (Hangar hangar in airbase.hangars)
            {
                if (hangar == null) continue;
                AircraftDefinition[] offers = hangar.GetAvailableAircraft();
                lines.Append("\n  ").Append(hangar.attachedUnit != null ? hangar.attachedUnit.name : hangar.name)
                    .Append(" [").Append(KindOf(hangar)).Append("]")
                    .Append(hangar.Disabled ? " DISABLED" : "")
                    .Append(hangar.Available ? " free" : " busy")
                    .Append(" · y ").Append((hangar.transform.position.y - Datum.LocalSeaY).ToString("0"))
                    .Append(" m · ").Append(offers != null ? offers.Length : 0).Append(" airframe(s)");
            }
            float radius = Mathf.Max(airbase.GetRadius(), 1500f);
            Vector3 centre = PositionOf(airbase).ToLocalPosition();
            foreach (Hangar hangar in Object.FindObjectsOfType<Hangar>())
            {
                if (hangar == null || hangar.parentAirbase == airbase) continue;
                if ((hangar.transform.position - centre).sqrMagnitude > radius * radius) continue;
                lines.Append("\n  nearby, not this field's: ")
                    .Append(hangar.attachedUnit != null ? hangar.attachedUnit.name : hangar.name)
                    .Append(" [").Append(KindOf(hangar)).Append("] belongs to ")
                    .Append(hangar.parentAirbase != null ? NameOf(hangar.parentAirbase) : "no airbase");
            }
            Plugin.Log.LogInfo(lines.ToString());
        }
    }
}

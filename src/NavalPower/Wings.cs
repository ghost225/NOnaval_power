using System.Collections.Generic;
using UnityEngine;

namespace NavalPower
{
    // Aircraft that fly and take orders as one.
    //
    // A wing is its members' shared callsign ("Viper 1") and a lead. Every
    // member is still an ordinary Flight -- threat reactions, fuel, recovery
    // and taking the controls all keep working per aircraft -- and wingmen fly
    // a formation slot on the lead. The first member off the deck leads, so a
    // wing assembles over its home as the rest launch: the lead holds there on
    // its default task, and each later member flies to its slot wherever the
    // lead has got to.
    internal static class Wings
    {
        private sealed class Record
        {
            internal string Name;
            internal Flight Lead;
        }

        private static readonly Dictionary<string, Record> wings = new Dictionary<string, Record>();

        private static bool Alive(Flight flight) =>
            flight != null && flight.Aircraft != null && !flight.Aircraft.disabled;

        // A new member, just off the deck: the lead if the wing has none alive,
        // otherwise into formation on it.
        internal static void Joined(Flight flight)
        {
            if (flight?.Wing == null) return;
            if (!wings.TryGetValue(flight.Wing, out Record record))
                wings[flight.Wing] = record = new Record { Name = flight.Wing };
            if (!Alive(record.Lead) || record.Lead.Wing != record.Name)
            {
                record.Lead = flight;
                return;
            }
            flight.Mode = FlightMode.Formation;
            flight.Altitude = record.Lead.Altitude;
            flight.Roe = record.Lead.Roe;
        }

        internal static Flight LeadOf(Flight flight)
        {
            if (flight?.Wing == null || !wings.TryGetValue(flight.Wing, out Record record)) return flight;
            return Alive(record.Lead) && record.Lead.Wing == flight.Wing ? record.Lead : flight;
        }

        internal static bool IsLead(Flight flight) => flight?.Wing != null && LeadOf(flight) == flight;
        internal static bool IsWingman(Flight flight) => flight?.Wing != null && LeadOf(flight) != flight;

        // Alive members, in callsign order: 1-1, 1-2, 1-3, 1-4.
        internal static List<Flight> Members(string wing)
        {
            var result = new List<Flight>();
            if (wing == null) return result;
            foreach (Flight flight in FlightOrders.All()) if (flight.Wing == wing) result.Add(flight);
            result.Sort((a, b) => string.CompareOrdinal(a.Label ?? "", b.Label ?? ""));
            return result;
        }

        // The flight and everyone it flies with.
        internal static List<Flight> Group(Flight flight) =>
            flight?.Wing != null ? Members(flight.Wing) : new List<Flight> { flight };

        internal static IEnumerable<string> Names()
        {
            foreach (KeyValuePair<string, Record> entry in wings)
                if (Members(entry.Key).Count > 0) yield return entry.Key;
        }

        // Anyone actually flying formation on this lead: without them the lead
        // has no reason to hold back on the throttle.
        internal static bool HasFollowers(Flight lead)
        {
            if (!IsLead(lead)) return false;
            foreach (Flight member in Members(lead.Wing))
                if (member != lead && member.Mode == FlightMode.Formation) return true;
            return false;
        }

        // Where a wingman sits, in the lead's frame: x to the right, z ahead.
        // A loose tactical spread rather than parade formation -- the autopilot
        // is not precise enough for close work, and nothing here needs it.
        internal static Vector3 SlotOffset(Flight flight, bool rotary)
        {
            int slot = 0;
            foreach (Flight member in Members(flight.Wing))
            {
                if (member == LeadOf(flight)) continue;
                slot++;
                if (member == flight) break;
            }
            Vector3 offset;
            switch (slot)
            {
                case 1: offset = new Vector3(600f, 0f, -450f); break;      // right, stepped back
                case 2: offset = new Vector3(-600f, 0f, -450f); break;     // left
                default: offset = new Vector3(1200f, 0f, -900f); break;    // second right, further back
            }
            return rotary ? offset * 0.4f : offset;
        }

        // Where a wingman's slot is right now, and which way the lead is going.
        internal static bool Slot(Flight flight, out GlobalPosition slot, out Vector3 forward, out Vector3 velocity)
        {
            slot = default; forward = Vector3.forward; velocity = Vector3.zero;
            Flight lead = LeadOf(flight);
            Aircraft leader = lead?.Aircraft;
            if (lead == flight || leader == null || leader.disabled || flight.Aircraft == null) return false;
            velocity = leader.rb != null ? leader.rb.velocity : leader.transform.forward * 100f;
            forward = new Vector3(velocity.x, 0f, velocity.z);
            if (forward.sqrMagnitude < 25f) forward = new Vector3(leader.transform.forward.x, 0f, leader.transform.forward.z);
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            Vector3 offset = SlotOffset(flight, !(flight.Aircraft.autopilot is AutopilotPlane));
            slot = leader.GlobalPosition() + right * offset.x + forward * offset.z;
            return true;
        }

        // How far the furthest-behind wingman is from its slot, along the lead's
        // track, in metres: what the lead slows down for.
        internal static float Straggle(Flight lead)
        {
            if (!IsLead(lead)) return 0f;
            float worst = 0f;
            foreach (Flight member in Members(lead.Wing))
            {
                if (member == lead || member.Mode != FlightMode.Formation) continue;
                if (!Slot(member, out GlobalPosition slot, out Vector3 forward, out _)) continue;
                Vector3 gap = slot - member.Aircraft.GlobalPosition();
                worst = Mathf.Max(worst, Vector3.Dot(gap, forward));
            }
            return worst;
        }

        internal static void Detach(Flight flight)
        {
            if (flight?.Wing == null) return;
            string wing = flight.Wing;
            bool wasLead = IsLead(flight);
            flight.Wing = null;
            if (flight.Mode == FlightMode.Formation && flight.Aircraft != null)
            {
                flight.OrbitCentre = flight.Aircraft.GlobalPosition();
                flight.Mode = FlightMode.Orbit;
            }
            if (wasLead) Promote(wing, flight);
        }

        internal static void Join(Flight flight, string wing)
        {
            if (flight == null || wing == null || flight.Wing == wing) return;
            Detach(flight);
            // It takes the wing's name, with the lowest number not in use.
            var used = new HashSet<int>();
            foreach (Flight member in Members(wing)) used.Add(Number(member.Label));
            int n = 1;
            while (used.Contains(n)) n++;
            flight.Wing = wing;
            FlightOrders.Rename(flight, wing + "-" + n);
            Joined(flight);
            flight.Adopted = false;                     // ours to fly again, if the combat pilot had it
        }

        // "Viper 1-3" is number 3 in its wing.
        private static int Number(string label)
        {
            if (string.IsNullOrEmpty(label)) return 0;
            int dash = label.LastIndexOf('-');
            return dash >= 0 && int.TryParse(label.Substring(dash + 1), out int n) ? n : 0;
        }

        // A wing is named, and its members are named from it: renaming the
        // wing renames every aircraft in it, keeping each one's number, and
        // those still waiting to launch.
        internal static bool Rename(string wing, string name, out string reason)
        {
            name = (name ?? "").Trim();
            reason = null;
            if (wing == null || name.Length == 0 || name == wing) return false;
            foreach (string other in Names())
                if (other == name) { reason = "Another wing is already called " + name + "."; return false; }

            List<Flight> members = Members(wing);
            int next = 1;
            foreach (Flight member in members)
            {
                int n = Number(member.Label);
                if (n <= 0) n = next;
                next = Mathf.Max(next, n) + 1;
                member.Wing = name;
                FlightOrders.Rename(member, name + "-" + n);
            }
            if (wings.TryGetValue(wing, out Record record))
            {
                wings.Remove(wing);
                record.Name = name;
                wings[name] = record;
            }
            LaunchQueue.RenameWing(wing, name);
            FlightOrders.RenameWing(wing, name);
            Plugin.Log.LogInfo("[wing] " + wing + " renamed " + name);
            return true;
        }

        internal static void Tick()
        {
            foreach (Record record in new List<Record>(wings.Values))
            {
                List<Flight> members = Members(record.Name);
                if (members.Count == 0) { wings.Remove(record.Name); continue; }
                if (!Alive(record.Lead) || record.Lead.Wing != record.Name) Promote(record.Name, record.Lead);

                // The lead going home takes the wing with it, rather than
                // leaving wingmen to follow it into the landing pattern.
                Flight lead = LeadOf(members[0]);
                if (lead.Mode != FlightMode.ReturnToBase) continue;
                foreach (Flight member in members)
                    if (member != lead && member.Mode == FlightMode.Formation) FlightOrders.ReturnToBase(member);
            }
        }

        // The next member takes the lead and carries on with the old lead's
        // orders: the Flight object outlives its aircraft, orders and all.
        private static void Promote(string wing, Flight previous)
        {
            if (!wings.TryGetValue(wing, out Record record)) return;
            Flight next = null;
            foreach (Flight member in Members(wing)) { next = member; break; }
            record.Lead = next;
            if (next == null) return;
            if (previous != null && next.Mode == FlightMode.Formation)
            {
                next.Mode = previous.Mode == FlightMode.Formation ? FlightMode.Orbit : previous.Mode;
                next.PreviousMode = previous.PreviousMode;
                next.Route.Clear();
                next.Route.AddRange(previous.Route);
                next.OrbitCentre = previous.OrbitCentre;
                next.OrbitRadius = previous.OrbitRadius;
                next.ConfineToArea = previous.ConfineToArea;
                next.Altitude = previous.Altitude;
                next.Target = previous.Target;
                next.PreferredWeapon = previous.PreferredWeapon;
                next.StationOffset = previous.StationOffset;
                if (next.Mode == FlightMode.Orbit && next.Route.Count == 0 && previous.Mode == FlightMode.Formation)
                    next.OrbitCentre = next.Aircraft.GlobalPosition();
                next.Adopted = false;
            }
            Plugin.Log.LogInfo("[wing] " + wing + " · " + next.Name + " takes the lead");
            CommandState.Say(wing + " · " + next.Name + " takes the lead");
        }
    }

    // Orders as the player gives them. A wing takes an order as a wing: where
    // to go is the lead's business and the wingmen follow; whom to hit, when
    // to go home and how freely to fight are every member's. FlightOrders
    // itself stays per aircraft, because it calls its own orders internally --
    // a re-attack after egress is one aircraft's, not the wing's.
    internal static class WingOrders
    {
        // Movement: the lead flies it; wingmen fall back into formation.
        private static Flight Led(Flight flight)
        {
            Flight lead = Wings.LeadOf(flight);
            if (lead?.Wing == null) return lead;
            foreach (Flight member in Wings.Members(lead.Wing))
            {
                if (member == lead || member.Mode == FlightMode.Formation) continue;
                if (member.Mode == FlightMode.ReturnToBase && lead.Mode != FlightMode.ReturnToBase) continue;   // bingo is bingo
                member.Mode = FlightMode.Formation;
                member.Target = null;
                member.Adopted = false;
            }
            return lead;
        }

        public static void SetRoute(Flight flight, GlobalPosition point, bool append) =>
            FlightOrders.SetRoute(Led(flight), point, append);

        public static void SetArea(Flight flight, GlobalPosition centre, float radius) =>
            FlightOrders.SetArea(Led(flight), centre, radius);

        public static void Station(Flight flight, Ship on = null) => FlightOrders.Station(Led(flight), on);

        public static void Jam(Flight flight, Unit target) => FlightOrders.Jam(Led(flight), target);

        public static void Deliver(Flight flight, GlobalPosition where, bool airdrop) =>
            FlightOrders.Deliver(Led(flight), where, airdrop);

        public static void SetConfined(Flight flight, bool confined)
        {
            foreach (Flight member in Wings.Group(flight)) FlightOrders.SetConfined(member, confined);
        }

        public static void SetAltitude(Flight flight, float metres)
        {
            foreach (Flight member in Wings.Group(flight)) FlightOrders.SetAltitude(member, metres);
        }

        public static void SetOrbitRadius(Flight flight, float metres) =>
            FlightOrders.SetOrbitRadius(Wings.LeadOf(flight), metres);

        // Every member that can hurt it goes in; the rest keep formation.
        public static void Strike(Flight flight, Unit target, string preferredWeapon = null)
        {
            List<Flight> capable = FlightOrders.CapableOf(target);
            bool any = false;
            foreach (Flight member in Wings.Group(flight))
            {
                if (member != flight && !capable.Contains(member)) continue;
                FlightOrders.Strike(member, target, member == flight ? preferredWeapon : null);
                any = true;
            }
            if (!any) FlightOrders.Strike(flight, target, preferredWeapon);
        }

        public static void Engage(Flight flight)
        {
            foreach (Flight member in Wings.Group(flight)) FlightOrders.Engage(member);
        }

        public static void SetRoe(Flight flight, FlightRoe roe)
        {
            foreach (Flight member in Wings.Group(flight)) FlightOrders.SetRoe(member, roe);
        }

        public static void ReturnToBase(Flight flight)
        {
            foreach (Flight member in Wings.Group(flight)) FlightOrders.ReturnToBase(member);
        }

        public static void RecoverToShip(Flight flight) => ReturnToBase(flight);
    }
}

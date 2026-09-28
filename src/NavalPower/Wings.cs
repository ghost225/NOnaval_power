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
            internal float Spread;              // 0 close route formation, 1 combat spread
            internal float LastCombat = -999f;
            internal bool JoiningUp;
            internal GlobalPosition JoinPoint;
            internal float JoinStarted, NextJoinAllowed;
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
        // Where a wingman sits, in the lead's frame: x to the right, z ahead.
        //
        // Two shapes, blended: a close route formation in transit -- about
        // 120 m abreast, as near as the autopilot can hold safely and near
        // enough to read as a wing -- and combat spread, about a mile abreast,
        // once the wing is threatened, attacking or escaping. Real fingertip is
        // a few wingspans; the game's autopilot is not that precise.
        internal static Vector3 SlotOffset(Flight flight, bool rotary)
        {
            int slot = 0;
            foreach (Flight member in Members(flight.Wing))
            {
                if (member == LeadOf(flight)) continue;
                slot++;
                if (member == flight) break;
            }
            float close = Settings.CloseSpacing.Value, spread = Settings.CombatSpacing.Value;
            Vector3 near, wide;
            switch (slot)
            {
                case 1: near = new Vector3(1f, 0f, -0.5f) * close; wide = new Vector3(1f, 0f, -0.12f) * spread; break;
                case 2: near = new Vector3(-1f, 0f, -0.5f) * close; wide = new Vector3(-1f, 0f, -0.12f) * spread; break;
                default: near = new Vector3(2f, 0f, -1f) * close; wide = new Vector3(0.5f, 0f, -0.9f) * spread; break;
            }
            Vector3 offset = Vector3.Lerp(near, wide, SpreadOf(flight));
            return rotary ? offset * 0.5f : offset;
        }

        // An escort's lead flies cover off the escorted lead's right shoulder
        // and above it: close in transit, well out once there is a fight.
        private static Vector3 EscortOffset(Flight flight, bool rotary)
        {
            Vector3 offset = Vector3.Lerp(new Vector3(700f, 0f, 150f), new Vector3(2500f, 0f, 500f), SpreadOf(flight));
            return rotary ? offset * 0.5f : offset;
        }

        internal static float SpreadOf(Flight flight)
        {
            Flight lead = LeadOf(flight);
            if (lead?.Wing != null && wings.TryGetValue(lead.Wing, out Record record)) return record.Spread;
            return lead != null && lead.Escorting != null && InCombat(new List<Flight> { lead }) ? 1f : 0f;
        }

        // The group this flight escorts, by its current lead.
        internal static Flight EscortedLead(Flight flight)
        {
            Flight target = LeadOf(flight)?.Escorting;
            if (target == null) return null;
            if (target.Wing != null)
            {
                List<Flight> members = Members(target.Wing);
                return members.Count > 0 ? LeadOf(members[0]) : null;
            }
            return Alive(target) ? target : null;
        }

        internal static void Escort(Flight flight, Flight target)
        {
            Flight lead = LeadOf(flight);
            if (lead == null || target == null) return;
            if (Group(lead).Contains(target)) return;
            lead.Escorting = target;
            lead.Route.Clear();
            lead.Mode = FlightMode.Formation;
            lead.Adopted = false;
            Plugin.Log.LogInfo("[escort] " + (lead.Wing ?? lead.Name) + " escorting " + (target.Wing ?? target.Name));
        }

        internal static void StopEscort(Flight flight)
        {
            Flight lead = LeadOf(flight);
            if (lead?.Escorting == null) return;
            lead.Escorting = null;
            if (lead.Mode == FlightMode.Formation && lead.Aircraft != null)
            {
                lead.OrbitCentre = lead.Aircraft.GlobalPosition();
                lead.Mode = FlightMode.Orbit;
            }
        }

        // Anything that says the group is fighting: a threat to any member, an
        // attack or escape under way, or a lock on it moments ago.
        private static bool InCombat(List<Flight> group)
        {
            foreach (Flight member in group)
            {
                if (member.Threat != FlightThreat.None || member.Interrupted) return true;
                if (member.Mode == FlightMode.Strike || member.Mode == FlightMode.Engage || member.Mode == FlightMode.Egress) return true;
                if (member.Aircraft != null && EscortDefence.RecentlyLocked(member.Aircraft)) return true;
            }
            return false;
        }

        // Height for a slot: the lead's actual height above the ground, not
        // the height it was ordered to. The lead climbs gently toward a task
        // area tens of kilometres off; a wingman told to go to the ordered
        // height itself, from an aim point a few kilometres ahead, climbed
        // steeply, bled its speed and fell behind. An escort holds above its
        // charge's actual height.
        internal static float SlotAltitude(Flight flight)
        {
            if (IsWingman(flight))
            {
                Flight lead = LeadOf(flight);
                return lead.Aircraft != null && !lead.Aircraft.disabled ? lead.Aircraft.radarAlt : lead.Altitude;
            }
            Flight escorted = EscortedLead(flight);
            if (escorted == null) return flight.Altitude;
            float above = Mathf.Lerp(300f, 600f, SpreadOf(flight));
            return (escorted.Aircraft != null && !escorted.Aircraft.disabled ? escorted.Aircraft.radarAlt : escorted.Altitude) + above;
        }

        // Where a wingman's slot is right now, and which way the lead is going.
        internal static bool Slot(Flight flight, out GlobalPosition slot, out Vector3 forward, out Vector3 velocity)
        {
            slot = default; forward = Vector3.forward; velocity = Vector3.zero;
            bool rotary = flight.Aircraft != null && !(flight.Aircraft.autopilot is AutopilotPlane);
            Flight lead = LeadOf(flight);
            Vector3 offset;
            if (lead != flight) offset = SlotOffset(flight, rotary);
            else
            {
                lead = EscortedLead(flight);
                if (lead == null) return false;
                offset = EscortOffset(flight, rotary);
            }
            Aircraft leader = lead?.Aircraft;
            if (leader == null || leader.disabled || flight.Aircraft == null) return false;
            velocity = leader.rb != null ? leader.rb.velocity : leader.transform.forward * 100f;
            forward = new Vector3(velocity.x, 0f, velocity.z);
            if (forward.sqrMagnitude < 25f) forward = new Vector3(leader.transform.forward.x, 0f, leader.transform.forward.z);
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
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

        // Furthest any wingman is from its slot, straight-line: what the lead
        // waits on while the wing forms up.
        private static float WorstOffSlot(Flight lead)
        {
            float worst = 0f;
            foreach (Flight member in Members(lead.Wing))
            {
                if (member == lead || member.Mode != FlightMode.Formation) continue;
                if (!Slot(member, out GlobalPosition slot, out _, out _)) continue;
                worst = Mathf.Max(worst, FastMath.Distance(slot, member.Aircraft.GlobalPosition()));
            }
            return worst;
        }

        // Whether the lead should hold where it is for its wing: while members
        // are still to launch, or once a wingman is more than 6 km off its
        // slot, until everyone is within 1.5 km -- never in a fight, and for
        // five minutes at most. The lead circles the spot it was at when the
        // hold began.
        internal static bool JoinUp(Flight lead, out GlobalPosition point)
        {
            point = default;
            if (!IsLead(lead) || !wings.TryGetValue(lead.Wing, out Record record)) return false;
            float now = Time.timeSinceLevelLoad;
            int waiting = LaunchQueue.QueuedInWing(lead.Wing) + FlightOrders.PendingInWing(lead.Wing);
            float worst = WorstOffSlot(lead);
            bool fighting = InCombat(Members(lead.Wing));
            bool need = !fighting && (waiting > 0 || worst > (record.JoiningUp ? 1500f : 6000f));
            if (need && !record.JoiningUp && now >= record.NextJoinAllowed)
            {
                record.JoiningUp = true;
                record.JoinPoint = lead.Aircraft.GlobalPosition();
                record.JoinStarted = now;
                Diag.Flight("[wing] " + lead.Wing + " · holding for the wing to join · " +
                    (waiting > 0 ? waiting + " still to launch" : "furthest " + UnitConverter.DistanceReading(worst) + " off"));
            }
            else if (record.JoiningUp && (!need || now - record.JoinStarted > 300f))
            {
                record.JoiningUp = false;
                record.NextJoinAllowed = now + 60f;
                Diag.Flight("[wing] " + lead.Wing + (need ? " · join-up timed out, proceeding" : " · joined, proceeding"));
                if (!need) CommandState.Say(lead.Wing + " · formed up, proceeding");
            }
            point = record.JoinPoint;
            return record.JoiningUp;
        }

        // The slowest wingman that is behind its slot, and its speed: a lead
        // outrunning it by more than a few m/s leaves it no way to close.
        internal static bool SlowestBehind(Flight lead, out float speed)
        {
            speed = float.MaxValue;
            if (!IsLead(lead)) return false;
            foreach (Flight member in Members(lead.Wing))
            {
                if (member == lead || member.Mode != FlightMode.Formation || member.Aircraft == null) continue;
                if (!Slot(member, out GlobalPosition slot, out Vector3 forward, out _)) continue;
                if (Vector3.Dot(slot - member.Aircraft.GlobalPosition(), forward) < 150f) continue;
                speed = Mathf.Min(speed, member.Aircraft.speed);
            }
            return speed < float.MaxValue;
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

                // Close up in transit, open out in a fight -- blended over
                // several seconds, and held open a while after it goes quiet.
                var fight = new List<Flight>(members);
                Flight escorted = EscortedLead(members[0]);
                if (escorted != null) fight.AddRange(Group(escorted));
                if (InCombat(fight)) record.LastCombat = Time.timeSinceLevelLoad;
                float want = Time.timeSinceLevelLoad - record.LastCombat < 20f ? 1f : 0f;
                record.Spread = Mathf.MoveTowards(record.Spread, want, Time.deltaTime * 0.15f);

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
                next.Escorting = previous.Escorting;
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
            if (lead != null) lead.Escorting = null;        // sent somewhere: no longer escorting
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

        private const float AirdropSpacing = 200f;       // drop points abreast, across the run
        private const float LandingSpacing = 150f;       // rotor to rotor, with room to spare
        private const float LandingSearch = 60f;         // each looks for ground only round its own slot

        // Every aircraft in the wing that has cargo makes its own drop. An
        // airdrop is flown abreast: each drop point beside the last, across
        // the approach, so they cross the zone side by side. A landing gives
        // each its own touchdown spot, the lead's in the middle and the rest
        // in a ring round it. Those with nothing to deliver keep formation,
        // or hold over the zone if it is the lead that has nothing.
        public static void Deliver(Flight flight, GlobalPosition where, bool airdrop)
        {
            Flight lead = Led(flight);
            if (lead == null) return;
            var carriers = new List<Flight>();
            foreach (Flight member in Wings.Group(lead))
                if (FlightOrders.CanDeliver(member.Aircraft) && FlightOrders.CargoAboard(member.Aircraft) > 0)
                    carriers.Add(member);
            if (carriers.Count <= 1 && (carriers.Count == 0 || carriers[0] == lead))
            {
                FlightOrders.Deliver(lead, where, airdrop);
                return;
            }
            carriers.Remove(lead);
            if (FlightOrders.CanDeliver(lead.Aircraft) && FlightOrders.CargoAboard(lead.Aircraft) > 0) carriers.Insert(0, lead);
            else FlightOrders.SetArea(lead, where, 2000f);

            Vector3 approach = where - lead.Aircraft.GlobalPosition();
            approach.y = 0f;
            approach = approach.sqrMagnitude > 1f ? approach.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, approach);
            for (int i = 0; i < carriers.Count; i++)
            {
                Vector3 offset;
                if (airdrop)
                {
                    int step = (i + 1) / 2 * (i % 2 == 1 ? 1 : -1);     // 0, +1, -1, +2, -2 ...
                    offset = side * step * AirdropSpacing;
                }
                else if (i == 0) offset = Vector3.zero;
                else
                {
                    int ring = (i - 1) / 6 + 1;
                    float angle = ((i - 1) % 6) * 60f + (ring % 2 == 0 ? 30f : 0f);
                    offset = Quaternion.AngleAxis(angle, Vector3.up) * approach * (LandingSpacing * ring);
                }
                FlightOrders.Deliver(carriers[i], where + offset, airdrop);
                if (!airdrop) carriers[i].CargoSearch = LandingSearch;
            }
            CommandState.Say((lead.Wing ?? lead.Name) + " · " + carriers.Count + " aircraft " + (airdrop ? "airdropping abreast" : "landing, each to its own spot"));
        }

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

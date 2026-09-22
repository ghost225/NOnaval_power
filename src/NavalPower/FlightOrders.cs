using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace NavalPower
{
    public enum FlightMode { Route, Orbit, Station, Strike, Engage, ReturnToBase }

    public enum FlightRoe
    {
        Hold,      // never fight; evasion only
        Tight,     // fight back at whatever has shot at us
        Free       // engage hostiles in reach, then resume the task
    }

    public enum FlightThreat { None, Missile, Hostile }

    // A flight this ship launched and still commands. Aircraft are meant to
    // feel owned, like a deployed vehicle: they hold what they are given and do
    // not go hunting unless told.
    public sealed class Flight
    {
        public Aircraft Aircraft;
        public Ship Parent;
        public FlightMode Mode = FlightMode.Orbit;
        public readonly List<GlobalPosition> Route = new List<GlobalPosition>();
        public GlobalPosition OrbitCentre;      // task area centre
        public float OrbitRadius = 3000f;       // task area radius, overwritten on adoption
        public bool ConfineToArea = true;       // fight only inside the area
        public float Altitude = 900f;
        public Vector3 StationOffset;
        public bool Adopted;
        public Unit Target;                 // designated for a strike
        public FlightMode PreviousMode = FlightMode.Orbit;
        public FlightRoe Roe = FlightRoe.Tight;
        public FlightThreat Threat;
        public bool Interrupted;            // native pilot has it while it fights or evades
        public float ThreatClearedAt;

        public string Name => Aircraft != null ? (Aircraft.definition?.unitName ?? Aircraft.name) : "lost";

        // 0-100. The constraint that actually governs carrier operations, and
        // until now it was invisible until the automatic recovery fired.
        public float FuelPercent => Aircraft != null ? Mathf.Clamp01(Aircraft.GetFuelLevel()) * 100f : 0f;

        public string ShortName
        {
            get
            {
                string full = Name;
                int space = full.IndexOf(' ');
                return space > 0 ? full.Substring(0, space) : full;
            }
        }

        // Whatever the standing task is, what it is doing right now comes first.
        public string Status =>
            Threat == FlightThreat.Missile ? "EVADING"
            : Interrupted ? "ENGAGING"
            : null;

        public string Describe()
        {
            string now = Status;
            if (now != null) return now + " · " + Task();
            return Task();
        }

        private string Task()
        {
            switch (Mode)
            {
                case FlightMode.Route: return Route.Count > 0 ? "Route · " + Route.Count + " leg(s)" : "Route complete";
                case FlightMode.Orbit: return "Station area · " + UnitConverter.DistanceReading(OrbitRadius) +
                    (ConfineToArea ? "" : " · unrestricted");
                case FlightMode.Station: return "Station on " + (Parent?.definition?.unitName ?? "ship");
                case FlightMode.Strike: return Target != null && !Target.disabled
                    ? "Strike · " + (Target.definition?.unitName ?? Target.name) : "Strike · target gone";
                case FlightMode.Engage: return "Weapons free · AI engaging";
                default: return "Returning to base";
            }
        }
    }

    public static class FlightOrders
    {
        private static readonly List<Flight> flights = new List<Flight>();

        // A launch is a request; the aircraft appears some time later. Watch for
        // it rather than guessing, and give up if it never arrives.
        private sealed class Pending
        {
            internal Ship Ship;
            internal AircraftDefinition Definition;
            internal NuclearOption.SavedMission.Loadout Loadout;   // identity of this launch
            internal float ExpiresAt;
        }
        private static readonly List<Pending> pending = new List<Pending>();

        internal static void ExpectLaunch(Ship ship, AircraftDefinition definition,
            NuclearOption.SavedMission.Loadout loadout)
        {
            pending.Add(new Pending
            {
                Ship = ship,
                Definition = definition,
                Loadout = loadout,
                ExpiresAt = Time.unscaledTime + 90f
            });
        }

        // Claimed the moment the hangar builds it, matched on the loadout we
        // handed in, so a simultaneous AI launch of the same type cannot be
        // mistaken for ours.
        internal static Flight ClaimLaunch(NuclearOption.SavedMission.Loadout loadout, Aircraft aircraft)
        {
            if (loadout == null || aircraft == null) return null;
            for (int i = 0; i < pending.Count; i++)
            {
                if (!ReferenceEquals(pending[i].Loadout, loadout)) continue;
                Ship parent = pending[i].Ship;
                pending.RemoveAt(i);
                if (parent == null) return null;
                var flight = new Flight
                {
                    Aircraft = aircraft,
                    Parent = parent,
                    Mode = FlightMode.Orbit,
                    OrbitCentre = parent.GlobalPosition(),
                    Altitude = Settings.DefaultAltitude.Value,
                    OrbitRadius = Settings.DefaultAreaRadius.Value
                };
                flights.Add(flight);
                return flight;
            }
            return null;
        }

        // Launches requested but not yet seen on deck.
        internal static List<string> PendingNames(Ship ship)
        {
            var names = new List<string>();
            foreach (Pending request in pending)
                if (request.Ship == ship && request.Definition != null) names.Add(request.Definition.unitName);
            return names;
        }

        public static List<Flight> For(Ship ship)
        {
            var result = new List<Flight>();
            foreach (Flight flight in flights)
                if (flight.Parent == ship && flight.Aircraft != null && !flight.Aircraft.disabled) result.Add(flight);
            return result;
        }

        public static Flight Of(Aircraft aircraft)
        {
            foreach (Flight flight in flights) if (flight.Aircraft == aircraft) return flight;
            return null;
        }

        internal static void Tick()
        {
            AssessThreats();
            for (int i = flights.Count - 1; i >= 0; i--)
                if (flights[i].Aircraft == null || flights[i].Aircraft.disabled) flights.RemoveAt(i);

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending request = pending[i];
                if (request.Ship == null || Time.unscaledTime > request.ExpiresAt) { pending.RemoveAt(i); continue; }
                // The spawn hook normally claims the aircraft outright; this
                // only covers a build where that hook failed to bind.
                Aircraft found = FindNew(request);
                if (found == null) continue;
                Plugin.Log.LogWarning("[deck] launch matched by proximity, not by loadout · " +
                    (request.Definition?.unitName ?? "aircraft"));
                pending.RemoveAt(i);
                var flight = new Flight
                {
                    Aircraft = found,
                    Parent = request.Ship,
                    Mode = FlightMode.Orbit,
                    OrbitCentre = request.Ship.GlobalPosition(),
                    Altitude = Settings.DefaultAltitude.Value,
                    OrbitRadius = Settings.DefaultAreaRadius.Value
                };
                flights.Add(flight);
                Plugin.Log.LogInfo("[flight] adopted " + flight.Name + " from " + (request.Ship.definition?.unitName ?? "ship"));
            }

            // Install our state once the aircraft is actually flying: taking it
            // over during taxi or takeoff would fight the native sequence.
            foreach (Flight flight in flights)
            {
                if (flight.Mode == FlightMode.Strike && (flight.Target == null || flight.Target.disabled))
                {
                    Plugin.Log.LogInfo("[flight] " + flight.Name + " · target destroyed, breaking off");
                    BreakOff(flight);
                }
                Pilot crew = FirstPilot(flight.Aircraft);
                if (crew != null && !crew.playerControlled && flight.Adopted)
                {
                    bool yield = ShouldYield(flight);
                    if (yield && !flight.Interrupted && crew.currentState is NavalPilotState)
                    {
                        // Hand it over: the native pilot evades and fights far
                        // better than a navigation loop ever will.
                        flight.Interrupted = true;
                        PilotBaseState combat = CombatStateFor(crew);
                        if (combat != null) crew.SwitchStateNew(combat);
                        Plugin.Log.LogInfo("[flight] " + flight.Name + " · " +
                            (flight.Threat == FlightThreat.Missile ? "evading" : "engaging"));
                    }
                    else if (!yield && flight.Interrupted &&
                             Time.unscaledTime - flight.ThreatClearedAt > Settings.ThreatSettleSeconds.Value)
                    {
                        // Settle before taking it back, or it yo-yos between
                        // states every time a threat flickers in and out.
                        Plugin.Log.LogInfo("[flight] " + flight.Name + " · clear, resuming task");
                        Reclaim(flight);
                    }
                }

                if (flight.Adopted || flight.Aircraft == null) continue;
                Pilot pilot = FirstPilot(flight.Aircraft);
                if (pilot == null || pilot.playerControlled) continue;
                // A strike or weapons-free order wants the native combat pilot,
                // and can be handed over from our own state as well as from
                // the native one -- gating it on the native state meant the
                // handoff never happened once we already had the aircraft.
                if (flight.Mode == FlightMode.Strike || flight.Mode == FlightMode.Engage)
                {
                    PilotBaseState combat = CombatStateFor(pilot);
                    if (combat != null && !ReferenceEquals(pilot.currentState, combat))
                        pilot.SwitchStateNew(combat);
                    flight.Adopted = true;
                    Plugin.Log.LogInfo("[flight] " + flight.Name + " · " +
                        (flight.Mode == FlightMode.Strike
                            ? "striking " + (flight.Target?.definition?.unitName ?? "target")
                            : "weapons free"));
                    continue;
                }

                // Wait until it is actually flying, but do not name the state it
                // must be in: a helicopter goes to AIHeloCombatState and never
                // to AIPilotCombatModes, so testing for the latter meant rotary
                // flights were never taken under command at all.
                if (StillLeaving(pilot)) continue;
                if (!NavalPilotState.CanBeFlown(flight.Aircraft))
                {
                    // Better the native AI than an aircraft nobody is flying.
                    Plugin.Log.LogWarning("[flight] " + flight.Name +
                        " has no usable autopilot; leaving it to the native AI");
                    flight.Mode = FlightMode.Engage;
                    flight.Adopted = true;
                    continue;
                }
                NavalPilotState.Install(pilot, flight);
                flight.Adopted = true;
            }
        }

        // Aircraft carry a pilots array rather than a single accessor; the
        // first live one flies the thing.
        internal static Pilot FirstPilot(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.pilots == null) return null;
            foreach (Pilot pilot in aircraft.pilots)
                if (pilot != null && !pilot.dead && !pilot.ejected) return pilot;
            return null;
        }

        // On the deck, taxiing, or climbing out: taking over now would fight
        // the native launch sequence.
        internal static bool StillLeaving(Pilot pilot) =>
            pilot.currentState is PilotParkedState ||
            pilot.currentState is AIPilotTaxiState ||
            pilot.currentState is AIPilotTakeoffState ||
            pilot.currentState is AIHeloTakeoffState;

        internal static bool IsRotary(Pilot pilot) => pilot != null && pilot.AIHeloCombatState != null;

        // The combat state that suits this airframe.
        internal static PilotBaseState CombatStateFor(Pilot pilot) =>
            IsRotary(pilot) && pilot.AIHeloCombatState != null
                ? (PilotBaseState)pilot.AIHeloCombatState : pilot.AICombatState;

        private static Aircraft FindNew(Pending request)
        {
            FactionHQ hq = request.Ship.NetworkHQ;
            if (hq == null) return null;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Aircraft aircraft) || aircraft.disabled) continue;
                if (aircraft.NetworkHQ != hq) continue;
                if (request.Definition != null && aircraft.definition != request.Definition) continue;
                if (Of(aircraft) != null) continue;
                Pilot crew = FirstPilot(aircraft);
                if (crew == null || crew.playerControlled) continue;
                // Close aboard: it came off this deck rather than an airfield.
                if (FastMath.Distance(aircraft.GlobalPosition(), request.Ship.GlobalPosition()) > 1200f) continue;
                return aircraft;
            }
            return null;
        }

        // ---- orders --------------------------------------------------------

        public static void SetRoute(Flight flight, GlobalPosition point, bool append)
        {
            if (flight == null) return;
            if (!append) flight.Route.Clear();
            flight.Route.Add(point);
            flight.Mode = FlightMode.Route;
        }

        public static void Orbit(Flight flight, GlobalPosition centre)
        {
            if (flight == null) return;
            flight.OrbitCentre = centre;
            flight.Route.Clear();
            flight.Mode = FlightMode.Orbit;
        }

        // A task area is where the flight works: it holds inside it and, unless
        // released, will not prosecute anything outside it. That is the
        // difference between a patrol and an aircraft that wanders off after the
        // first contact it sees.
        public static void SetArea(Flight flight, GlobalPosition centre, float radius)
        {
            if (flight == null) return;
            flight.OrbitCentre = centre;
            flight.OrbitRadius = Mathf.Clamp(radius, 500f, 60000f);
            flight.Route.Clear();
            flight.Mode = FlightMode.Orbit;
        }

        public static void SetConfined(Flight flight, bool confined)
        {
            if (flight != null) flight.ConfineToArea = confined;
        }

        // Does this flight have an area that limits where it may fight?
        internal static bool HasArea(Flight flight) =>
            flight != null && flight.ConfineToArea &&
            (flight.Mode == FlightMode.Orbit || flight.Mode == FlightMode.Station);

        internal static GlobalPosition AreaCentre(Flight flight) =>
            flight.Mode == FlightMode.Station && flight.Parent != null
                ? flight.Parent.GlobalPosition() : flight.OrbitCentre;

        public static void Station(Flight flight)
        {
            if (flight == null || flight.Parent == null) return;
            flight.Route.Clear();
            // Abeam and slightly ahead: clear of the ship, still close aboard.
            flight.StationOffset = new Vector3(2200f, 0f, 1200f);
            flight.Mode = FlightMode.Station;
        }

        // Designating a target hands the flight to the native combat pilot,
        // which knows how to run an attack, while a patch pins its target to
        // ours. When the target dies the flight comes back under command rather
        // than wandering off hunting.
        public static void Strike(Flight flight, Unit target)
        {
            if (flight == null || target == null) return;
            if (flight.Mode != FlightMode.Strike) flight.PreviousMode = flight.Mode;
            flight.Target = target;
            flight.Route.Clear();
            flight.Mode = FlightMode.Strike;
            flight.Adopted = false;                 // let Tick hand it to the combat state
        }

        public static void BreakOff(Flight flight)
        {
            if (flight == null) return;
            flight.Target = null;
            flight.Mode = flight.PreviousMode == FlightMode.Strike ? FlightMode.Orbit : flight.PreviousMode;
            if (flight.Mode == FlightMode.Orbit && flight.Aircraft != null)
                flight.OrbitCentre = flight.Aircraft.GlobalPosition();
            flight.Adopted = false;                 // reclaim on the next tick
        }

        // Every flight that could usefully be sent at this contact.
        public static List<Flight> CapableOf(Ship ship, Unit target)
        {
            var result = new List<Flight>();
            if (target == null) return result;
            foreach (Flight flight in For(ship))
            {
                if (flight.Aircraft == null) continue;
                if (BestStationFor(flight.Aircraft, target) != null) result.Add(flight);
            }
            return result;
        }

        // The fitted station best suited to this target, falling back to a gun.
        // A gun will hurt almost anything given the chance, so "no dedicated
        // weapon for this" should not mean "cannot attack at all" -- but a
        // flight with nothing at all still has to be told no rather than sent.
        internal static WeaponStation BestStationFor(Aircraft aircraft, Unit target)
        {
            if (aircraft == null || target == null || aircraft.weaponStations == null) return null;
            WeaponStation best = null, gun = null;
            float bestScore = 0.01f;
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || station.WeaponInfo == null || station.Ammo <= 0) continue;
                if (station.WeaponInfo.gun && gun == null) gun = station;
                float score = WeaponOrders.Opportunity(station.WeaponInfo, target);
                if (score <= bestScore) continue;
                bestScore = score;
                best = station;
            }
            return best ?? gun;
        }

        public static void Engage(Flight flight)
        {
            if (flight == null) return;
            flight.Mode = FlightMode.Engage;
        }

        public static void SetRoe(Flight flight, FlightRoe roe)
        {
            if (flight == null) return;
            flight.Roe = roe;
            // Tightening while the native pilot has it takes control straight back.
            if (roe == FlightRoe.Hold && flight.Interrupted && flight.Threat != FlightThreat.Missile)
                Reclaim(flight);
        }

        public static string Describe(FlightRoe roe) =>
            roe == FlightRoe.Hold ? "Weapons Hold"
            : roe == FlightRoe.Tight ? "Weapons Tight" : "Weapons Free";

        private static void Reclaim(Flight flight)
        {
            flight.Interrupted = false;
            flight.Adopted = false;          // Tick reinstalls our state
        }

        // ---- threats -------------------------------------------------------

        private static float nextThreatScan;

        private static void AssessThreats()
        {
            if (Time.unscaledTime < nextThreatScan) return;
            nextThreatScan = Time.unscaledTime + 0.25f;

            foreach (Flight flight in flights)
            {
                Aircraft aircraft = flight.Aircraft;
                if (aircraft == null || aircraft.disabled) continue;
                FlightThreat threat = FlightThreat.None;

                foreach (Unit unit in UnitRegistry.allUnits)
                {
                    if (unit == null || unit.disabled || unit.NetworkHQ == null) continue;
                    if (unit.NetworkHQ == aircraft.NetworkHQ) continue;

                    // Anything already in the air at us outranks every order.
                    if (unit is Missile missile)
                    {
                        if (missile.targetID == aircraft.persistentID) { threat = FlightThreat.Missile; break; }
                        continue;
                    }
                    if (threat != FlightThreat.None) continue;
                    if (flight.Roe != FlightRoe.Free) continue;
                    // Weapons free inside the task area: something we can reach
                    // and hurt, that is also somewhere we were sent to fight.
                    if (HasArea(flight) &&
                        FastMath.Distance(unit.GlobalPosition(), AreaCentre(flight)) > flight.OrbitRadius) continue;
                    WeaponStation station = BestStationFor(aircraft, unit);
                    if (station == null) continue;
                    float range = FastMath.Distance(aircraft.GlobalPosition(), unit.GlobalPosition());
                    if (range <= station.WeaponInfo.targetRequirements.maxRange) threat = FlightThreat.Hostile;
                }

                // Weapons tight fights back at whoever actually shot at us, which
                // is exactly the missile case above.
                if (threat == FlightThreat.None && flight.Threat != FlightThreat.None)
                    flight.ThreatClearedAt = Time.unscaledTime;
                flight.Threat = threat;
            }
        }

        // Does the flight's ROE let the native pilot take it right now?
        private static bool ShouldYield(Flight flight)
        {
            if (flight.Mode == FlightMode.Strike || flight.Mode == FlightMode.Engage) return true;
            // Evasion is never a choice: being shot at overrides Weapons Hold.
            if (flight.Threat == FlightThreat.Missile) return true;
            if (flight.Roe == FlightRoe.Hold) return false;
            return flight.Threat == FlightThreat.Hostile;
        }

        public static void ReturnToBase(Flight flight)
        {
            if (flight == null) return;
            flight.Mode = FlightMode.ReturnToBase;
        }

        public static void SetAltitude(Flight flight, float metres)
        {
            if (flight != null) flight.Altitude = Mathf.Clamp(metres, 60f, 12000f);
        }

        public static void SetOrbitRadius(Flight flight, float metres)
        {
            if (flight != null) flight.OrbitRadius = Mathf.Clamp(metres, 500f, 30000f);
        }
    }
}

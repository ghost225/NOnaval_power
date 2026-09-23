using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace NavalPower
{
    public enum FlightMode { Route, Orbit, Station, Strike, Jam, Cargo, Egress, Engage, ReturnToBase }

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
        public int AmmoAtAttack = -1;       // total rounds when the run began
        public GlobalPosition CargoPoint;
        public bool Airdrop;
        public GlobalPosition EgressPoint;
        public float EgressUntil;
        public float NextEgressPlan;
        public FlightThreat Threat;
        public bool ThreatIsInfrared;       // flares matter, and it must be let in much closer
        public float ThreatRange = float.PositiveInfinity;
        public float NextFlare;
        public bool Interrupted;            // native pilot has it while it fights or evades
        public float ThreatClearedAt;

        public string Name => Aircraft != null ? (Aircraft.definition?.unitName ?? Aircraft.name) : "lost";

        // 0-100. The constraint that actually governs carrier operations, and
        // until now it was invisible until the automatic recovery fired.
        public float FuelPercent => Aircraft != null ? Mathf.Clamp01(Aircraft.GetFuelLevel()) * 100f : 0f;

        // What it still has to fight with. A flight that has shot itself dry is
        // just fuel and risk, and that should be visible without opening it.
        public int RoundsRemaining => FlightOrders.TotalAmmo(Aircraft);

        public string Stores
        {
            get
            {
                if (Aircraft == null || Aircraft.weaponStations == null) return "";
                var parts = new List<string>();
                foreach (WeaponStation station in Aircraft.weaponStations)
                {
                    if (station?.WeaponInfo == null || station.Ammo <= 0) continue;
                    parts.Add(station.WeaponInfo.shortName is string shortName && shortName.Length > 0
                        ? shortName + " " + station.Ammo
                        : station.WeaponInfo.weaponName + " " + station.Ammo);
                }
                return parts.Count == 0 ? "no stores" : string.Join(", ", parts.ToArray());
            }
        }

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
                case FlightMode.Egress: return "Egressing · weapons away";
                case FlightMode.Cargo: return (Airdrop ? "Airdrop" : "Delivery") + " · inbound to the zone";
                case FlightMode.Jam: return Target != null && !Target.disabled
                    ? "Jamming · " + (Target.definition?.unitName ?? Target.name) : "Jamming · target gone";
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
                if (flight.Mode == FlightMode.Jam && (flight.Target == null || flight.Target.disabled))
                {
                    Plugin.Log.LogInfo("[flight] " + flight.Name + " · jamming target gone");
                    BreakOff(flight);
                }

                if (flight.Mode == FlightMode.Strike && (flight.Target == null || flight.Target.disabled))
                {
                    Plugin.Log.LogInfo("[flight] " + flight.Name + " · target destroyed, breaking off");
                    BreakOff(flight);
                }

                // A shot has left the aircraft: stop pressing.
                if (flight.Mode == FlightMode.Strike && flight.AmmoAtAttack >= 0)
                {
                    int now = TotalAmmo(flight.Aircraft);
                    if (now >= 0 && now < flight.AmmoAtAttack) Egress(flight);
                }

                if (flight.Mode == FlightMode.Egress)
                {
                    // The threat picture moves; so should the escape route.
                    if (Time.timeSinceLevelLoad >= flight.NextEgressPlan)
                    {
                        flight.NextEgressPlan = Time.timeSinceLevelLoad + 2f;
                        PlanEgress(flight);
                    }
                    bool clear = flight.Target == null || flight.Target.disabled ||
                        FastMath.Distance(flight.Aircraft.GlobalPosition(), flight.Target.GlobalPosition())
                            >= Settings.StandoffMetres.Value;
                    if (clear || Time.timeSinceLevelLoad >= flight.EgressUntil)
                    {
                        // Out of danger. Press again only with something left to
                        // press with, and only if the target is still there.
                        bool rearmed = flight.Target != null && !flight.Target.disabled &&
                            BestStationFor(flight.Aircraft, flight.Target) != null &&
                            Settings.ReattackAfterEgress.Value;
                        if (rearmed)
                        {
                            Plugin.Log.LogInfo("[flight] " + flight.Name + " · re-attacking");
                            Strike(flight, flight.Target);
                        }
                        else
                        {
                            Plugin.Log.LogInfo("[flight] " + flight.Name + " · clear of the target, resuming");
                            BreakOff(flight);
                        }
                    }
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
                if (flight.Mode == FlightMode.Cargo)
                {
                    if (pilot.AIHeloTransportState != null &&
                        !(pilot.currentState is AIHeloTransportState))
                        pilot.SwitchStateNew(pilot.AIHeloTransportState);
                    flight.Adopted = true;
                    Plugin.Log.LogInfo("[flight] " + flight.Name + " · " +
                        (flight.Airdrop ? "airdropping" : "delivering") + " cargo");
                    continue;
                }

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
            if (flight.Mode != FlightMode.Strike && flight.Mode != FlightMode.Egress) flight.PreviousMode = flight.Mode;
            flight.Target = target;
            flight.AmmoAtAttack = TotalAmmo(flight.Aircraft);
            flight.Route.Clear();
            flight.Mode = FlightMode.Strike;
            flight.Adopted = false;                 // let Tick hand it to the combat state
        }

        internal static int TotalAmmo(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.weaponStations == null) return -1;
            int total = 0;
            foreach (WeaponStation station in aircraft.weaponStations)
                if (station != null) total += Mathf.Max(0, station.Ammo);
            return total;
        }

        // Weapons away: get out rather than keep closing.
        //
        // The native pilot presses an attack for as long as it holds the target,
        // and pinning that target on every search means it never re-evaluates
        // and never disengages -- so it flies down the throat of whatever is
        // defending and dies there. Take the aircraft back once a shot is off
        // and fly it out to standoff before deciding what to do next.
        private static void Egress(Flight flight)
        {
            flight.Mode = FlightMode.Egress;
            flight.EgressUntil = Time.timeSinceLevelLoad + Settings.EgressSeconds.Value;
            PlanEgress(flight);
            flight.Adopted = false;                 // take it back off the native pilot
            flight.Interrupted = false;
            Plugin.Log.LogInfo("[flight] " + flight.Name + " · weapons away, egressing");
        }

        // Away from the threat, never through it.
        //
        // Steering toward home is wrong whenever home lies beyond the target:
        // it takes the aircraft directly over what it just attacked, and over
        // whatever is defending it. Push away from every hostile close enough to
        // matter, weighted by how close it is, and only lean toward home when
        // that does not turn the aircraft back into them.
        private static void PlanEgress(Flight flight)
        {
            Aircraft aircraft = flight.Aircraft;
            if (aircraft == null) return;
            GlobalPosition here = aircraft.GlobalPosition();
            float reach = Settings.StandoffMetres.Value * 2f;

            Vector3 away = Vector3.zero;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.disabled || unit is Missile) continue;
                if (unit.NetworkHQ == null || unit.NetworkHQ == aircraft.NetworkHQ) continue;
                Vector3 from = here - unit.GlobalPosition();
                from.y = 0f;
                float distance = from.magnitude;
                if (distance < 1f || distance > reach) continue;
                // Nearer things push harder.
                away += from / distance * (1f - distance / reach);
            }

            // Nothing close enough to weigh: just leave the target behind.
            if (away.sqrMagnitude < 0.0001f && flight.Target != null)
            {
                away = here - flight.Target.GlobalPosition();
                away.y = 0f;
            }
            if (away.sqrMagnitude < 0.0001f) away = aircraft.transform.forward;
            away.y = 0f;
            away.Normalize();

            GlobalPosition home = flight.Parent != null ? flight.Parent.GlobalPosition() : flight.OrbitCentre;
            Vector3 toHome = home - here;
            toHome.y = 0f;
            if (toHome.sqrMagnitude > 1f)
            {
                toHome.Normalize();
                // Only lean homeward when home is not back through the threat.
                if (Vector3.Dot(toHome, away) > 0.1f)
                    away = (away * 0.6f + toHome * 0.4f).normalized;
            }

            flight.EgressPoint = here + away * Settings.StandoffMetres.Value;
        }

        // A jamming pod is a weapon, so the aircraft carrying one can be sent
        // to suppress a specific emitter. Unlike a strike this never closes:
        // the flight holds at standoff and keeps the pod on the target, which
        // is the whole point of sending it rather than something with bombs.
        public static void Jam(Flight flight, Unit target)
        {
            if (flight == null || target == null) return;
            if (flight.Mode != FlightMode.Jam) flight.PreviousMode = flight.Mode;
            flight.Target = target;
            flight.Route.Clear();
            flight.Mode = FlightMode.Jam;
            flight.Adopted = false;                 // ours to fly, not the combat pilot's
        }

        internal static WeaponStation JammerOn(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.weaponStations == null) return null;
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || station.Weapons == null) continue;
                foreach (Weapon weapon in station.Weapons)
                    if (weapon is JammingPod) return station;
            }
            return null;
        }

        public static List<Flight> JammersFor(Ship ship)
        {
            var result = new List<Flight>();
            foreach (Flight flight in For(ship))
                if (JammerOn(flight.Aircraft) != null) result.Add(flight);
            return result;
        }

        // The native transport state flies the delivery; we only tell it where.
        public static void Deliver(Flight flight, GlobalPosition where, bool airdrop)
        {
            if (flight == null) return;
            if (flight.Mode != FlightMode.Cargo) flight.PreviousMode = flight.Mode;
            flight.CargoPoint = where;
            flight.Airdrop = airdrop;
            flight.Route.Clear();
            flight.Mode = FlightMode.Cargo;
            flight.Adopted = false;
        }

        public static List<Flight> CarriersFor(Ship ship)
        {
            var result = new List<Flight>();
            foreach (Flight flight in For(ship))
                if (CargoMissions.CanCarry(flight.Aircraft)) result.Add(flight);
            return result;
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

                float nearestShot = float.PositiveInfinity;
                bool infrared = false;

                foreach (Unit unit in UnitRegistry.allUnits)
                {
                    if (unit == null || unit.disabled || unit.NetworkHQ == null) continue;
                    if (unit.NetworkHQ == aircraft.NetworkHQ) continue;

                    // Anything already in the air at us outranks every order.
                    if (unit is Missile missile)
                    {
                        if (missile.targetID != aircraft.persistentID) continue;
                        threat = FlightThreat.Missile;
                        float shotRange = FastMath.Distance(aircraft.GlobalPosition(), missile.GlobalPosition());
                        if (shotRange < nearestShot)
                        {
                            nearestShot = shotRange;
                            // Anything that is not clearly heat-seeking is
                            // treated as radar guided, which hands over to
                            // native evasion far earlier -- the safer mistake.
                            infrared = missile.GetComponent<IRSeeker>() != null;
                        }
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
                flight.ThreatIsInfrared = infrared;
                flight.ThreatRange = nearestShot;

                // Decoy on the way out. Once the native pilot has the aircraft
                // it runs its own countermeasures, so this only covers the
                // stretch we are flying ourselves.
                if (threat == FlightThreat.Missile && infrared && flight.Mode == FlightMode.Egress &&
                    Time.timeSinceLevelLoad >= flight.NextFlare &&
                    aircraft.countermeasureManager != null &&
                    aircraft.countermeasureManager.GetFlareAmmoProportion() > 0f)
                {
                    flight.NextFlare = Time.timeSinceLevelLoad + Settings.FlareInterval.Value;
                    aircraft.countermeasureManager.PopFlares();
                }
            }
        }

        // Does the flight's ROE let the native pilot take it right now?
        private static bool ShouldYield(Flight flight)
        {
            if (flight.Mode == FlightMode.Strike || flight.Mode == FlightMode.Engage) return true;
            if (flight.Mode == FlightMode.Cargo) return false;       // the transport state has it
            // Jamming holds station; only an actual shot takes it off the job.
            if (flight.Mode == FlightMode.Jam) return flight.Threat == FlightThreat.Missile;

            // Leaving outranks evading, up to a point. Turning to fight a shot
            // that is still thirty kilometres away just keeps the aircraft in
            // the threat envelope; running until it is genuinely close, then
            // handing to the native pilot, gets it out alive. Heat-seekers are
            // let in much closer than radar shots because flares work and the
            // endgame is short.
            if (flight.Mode == FlightMode.Egress)
            {
                if (flight.Threat != FlightThreat.Missile) return false;
                float handover = flight.ThreatIsInfrared
                    ? Settings.InfraredHandover.Value : Settings.RadarHandover.Value;
                return flight.ThreatRange <= handover;
            }

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

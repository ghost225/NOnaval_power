using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace NavalPower
{
    public enum FlightMode { Route, Orbit, Station, Engage, ReturnToBase }

    // A flight this ship launched and still commands. Aircraft are meant to
    // feel owned, like a deployed vehicle: they hold what they are given and do
    // not go hunting unless told.
    public sealed class Flight
    {
        public Aircraft Aircraft;
        public Ship Parent;
        public FlightMode Mode = FlightMode.Orbit;
        public readonly List<GlobalPosition> Route = new List<GlobalPosition>();
        public GlobalPosition OrbitCentre;
        public float OrbitRadius = 3000f;
        public float Altitude = 900f;
        public Vector3 StationOffset;
        public bool Adopted;

        public string Name => Aircraft != null ? (Aircraft.definition?.unitName ?? Aircraft.name) : "lost";

        public string Describe()
        {
            switch (Mode)
            {
                case FlightMode.Route: return Route.Count > 0 ? "Route · " + Route.Count + " leg(s)" : "Route complete";
                case FlightMode.Orbit: return "Orbit · " + UnitConverter.DistanceReading(OrbitRadius);
                case FlightMode.Station: return "Station on " + (Parent?.definition?.unitName ?? "ship");
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
            internal float ExpiresAt;
        }
        private static readonly List<Pending> pending = new List<Pending>();

        internal static void ExpectLaunch(Ship ship, AircraftDefinition definition)
        {
            pending.Add(new Pending { Ship = ship, Definition = definition, ExpiresAt = Time.unscaledTime + 60f });
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
            for (int i = flights.Count - 1; i >= 0; i--)
                if (flights[i].Aircraft == null || flights[i].Aircraft.disabled) flights.RemoveAt(i);

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending request = pending[i];
                if (request.Ship == null || Time.unscaledTime > request.ExpiresAt) { pending.RemoveAt(i); continue; }
                Aircraft found = FindNew(request);
                if (found == null) continue;
                pending.RemoveAt(i);
                var flight = new Flight
                {
                    Aircraft = found,
                    Parent = request.Ship,
                    Mode = FlightMode.Orbit,
                    OrbitCentre = request.Ship.GlobalPosition(),
                    Altitude = Mathf.Max(600f, found.radarAlt + 400f)
                };
                flights.Add(flight);
                Plugin.Log.LogInfo("[flight] adopted " + flight.Name + " from " + (request.Ship.definition?.unitName ?? "ship"));
            }

            // Install our state once the aircraft is actually flying: taking it
            // over during taxi or takeoff would fight the native sequence.
            foreach (Flight flight in flights)
            {
                if (flight.Adopted || flight.Aircraft == null) continue;
                Pilot pilot = FirstPilot(flight.Aircraft);
                if (pilot == null || pilot.playerControlled) continue;
                if (!(pilot.currentState is AIPilotCombatModes)) continue;   // still on the deck or climbing out
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

        public static void Station(Flight flight)
        {
            if (flight == null || flight.Parent == null) return;
            flight.Route.Clear();
            // Abeam and slightly ahead: clear of the ship, still close aboard.
            flight.StationOffset = new Vector3(2200f, 0f, 1200f);
            flight.Mode = FlightMode.Station;
        }

        public static void Engage(Flight flight)
        {
            if (flight == null) return;
            flight.Mode = FlightMode.Engage;
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

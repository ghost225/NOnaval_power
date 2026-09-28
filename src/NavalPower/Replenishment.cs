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

        // ---- supply by air ---------------------------------------------------
        //
        // A rearm request by itself is answered by whatever the faction has:
        // a ship in range, a transport helicopter the AI happens to task, or
        // -- as reported -- a ship from the far side of the map setting off
        // towards it. Instead, send one: the nearest friendly base that can
        // launch a helicopter carrying a naval supply container (an Ibis or a
        // Tarantula, or whatever mod airframe carries one) launches it as one
        // of our flights, the game's own naval-supply delivery flies it onto
        // this ship, and the ship asks for its rearm only when the helicopter
        // is close, so nothing else is drawn in. Paid for like any launch.

        internal sealed class SupplySource
        {
            internal Airbase Base;
            internal AircraftDefinition Type;
            internal int Station;
            internal WeaponMount Container;
            internal float Distance;
            internal bool InReserve;
        }

        private sealed class SupplyRun
        {
            internal Ship Ship;
            internal string Callsign;
            internal string From;
            internal float LaunchedAt;
            internal Flight Flight;
        }

        private static readonly List<SupplyRun> runs = new List<SupplyRun>();
        private const float RequestWithin = 6000f;      // ask for the rearm once the helicopter is this close

        internal static string InboundTo(Ship ship)
        {
            foreach (SupplyRun run in runs)
            {
                if (run.Ship != ship) continue;
                if (run.Flight?.Aircraft == null) return run.Callsign + " launching from " + run.From;
                return run.Callsign + " inbound · " +
                    UnitConverter.DistanceReading(FastMath.Distance(run.Flight.Aircraft.GlobalPosition(), ship.GlobalPosition()));
            }
            return null;
        }

        internal static Aircraft InboundAircraft(Ship ship)
        {
            foreach (SupplyRun run in runs) if (run.Ship == ship) return run.Flight?.Aircraft;
            return null;
        }

        // The station on this airframe that can hold a naval supply container.
        private static bool Carries(AircraftDefinition type, out int station, out WeaponMount container)
        {
            station = -1; container = null;
            var prefab = type?.unitPrefab != null ? type.unitPrefab.GetComponent<Aircraft>() : null;
            HardpointSet[] sets = prefab?.weaponManager?.hardpointSets;
            if (sets == null) return false;
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i]?.weaponOptions == null) continue;
                foreach (WeaponMount mount in sets[i].weaponOptions)
                    if (mount != null && mount.info != null && mount.info.rearmShip)
                    { station = i; container = mount; return true; }
            }
            return false;
        }

        internal static WeaponStation SupplyStation(Aircraft aircraft)
        {
            if (aircraft?.weaponStations == null) return null;
            foreach (WeaponStation station in aircraft.weaponStations)
                if (station?.WeaponInfo != null && station.WeaponInfo.rearmShip) return station;
            return null;
        }

        // Dropped: nothing left on the supply station.
        internal static bool Delivered(Flight flight)
        {
            WeaponStation station = SupplyStation(flight?.Aircraft);
            return station == null || station.Ammo <= 0;
        }

        // Nearest first; one already bought and waiting in the reserve wins a
        // near tie, since it costs nothing to fly.
        private static Ship cachedFor;
        private static SupplySource cached;
        private static string cachedReason;
        private static float cachedAt = -10f;

        // The window asks five times a second; the answer is good for one.
        internal static SupplySource BestSource(Ship ship, out string reason)
        {
            if (ship == cachedFor && Time.unscaledTime - cachedAt < 1f) { reason = cachedReason; return cached; }
            cached = FindSource(ship, out cachedReason);
            cachedFor = ship; cachedAt = Time.unscaledTime;
            reason = cachedReason;
            return cached;
        }

        private static SupplySource FindSource(Ship ship, out string reason)
        {
            reason = null;
            SupplySource best = null;
            FactionHQ hq = ship?.NetworkHQ;
            if (hq == null) { reason = "No faction."; return null; }
            Airbase ownDeck = null;
            foreach (Airbase airbase in hq.GetAirbases())
                if (airbase != null && Airfields.ShipOf(airbase) == ship) ownDeck = airbase;
            bool anyCarrier = false;
            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || airbase.disabled || airbase == ownDeck) continue;
                bool serviceable = false;
                foreach (Hangar hangar in airbase.hangars) if (Airfields.Serviceable(hangar)) { serviceable = true; break; }
                if (!serviceable) continue;
                float distance = FastMath.Distance(Airfields.PositionOf(airbase), ship.GlobalPosition());
                foreach (DeckAircraft airframe in CarrierOps.Available(airbase))
                {
                    if (!Carries(airframe.Definition, out int station, out WeaponMount container)) continue;
                    anyCarrier = true;
                    if (!airbase.CanSpawnAircraft(airframe.Definition)) continue;
                    float score = distance - (airframe.InReserve ? 5000f : 0f);
                    float bestScore = best == null ? float.MaxValue : best.Distance - (best.InReserve ? 5000f : 0f);
                    if (score >= bestScore) continue;
                    best = new SupplySource
                    {
                        Base = airbase, Type = airframe.Definition, Station = station, Container = container,
                        Distance = distance, InReserve = airframe.InReserve
                    };
                }
            }
            if (best == null)
                reason = anyCarrier ? "Every base with a supply helicopter is busy just now."
                    : "No friendly base can launch a helicopter that carries naval supplies.";
            return best;
        }

        internal static bool SendSupply(Ship ship, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            if (Status(ship).StationsShort == 0) { reason = "Magazines are already full."; return false; }
            string inbound = InboundTo(ship);
            if (inbound != null) { reason = "Supply already on its way · " + inbound; return false; }

            cachedAt = -10f;
            SupplySource source = BestSource(ship, out reason);
            if (source == null) return false;

            LoadoutPlan plan = CarrierOps.PlanFor(source.Type);
            plan.Count = 1;
            foreach (LoadoutStation station in plan.Stations)
                if (station.Index == source.Station) station.Selected = source.Container;
            string callsign = plan.Callsign ?? Callsigns.Suggest(source.Type);
            if (!CarrierOps.Launch(source.Base, plan, callsign, null, out reason)) return false;

            runs.Add(new SupplyRun
            {
                Ship = ship, Callsign = callsign, From = Airfields.NameOf(source.Base), LaunchedAt = Time.unscaledTime
            });
            reason = callsign + " · " + source.Type.unitName + " launching from " + Airfields.NameOf(source.Base) +
                " with supplies · " + UnitConverter.DistanceReading(source.Distance);
            Plugin.Log.LogInfo("[rearm] " + ShipNames.Of(ship) + ": " + reason);
            return true;
        }

        internal static void Tick()
        {
            for (int i = runs.Count - 1; i >= 0; i--)
            {
                SupplyRun run = runs[i];
                Flight flight = run.Flight;
                if (run.Ship == null || run.Ship.disabled)
                {
                    if (flight != null) { flight.SupplyShip = null; FlightOrders.ReturnToBase(flight); }
                    runs.RemoveAt(i);
                    continue;
                }

                if (flight == null)
                {
                    // Waiting for the hangar to build it and the flight to be
                    // taken on; then the delivery is ordered.
                    foreach (Flight candidate in FlightOrders.All())
                        if (candidate.Label == run.Callsign && candidate.Aircraft != null) { flight = candidate; break; }
                    if (flight == null)
                    {
                        if (Time.unscaledTime - run.LaunchedAt > 150f)
                        {
                            CommandState.Say(run.Callsign + " · the supply launch never got away");
                            runs.RemoveAt(i);
                        }
                        continue;
                    }
                    run.Flight = flight;
                    if (SupplyStation(flight.Aircraft) == null)
                    {
                        CommandState.Say(run.Callsign + " · launched without a supply container, returning");
                        FlightOrders.ReturnToBase(flight);
                        runs.RemoveAt(i);
                        continue;
                    }
                    flight.SupplyShip = run.Ship;
                    FlightOrders.Deliver(flight, run.Ship.GlobalPosition(), airdrop: true);
                    continue;
                }

                if (flight.Aircraft == null || flight.Aircraft.disabled)
                {
                    CommandState.Say(run.Callsign + " · supply helicopter lost");
                    runs.RemoveAt(i);
                    continue;
                }
                // Given other orders: it is no longer a supply run.
                if (flight.Mode != FlightMode.Cargo || flight.SupplyShip != run.Ship)
                {
                    if (flight.SupplyShip == run.Ship) flight.SupplyShip = null;
                    runs.RemoveAt(i);
                    continue;
                }
                flight.CargoPoint = run.Ship.GlobalPosition();     // the map marks the ship, not where it was
                if (Delivered(flight))
                {
                    CommandState.Say(run.Callsign + " · supplies delivered to " + ShipNames.Of(run.Ship) + ", returning");
                    Plugin.Log.LogInfo("[rearm] " + ShipNames.Of(run.Ship) + ": supplies delivered by " + run.Callsign);
                    flight.SupplyShip = null;
                    FlightOrders.ReturnToBase(flight);
                    runs.RemoveAt(i);
                    continue;
                }
                // Asked for only now, so the container finds a standing
                // request when it lands, and nothing else answers it first.
                if (!run.Ship.HasRequestedRearm &&
                    FastMath.InRange(flight.Aircraft.GlobalPosition(), run.Ship.GlobalPosition(), RequestWithin))
                    run.Ship.RequestRearm();
            }
        }
    }
}

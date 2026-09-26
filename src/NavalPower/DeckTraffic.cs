using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    // Ordered by how close each is to being off the deck, which is also the
    // order they appear in and the basis for counting what is ahead.
    public enum TrafficPhase { Launching, Queued, Recovering }

    public sealed class DeckMovement
    {
        public TrafficPhase Phase;
        public string Name;
        public string Detail;
        public int Ahead = -1;          // movements that must clear first; -1 when not queued
        public Aircraft Aircraft;       // null for a launch we have only requested
        public float RangeMetres;
        public bool Ours;
    }

    // What is happening on and around the deck: hangars working, aircraft
    // taxiing or climbing out, and anything in the pattern to recover here.
    //
    // The hangar's spawn queue lives inside an async state machine rather than
    // an inspectable list, so readiness comes from Hangar.Available and the
    // movements themselves are read from the pilot states.
    public static class DeckTraffic
    {
        private static readonly FieldInfo LandingAirbase = AccessTools.Field(typeof(AIPilotLandingState), "airbase");
        // AIHeloLandingState declares no airbase of its own; it recovers to the
        // one PilotBaseState found.
        private static readonly FieldInfo HeloLandingAirbase = AccessTools.Field(typeof(PilotBaseState), "nearestAirbase");

        internal static string Report() =>
            "deck traffic:" +
            "\n  " + (LandingAirbase != null ? "ok      " : "MISSING ") + "AIPilotLandingState.airbase" +
            "\n  " + (HeloLandingAirbase != null ? "ok      " : "MISSING ") + "PilotBaseState.nearestAirbase";

        // Aircraft that actually came off a given field's hangars. Proximity is
        // no test at all: a nearby airfield, or another carrier in company,
        // fills the list with movements that have nothing to do with this deck.
        private static readonly Dictionary<Aircraft, Airbase> launchedFrom = new Dictionary<Aircraft, Airbase>();

        internal static void NoteLaunch(Airbase field, Aircraft aircraft)
        {
            if (field == null || aircraft == null) return;
            launchedFrom[aircraft] = field;
        }

        private static bool CameFrom(Airbase field, Aircraft aircraft) =>
            aircraft != null && launchedFrom.TryGetValue(aircraft, out Airbase from) && from == field;

        private static void Forget()
        {
            if (launchedFrom.Count == 0) return;
            var gone = new List<Aircraft>();
            foreach (KeyValuePair<Aircraft, Airbase> entry in launchedFrom)
                if (entry.Key == null || entry.Key.disabled || entry.Value == null) gone.Add(entry.Key);
            foreach (Aircraft aircraft in gone) launchedFrom.Remove(aircraft);
        }

        public static List<DeckMovement> Movements(Airbase deck)
        {
            var rows = new List<DeckMovement>();
            if (deck == null || deck.disabled || deck.CurrentHQ == null) return rows;
            FactionHQ hq = deck.CurrentHQ;
            GlobalPosition centre = Airfields.PositionOf(deck);
            Airfields.Hangars(deck, out int ready, out _);
            Forget();

            // Our own requested launches that have not appeared yet.
            foreach (string waiting in FlightOrders.PendingNames(deck))
                rows.Add(new DeckMovement
                {
                    Phase = TrafficPhase.Queued,
                    Name = waiting,
                    Detail = "in the hangar",
                    Ours = true
                });

            // Queued behind a busy hangar: not yet asked of the deck at all.
            foreach (LaunchQueue.Entry entry in LaunchQueue.For(deck))
                rows.Add(new DeckMovement
                {
                    Phase = TrafficPhase.Queued,
                    Name = entry.Callsign + " · " + entry.Plan.Definition.unitName,
                    Detail = "waiting for a hangar",
                    Ours = true
                });

            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Aircraft aircraft) || aircraft.disabled) continue;
                if (aircraft.NetworkHQ != hq) continue;
                Pilot pilot = FlightOrders.FirstPilot(aircraft);
                if (pilot == null) continue;

                float range = FastMath.Distance(aircraft.GlobalPosition(), centre);
                bool ours = FlightOrders.Of(aircraft) != null;
                string name = aircraft.definition?.unitName ?? aircraft.name;

                // Getting off the deck: this deck specifically, recorded when
                // the hangar built the aircraft.
                if (pilot.currentState is AIPilotTaxiState || pilot.currentState is AIPilotTakeoffState ||
                    pilot.currentState is AIHeloTakeoffState)
                {
                    if (!CameFrom(deck, aircraft)) continue;
                    rows.Add(new DeckMovement
                    {
                        Phase = TrafficPhase.Launching,
                        Name = name,
                        Detail = pilot.currentState is AIPilotTaxiState ? "taxiing" : "rolling",
                        Aircraft = aircraft,
                        RangeMetres = range,
                        Ours = ours
                    });
                    continue;
                }

                // In the pattern: recovering here specifically, not merely nearby.
                if (RecoveringTo(pilot, deck))
                    rows.Add(new DeckMovement
                    {
                        Phase = TrafficPhase.Recovering,
                        Name = name,
                        Detail = UnitConverter.DistanceReading(range) + " out",
                        Aircraft = aircraft,
                        RangeMetres = range,
                        Ours = ours
                    });
            }

            rows.Sort((a, b) =>
            {
                int phase = a.Phase.CompareTo(b.Phase);
                return phase != 0 ? phase : a.RangeMetres.CompareTo(b.RangeMetres);
            });

            // There is no readable queue -- the hangar's is an async state
            // machine -- but what has to clear the deck first is observable:
            // everything already rolling, plus whatever we asked for earlier.
            int launching = 0;
            foreach (DeckMovement movement in rows)
                if (movement.Phase == TrafficPhase.Launching) launching++;

            int queuedSoFar = 0;
            foreach (DeckMovement movement in rows)
            {
                if (movement.Phase != TrafficPhase.Queued) continue;
                movement.Ahead = launching + queuedSoFar;
                queuedSoFar++;
                movement.Detail = movement.Ahead == 0
                    ? (ready > 0 ? "next off the deck" : "waiting for a hangar")
                    : movement.Ahead + " ahead";
            }
            return rows;
        }

        private static bool RecoveringTo(Pilot pilot, Airbase deck)
        {
            if (pilot.currentState is AIPilotLandingState fixedWing && LandingAirbase != null)
                return ReferenceEquals(LandingAirbase.GetValue(fixedWing), deck);
            if (pilot.currentState is AIHeloLandingState rotary && HeloLandingAirbase != null)
                return ReferenceEquals(HeloLandingAirbase.GetValue(rotary), deck);
            return false;
        }
    }
}

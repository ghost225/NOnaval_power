using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    public enum TrafficPhase { Queued, Launching, Recovering }

    public sealed class DeckMovement
    {
        public TrafficPhase Phase;
        public string Name;
        public string Detail;
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
        private static readonly FieldInfo HeloLandingAirbase = AccessTools.Field(typeof(AIHeloLandingState), "airbase");

        internal static string Report() =>
            "deck traffic:" +
            "\n  " + (LandingAirbase != null ? "ok      " : "MISSING ") + "AIPilotLandingState.airbase" +
            "\n  " + (HeloLandingAirbase != null ? "ok      " : "MISSING ") + "AIHeloLandingState.airbase";

        public static void Hangars(Ship ship, out int ready, out int busy)
        {
            ready = 0; busy = 0;
            if (ship == null) return;
            foreach (Hangar hangar in ship.GetComponentsInChildren<Hangar>(true))
            {
                if (hangar == null || !hangar.IsFunctional()) continue;
                if (hangar.Available) ready++; else busy++;
            }
        }

        public static List<DeckMovement> Movements(Ship ship)
        {
            var rows = new List<DeckMovement>();
            Airbase deck = CarrierOps.Deck(ship);
            if (deck == null || ship.NetworkHQ == null) return rows;

            // Our own requested launches that have not appeared yet.
            foreach (string waiting in FlightOrders.PendingNames(ship))
                rows.Add(new DeckMovement
                {
                    Phase = TrafficPhase.Queued,
                    Name = waiting,
                    Detail = "in the hangar",
                    Ours = true
                });

            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Aircraft aircraft) || aircraft.disabled) continue;
                if (aircraft.NetworkHQ != ship.NetworkHQ) continue;
                Pilot pilot = FlightOrders.FirstPilot(aircraft);
                if (pilot == null) continue;

                float range = FastMath.Distance(aircraft.GlobalPosition(), ship.GlobalPosition());
                bool ours = FlightOrders.Of(aircraft) != null;
                string name = aircraft.definition?.unitName ?? aircraft.name;

                // Getting off the deck: only count aircraft still close aboard,
                // or every airfield launch in the faction would appear here.
                if (pilot.currentState is AIPilotTaxiState || pilot.currentState is AIPilotTakeoffState ||
                    pilot.currentState is AIHeloTakeoffState)
                {
                    if (range > 2500f) continue;
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

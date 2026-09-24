using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    // Cargo delivery to a chosen place.
    //
    // AIHeloTransportState already knows how to run an approach, pick a
    // touchdown point on ground it can actually use, land, unload, or make a
    // parachute pass -- none of which is worth rewriting. What it will not do
    // is go where it is told: it picks its own destination from the nearest
    // ground enemy or the nearest mission objective.
    //
    // So the state keeps flying the aircraft and its choice is overridden each
    // tick, which is the approach NOCommander takes for the same problem.
    internal static class CargoMissions
    {
        private static readonly Type DestinationType =
            AccessTools.Inner(typeof(AIHeloTransportState), "TransportDestination");

        private static readonly FieldInfo StateAircraft = AccessTools.Field(typeof(PilotBaseState), "aircraft");
        private static readonly FieldInfo TransportMode = AccessTools.Field(typeof(AIHeloTransportState), "transportMode");
        private static readonly FieldInfo Airdrop = AccessTools.Field(typeof(AIHeloTransportState), "airdrop");
        private static readonly FieldInfo Destination = AccessTools.Field(typeof(AIHeloTransportState), "transportDestination");
        private static readonly FieldInfo LastSpotCheck = AccessTools.Field(typeof(AIHeloTransportState), "lastLandingSpotCheck");
        private static readonly FieldInfo ValidMission = DestinationType != null
            ? AccessTools.Field(DestinationType, "validMission") : null;
        private static readonly MethodInfo UpdateTouchdown = DestinationType != null
            ? AccessTools.Method(DestinationType, "UpdateTouchdownPoint", new[] { typeof(float), typeof(Aircraft) })
            : null;
        private static readonly FieldInfo Lz = DestinationType != null
            ? AccessTools.Field(DestinationType, "LZ") : null;
        private static readonly FieldInfo Touchdown = DestinationType != null
            ? AccessTools.Field(DestinationType, "touchdownPoint") : null;
        private static readonly ConstructorInfo NewDestination = DestinationType != null
            ? AccessTools.Constructor(DestinationType,
                new[] { typeof(GlobalPosition), typeof(GlobalPosition), typeof(float) })
            : null;

        internal static string Report() =>
            "cargo missions:" +
            Line("AIHeloTransportState.transportMode", TransportMode) +
            Line("AIHeloTransportState.airdrop", Airdrop) +
            Line("AIHeloTransportState.transportDestination", Destination) +
            Line("TransportDestination.LZ", Lz) +
            Line("TransportDestination.touchdownPoint", Touchdown) +
            Line("TransportDestination.UpdateTouchdownPoint", UpdateTouchdown) +
            Line("TransportDestination..ctor", NewDestination);

        private static string Line(string name, MemberInfo member) =>
            "\n  " + (member != null ? "ok      " : "MISSING ") + name;

        internal static bool Available =>
            TransportMode != null && Airdrop != null && Destination != null &&
            UpdateTouchdown != null && ValidMission != null && NewDestination != null &&
            Lz != null && Touchdown != null;

        // Can this aircraft actually carry anything?
        internal static bool CanCarry(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.weaponStations == null) return false;
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null) continue;
                // The station itself declares it carries cargo -- a container
                // or troops sit on one of these, not on a hook or a ramp, which
                // is why looking only for those missed an aircraft that was
                // plainly loaded.
                if (station.Cargo) return true;
                if (station.Weapons == null) continue;
                foreach (Weapon weapon in station.Weapons)
                    if (weapon is SlingloadHook) return true;
            }
            return aircraft.GetComponentInChildren<CargoRamp>(true) != null;
        }

        // How often the landing zone is re-solved. Doing it every physics tick
        // never lets the state settle on an approach: UpdateLZ commits once the
        // aircraft is within three kilometres of its touchdown point, and
        // re-solving continually keeps moving that point out from under it. The
        // aircraft then overflies the zone without dropping, turns back, and
        // thrashes. Three seconds is what NOCommander uses for the same reason.
        private const float ReplanSeconds = 3f;

        // Point the state at our landing zone instead of its own idea of one.
        internal static void Apply(AIHeloTransportState state, Flight flight)
        {
            if (!Available || state == null || flight == null) return;

            // Cheap every tick: says what job this is, and that there is one.
            TransportMode.SetValue(state, AIHeloTransportState.TransportMode.LandSuppy);
            Airdrop.SetValue(state, flight.Airdrop);
            state.stateDisplayName = flight.Airdrop ? "Airdropping cargo" : "Delivering cargo";

            // A destination the state built for itself carries its own idea of
            // where to go, and one it never built carries nothing: a default
            // struct reads as slope 0, which UpdateTouchdownPoint takes to mean
            // "this ground is already perfectly flat, keep the point you have"
            // -- and the point it has is the world origin. Either way the first
            // order for a zone starts the destination again from that zone.
            object destination;
            if (!flight.CargoSeeded)
            {
                destination = NewDestination.Invoke(new object[] { flight.CargoPoint, flight.CargoPoint, 90f });
                flight.CargoSeeded = true;
                flight.LastCargoPlan = 0f;
            }
            else
            {
                destination = Destination.GetValue(state);
                if (destination == null) return;
            }
            ValidMission.SetValue(destination, true);
            Destination.SetValue(state, destination);

            if (Time.timeSinceLevelLoad - flight.LastCargoPlan < ReplanSeconds) return;
            flight.LastCargoPlan = Time.timeSinceLevelLoad;

            Aircraft aircraft = StateAircraft?.GetValue(state) as Aircraft;
            if (aircraft == null) return;

            // UpdateLZ is not "go to this point" -- it is the state's standoff
            // solver. Given an enemy position it backs a landing zone away from
            // it, along the inbound track, by as much as ten kilometres, so
            // troops are not set down on top of what they came to fight. Fed a
            // zone the commander picked, it walked that zone back up the track
            // until it sat on the aircraft itself, and the load went out the
            // door where the aircraft happened to be. The zone is not a guess
            // to be refined; it is the order. It stays where it was put.
            //
            // A struct field has to be unboxed, mutated and written back; the
            // methods act on the box, not on the field in place.
            Lz.SetValue(destination, flight.CargoPoint);
            if (flight.Airdrop)
            {
                // A parachute pass wants the point itself. What the ground is
                // like underneath is the cargo's problem, not the approach's.
                Touchdown.SetValue(destination, flight.CargoPoint);
            }
            else
            {
                // A landing has to be put down on something usable, so the
                // state's own search runs -- but close in, around the ordered
                // point rather than around a zone of its choosing.
                UpdateTouchdown.Invoke(destination, new object[] { 120f, aircraft });
            }
            Destination.SetValue(state, destination);
            // Only stamped when we actually re-solved, or the state's own
            // throttling is defeated and it re-plans as fast as we do.
            LastSpotCheck.SetValue(state, Time.timeSinceLevelLoad);
        }
    }

    [HarmonyPatch(typeof(AIHeloTransportState), "FixedUpdateState")]
    internal static class CargoTargetPatch
    {
        private static readonly FieldInfo StateAircraft = AccessTools.Field(typeof(PilotBaseState), "aircraft");

        // Before the state acts on its own choice, replace it with ours.
        private static void Prefix(AIHeloTransportState __instance)
        {
            if (!Guard.Ok(Name)) return;
            try
            {
                if (!(StateAircraft?.GetValue(__instance) is Aircraft aircraft)) return;
                Flight flight = FlightOrders.Of(aircraft);
                if (flight == null || flight.Mode != FlightMode.Cargo) return;
                CargoMissions.Apply(__instance, flight);
            }
            catch (Exception ex) { Guard.Failed(Name, ex); }
        }

        private const string Name = "Cargo missions";
    }
}

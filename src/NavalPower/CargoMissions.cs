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
        private static readonly MethodInfo UpdateLz = DestinationType != null
            ? AccessTools.Method(DestinationType, "UpdateLZ",
                new[] { typeof(Aircraft), typeof(GlobalPosition?), typeof(float), typeof(Vector3).MakeByRefType() })
            : null;
        private static readonly MethodInfo UpdateTouchdown = DestinationType != null
            ? AccessTools.Method(DestinationType, "UpdateTouchdownPoint", new[] { typeof(float), typeof(Aircraft) })
            : null;

        internal static string Report() =>
            "cargo missions:" +
            Line("AIHeloTransportState.transportMode", TransportMode) +
            Line("AIHeloTransportState.airdrop", Airdrop) +
            Line("AIHeloTransportState.transportDestination", Destination) +
            Line("TransportDestination.UpdateLZ", UpdateLz) +
            Line("TransportDestination.UpdateTouchdownPoint", UpdateTouchdown);

        private static string Line(string name, MemberInfo member) =>
            "\n  " + (member != null ? "ok      " : "MISSING ") + name;

        internal static bool Available =>
            TransportMode != null && Airdrop != null && Destination != null &&
            UpdateLz != null && UpdateTouchdown != null && ValidMission != null;

        // Can this aircraft actually carry anything?
        internal static bool CanCarry(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.weaponStations == null) return false;
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station?.WeaponInfo == null) continue;
                foreach (Weapon weapon in station.Weapons)
                    if (weapon is SlingloadHook) return true;
            }
            return aircraft.GetComponentInChildren<CargoRamp>(true) != null;
        }

        // Point the state at our landing zone instead of its own idea of one.
        internal static void Apply(AIHeloTransportState state, Flight flight)
        {
            if (!Available || state == null || flight == null) return;

            TransportMode.SetValue(state, AIHeloTransportState.TransportMode.LandSuppy);
            Airdrop.SetValue(state, flight.Airdrop);
            // Keeping this fresh stops the state deciding it has no mission and
            // wandering back to orbit the airbase.
            LastSpotCheck.SetValue(state, Time.timeSinceLevelLoad);
            state.stateDisplayName = flight.Airdrop ? "Airdropping cargo" : "Delivering cargo";

            // A struct field has to be unboxed, mutated and written back; the
            // methods are called on the box, not on the field in place.
            object destination = Destination.GetValue(state);
            if (destination == null) return;
            ValidMission.SetValue(destination, true);

            Aircraft aircraft = StateAircraft?.GetValue(state) as Aircraft;
            if (aircraft == null) return;

            Vector3 approach = aircraft.transform.forward;
            approach.y = 0f;
            if (approach.sqrMagnitude < 0.001f) approach = Vector3.forward;

            object[] lzArgs = { aircraft, (GlobalPosition?)flight.CargoPoint, 100f, approach };
            UpdateLz.Invoke(destination, lzArgs);
            UpdateTouchdown.Invoke(destination, new object[] { flight.Airdrop ? 1000f : 100f, aircraft });
            Destination.SetValue(state, destination);
        }
    }

    [HarmonyPatch(typeof(AIHeloTransportState), "FixedUpdateState")]
    internal static class CargoTargetPatch
    {
        private static readonly FieldInfo StateAircraft = AccessTools.Field(typeof(PilotBaseState), "aircraft");

        // Before the state acts on its own choice, replace it with ours.
        private static void Prefix(AIHeloTransportState __instance)
        {
            if (!(StateAircraft?.GetValue(__instance) is Aircraft aircraft)) return;
            Flight flight = FlightOrders.Of(aircraft);
            if (flight == null || flight.Mode != FlightMode.Cargo) return;
            CargoMissions.Apply(__instance, flight);
        }
    }
}

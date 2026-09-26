using System;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    // Which way to dodge a radar-guided missile.
    //
    // The combat pilot beams the missile -- turns across its line of sight --
    // and of the two ways across it takes whichever is nearer the way the
    // aircraft is already pointing. That keeps its speed, but says nothing
    // about where the enemy is: half the time it breaks towards them. For our
    // flights it breaks to the side of friendly lines -- the side its home
    // deck or field is on -- and keeps the rest of the manoeuvre as the game
    // flies it: the dive, the countermeasure timing, the last-second pull.
    [HarmonyPatch(typeof(AIPilotCombatModes), "EvadeModeRadar")]
    internal static class EvadeTowardFriendsPatch
    {
        private const string Name = "Evasion toward friendly lines";
        private static readonly AccessTools.FieldRef<PilotBaseState, Aircraft> AircraftOf =
            AccessTools.FieldRefAccess<PilotBaseState, Aircraft>("aircraft");
        private static readonly AccessTools.FieldRef<PilotBaseState, GlobalPosition> DestinationOf =
            AccessTools.FieldRefAccess<PilotBaseState, GlobalPosition>("destination");
        private static readonly AccessTools.FieldRef<AIPilotCombatModes, Vector3> EvadeVectorOf =
            AccessTools.FieldRefAccess<AIPilotCombatModes, Vector3>("missileEvadeVector");
        private static readonly AccessTools.FieldRef<AIPilotCombatModes, Vector3> ErrorOf =
            AccessTools.FieldRefAccess<AIPilotCombatModes, Vector3>("randomErrorVector");
        private static readonly AccessTools.FieldRef<AIPilotCombatModes, float> ImpactTimeOf =
            AccessTools.FieldRefAccess<AIPilotCombatModes, float>("missileImpactTime");

        private static void Postfix(AIPilotCombatModes __instance, ref GlobalPosition evadeDestination)
        {
            if (!Guard.Ok(Name)) return;
            try
            {
                Aircraft aircraft = AircraftOf(__instance);
                Flight flight = FlightOrders.Of(aircraft);
                if (flight == null || flight.Home == null || PilotSeat.Flying == flight) return;

                Vector3 friendly = flight.HomePosition - aircraft.GlobalPosition();
                friendly.y = 0f;
                if (friendly.sqrMagnitude < 1f) return;
                ref Vector3 evade = ref EvadeVectorOf(__instance);
                Vector3 flat = new Vector3(evade.x, 0f, evade.z);
                if (flat.sqrMagnitude < 0.0001f || Vector3.Dot(flat, friendly) >= 0f) return;

                // The other way across the missile's line: the same beam, on
                // the friendly side. Rebuilt the way the game builds it.
                evade = -evade;
                float impact = ImpactTimeOf(__instance);
                float blend = impact > 7f ? 0.3f : 1f;
                Vector3 target = aircraft.transform.position + evade * 1000f +
                    ErrorOf(__instance) * 100f / Mathf.Max(aircraft.skill, 0.01f);
                evadeDestination = Vector3.Lerp(DestinationOf(__instance).ToLocalPosition(), target, blend).ToGlobalPosition();
            }
            catch (Exception ex) { Guard.Failed(Name, ex); }
        }
    }
}

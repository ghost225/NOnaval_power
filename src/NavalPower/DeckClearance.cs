using System;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    // Why a recovered aircraft sits on the deck.
    //
    // An aircraft that lands under AI leaves by one of two doors, both inside
    // the ejection sequence that runs once the crew is out:
    //
    //     if (speed < 2 && HQ.AnyNearAirbase(position, out _) && position.y > sea)
    //     { unitState = Abandoned; ReturnToInventory(); }
    //     else if (!disabled) { ...; DisableUnit(); }
    //
    // Either one removes it, so an aircraft still standing there means the
    // sequence did not reach the test, or the test was reached and something
    // threw before the removal. Reasoning about which has been wrong twice, so
    // this records which doors are actually opened and what the test was
    // looking at when it ran.
    internal static class DeckClearance
    {
        internal static void Note(string what, Aircraft aircraft)
        {
            if (!Settings.DeckTrace.Value || aircraft == null) return;
            Flight flight = FlightOrders.Of(aircraft);
            string name = aircraft.definition?.unitName ?? aircraft.name;
            bool nearField = aircraft.NetworkHQ != null &&
                aircraft.NetworkHQ.AnyNearAirbase(aircraft.transform.position, out _);
            Plugin.Log.LogInfo("[clear] " + name + (flight != null ? " (ours)" : "") + " · " + what +
                " · speed " + aircraft.speed.ToString("0.0") +
                " · near a field " + nearField +
                " · above sea " + (aircraft.transform.position.y > Datum.LocalSeaY) +
                " · disabled " + aircraft.disabled +
                " · state " + aircraft.unitState);
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.StartEjectionSequence))]
    internal static class EjectionTracePatch
    {
        private static void Postfix(Aircraft __instance) =>
            Guard.Run("Deck trace: ejection", () => DeckClearance.Note("crew leaving", __instance));
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.ReturnToInventory))]
    internal static class ReturnTracePatch
    {
        private static void Prefix(Aircraft __instance) =>
            Guard.Run("Deck trace: return", () => DeckClearance.Note("returning to inventory", __instance));
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.DisableUnit))]
    internal static class DisableTracePatch
    {
        private static void Prefix(Unit __instance) =>
            Guard.Run("Deck trace: disable", () =>
            {
                if (__instance is Aircraft aircraft) DeckClearance.Note("disabling", aircraft);
            });
    }
}

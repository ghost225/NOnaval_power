using UnityEngine;
using NOrders;

namespace NavalPower
{
    // Shared selection state. The map input layer and the command bar both act
    // on the same weapon/quantity selection, so it lives in one place.
    internal static class CommandState
    {
        // What is being commanded: a ship, or a land airbase. Never both.
        internal static Ship Ship;
        internal static Airbase Base;
        internal static string SelectedKey;
        internal static int Quantity = 1;
        // While a flight is selected, map right-clicks task it rather than the ship.
        internal static Flight SelectedFlight;

        // A cargo order is a place, and the place comes from the map. Until one
        // is picked there is no order, only an intent -- which is why choosing
        // the kind of delivery no longer issues one. It used to, with the
        // aircraft's own position standing in for the zone, and the load went
        // out of the door on the spot.
        internal static Flight AwaitingCargoZone;
        internal static bool AwaitingAirdrop;
        // What the flight was doing when the zone was asked for. Any other
        // order moves it off that, and a request the orders have overtaken must
        // not go on to catch the next map click.
        internal static FlightMode AwaitingFromMode;

        internal static void AskForCargoZone(Flight flight, bool airdrop)
        {
            AwaitingCargoZone = flight;
            AwaitingAirdrop = airdrop;
            AwaitingFromMode = flight.Mode;
        }

        // A landing point wanted for this ship's next craft: the next
        // right-click on the map gives it.
        internal static Ship AwaitingLanding;

        // An airdrop along a line, being laid down: the flight, and the start
        // once the first click has given it.
        internal static Flight AwaitingDropLine;
        // The wing lead whose strike plan the map is building (the strike
        // planner open): clicks on enemies add or remove them, nothing else.
        internal static Flight StrikePlanning;
        internal static GlobalPosition? DropLineStart;

        internal static bool Active => Ship != null || Base != null;

        // The field air operations run from: the base itself, or the ship's deck.
        internal static Airbase Airfield => Base != null ? Base : CarrierOps.Deck(Ship);
        internal static FactionHQ Hq => Ship != null ? Ship.NetworkHQ : Base != null ? Base.CurrentHQ : null;
        internal static string PostName =>
            Ship != null ? ShipNames.Of(Ship) : Base != null ? Airfields.NameOf(Base) : "";
        internal static GlobalPosition PostPosition =>
            Ship != null ? Ship.GlobalPosition() : Base != null ? Airfields.PositionOf(Base) : default;
        internal static bool Armed => SelectedKey != null;

        private static string feedback = "";
        private static float feedbackUntil;

        internal static void Say(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            feedback = message;
            feedbackAt = Time.unscaledTime;
            feedbackUntil = Time.unscaledTime + 12f;
        }

        // Fresh for 12 s, at full colour, ahead of the standing status; after
        // that, still shown dimmed for up to a minute where the line would
        // otherwise be empty -- a newer event replaces it, nothing blanks it.
        private static float feedbackAt = -100f;
        internal static string Feedback => Time.unscaledTime < feedbackUntil ? feedback : null;
        internal static string Recent => Time.unscaledTime - feedbackAt < 60f ? feedback : null;

        internal static void Clear()
        {
            Ship = null;
            Base = null;
            SelectedKey = null;
            SelectedFlight = null;
            AwaitingCargoZone = null;
            AwaitingLanding = null;
            AwaitingDropLine = null;
            StrikePlanning = null;
            DropLineStart = null;
            Quantity = 1;
        }

        internal static WeaponCommandInfo SelectedWeapon()
        {
            if (Ship == null || SelectedKey == null) return null;
            foreach (WeaponCommandInfo weapon in WeaponOrders.GetWeapons(Ship))
                if (weapon.Key == SelectedKey) return weapon;
            return null;
        }
    }
}

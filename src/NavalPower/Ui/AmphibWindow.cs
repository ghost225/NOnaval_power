using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // The Amphibious window, on a ship with a well deck: what the hold has,
    // the craft that are out, loading and launching the next, and buying.
    internal sealed partial class CommandUi
    {
        private void AmphibPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            Amphib.WellDeck deck = Amphib.Deck(ship);
            if (deck == null) { s.Title("AMPHIBIOUS"); s.Info("This ship has no well deck.", Theme.TextMuted); return; }
            Amphib.Plan plan = Amphib.PlanFor(ship);

            int craft = Amphib.Count(deck.Hold, deck.Craft);
            s.Title("AMPHIBIOUS  ·  " + craft + " × " + deck.Craft.unitName + "  ·  " + Amphib.Allocation().ToString("0") + " available");

            // The hold, with what the wave has set aside.
            List<KeyValuePair<UnitDefinition, int>> vehicles = Amphib.Vehicles(deck);
            if (craft == 0 && vehicles.Count == 0) s.Info("The hold is empty · buy landing craft and vehicles below.", Theme.TextMuted);
            else
            {
                s.Info("HOLD", Theme.TextFaint);
                foreach (var entry in vehicles)
                {
                    int reserved = Amphib.Reserved(plan, entry.Key);
                    s.Info("   " + entry.Value + " × " + entry.Key.unitName + "  ·  " + Tonnes(entry.Key.mass) + " each" +
                        (reserved > 0 ? "  ·  " + reserved + " in the wave" : ""), Theme.Text);
                }
                if (vehicles.Count == 0) s.Info("   no vehicles aboard", Theme.TextMuted);
            }

            // The wave being readied.
            s.Info("WAVE  ·  " + (plan.Wave.Count == 0 ? "nothing readied" : plan.Wave.Count + " craft ready"), Theme.TextFaint);
            for (int i = 0; i < plan.Wave.Count; i++)
            {
                int index = i;
                var readied = plan.Wave[i];
                s.Row("   Craft " + (i + 1) + "  ·  " + Amphib.Describe(readied) + "  ·  " + Tonnes(Amphib.Mass(readied)) + "  ·  click to stand down", () =>
                {
                    if (index < plan.Wave.Count) plan.Wave.RemoveAt(index);
                });
            }
            Button load = s.Row("Load a landing craft for the wave…" +
                (Amphib.Free(deck, plan, deck.Craft) <= 0 ? "  ·  no craft free" : ""), () => s.Show(LoadCraftPage));
            if (Amphib.Free(deck, plan, deck.Craft) <= 0) load.GetComponentInChildren<Text>().color = Theme.TextMuted;

            // Where it goes: the commander's call. Shown is where it will
            // really come ashore, with a warning if the way in looks bad.
            bool picking = CommandState.AwaitingLanding == ship;
            string point = picking ? "right-click the map where it should land"
                : !plan.HasPoint ? "not chosen"
                : Bearing(ship, plan.Predicted ? plan.Ashore : plan.Point) + (plan.Hint != null ? "  ·  " + UiKit.Tint(plan.Hint, Theme.Warn) : "");
            Button choose = s.Row("Landing point  ·  " + point, () =>
            {
                CommandState.AwaitingLanding = CommandState.AwaitingLanding == ship ? null : ship;
                if (CommandState.AwaitingLanding != null)
                {
                    if (!DynamicMap.mapMaximized) ToggleMap();
                    CommandState.Say("Right-click the map where the craft should land");
                }
            });
            if (picking) choose.image.color = Theme.AccentFill;

            bool busy = Amphib.DeckBusy(ship);
            bool ready = plan.Wave.Count > 0 && plan.HasPoint && !busy;
            Button launch = s.Row("LAUNCH THE WAVE  ·  " + plan.Wave.Count + " craft" +
                (busy ? "  ·  the well deck is launching" : !plan.HasPoint && plan.Wave.Count > 0 ? "  ·  choose a landing point" : ""), () =>
                {
                    Amphib.LaunchWave(CommandState.Ship, out string reason);
                    CommandState.Say(reason);
                });
            launch.image.color = ready ? Theme.Dim(Theme.Good, 0.35f) : Theme.Control;
            if (!ready) launch.GetComponentInChildren<Text>().color = Theme.TextMuted;

            // Craft that are out.
            bool any = false;
            foreach (Amphib.Sortie sortie in Amphib.SortiesFrom(ship))
            {
                if (!any) { s.Info("AT SEA", Theme.TextFaint); any = true; }
                Amphib.Sortie shown = sortie;
                CameraRow(s, sortie.Name + "  ·  " + Amphib.Status(sortie) + "  ·  " + sortie.Load,
                    () => s.Show(x => SortiePage(x, shown)), sortie.Craft, sortie.Name);
            }

            s.Row("Buy for the hold…", () => s.Show(BuyPage));
        }

        private void LoadCraftPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            Amphib.WellDeck deck = Amphib.Deck(ship);
            if (deck == null) { s.Show(AmphibPage); return; }
            Amphib.Plan plan = Amphib.PlanFor(ship);
            float mass = Amphib.LoadMass(plan), capacity = Amphib.CraftCapacity(deck);
            s.Title("LOAD CRAFT " + (plan.Wave.Count + 1) + "  ·  " + deck.Craft.unitName + "  ·  " + Tonnes(mass) + " of " + Tonnes(capacity));

            List<KeyValuePair<UnitDefinition, int>> vehicles = Amphib.Vehicles(deck);
            if (vehicles.Count == 0) s.Info("No vehicles in the hold · buy some first.", Theme.TextMuted);
            s.Info("Click a vehicle to add one more; past what fits, it goes back to none.", Theme.TextFaint);
            foreach (var entry in vehicles)
            {
                UnitDefinition type = entry.Key;
                plan.Load.TryGetValue(type, out int loaded);
                int free = Amphib.Free(deck, plan, type);
                bool fits = Amphib.Fits(deck, type);
                Button row = s.Row((loaded > 0 ? "▸ " : "") + type.unitName + "  ·  " + Tonnes(type.mass) + "  ·  " +
                    loaded + " of " + free + " free" + (fits ? "" : "  ·  does not fit"), () =>
                    {
                        if (fits) Amphib.Cycle(deck, plan, type);
                    });
                if (loaded > 0) row.image.color = Theme.AccentFill;
                if (!fits || free <= 0) row.GetComponentInChildren<Text>().color = Theme.TextFaint;
            }
            if (Amphib.LoadCount(plan) > 0) s.Row("Clear the load", () => plan.Load.Clear());

            bool craftFree = Amphib.Free(deck, plan, deck.Craft) > 0;
            bool ready = craftFree && Amphib.LoadCount(plan) > 0;
            Button add = s.Row("ADD TO THE WAVE  ·  " + Amphib.LoadCount(plan) + " vehicle(s), " + Tonnes(mass) +
                (craftFree ? "" : "  ·  no landing craft free"), () =>
                {
                    if (Amphib.Ready(CommandState.Ship, out string reason)) s.Show(AmphibPage);
                    CommandState.Say(reason);
                });
            add.image.color = ready ? Theme.Dim(Theme.Good, 0.35f) : Theme.Control;
            if (!ready) add.GetComponentInChildren<Text>().color = Theme.TextMuted;
            s.Row("◀  Back", () => s.Show(AmphibPage));
        }

        private void BuyPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            Amphib.WellDeck deck = Amphib.Deck(ship);
            if (deck == null) { s.Show(AmphibPage); return; }
            float available = Amphib.Allocation();
            s.Title("BUY FOR THE HOLD  ·  " + available.ToString("0") + " available");
            s.Info("Priced as a convoy: value plus ammunition. One per click.", Theme.TextFaint);
            foreach (UnitDefinition type in Amphib.Catalogue(deck))
            {
                UnitDefinition chosen = type;
                float price = Amphib.Price(type);
                int aboard = Amphib.Count(deck.Hold, type);
                Button row = s.Row(type.unitName + "  ·  " + price.ToString("0") +
                    (type == deck.Craft ? "  ·  landing craft" : "  ·  " + Tonnes(type.mass)) +
                    (aboard > 0 ? "  ·  " + aboard + " aboard" : ""), () =>
                    {
                        Amphib.Buy(CommandState.Ship, chosen, 1, out string reason);
                        CommandState.Say(reason);
                    });
                if (price > available) row.GetComponentInChildren<Text>().color = Theme.TextMuted;
            }
            s.Row("◀  Back", () => s.Show(AmphibPage));
        }

        private void SortiePage(Surface s, Amphib.Sortie sortie)
        {
            if (sortie.Craft == null || sortie.Craft.disabled) { s.Show(AmphibPage); return; }
            s.Title(sortie.Name.ToUpperInvariant());
            s.Info(Amphib.Status(sortie), Theme.Text);
            s.Info("Carrying " + sortie.Load, Theme.TextMuted);
            s.Info("Landing at " + Bearing(sortie.Carrier, sortie.Ashore), Theme.TextMuted);
            bool home = sortie.Recalled || sortie.Ai == null ||
                sortie.Ai.state == ShipAI.ShipAIState.returning || sortie.Ai.state == ShipAI.ShipAIState.docking;
            Button recall = s.Row(home ? "Returning to " + ShipNames.Of(sortie.Carrier) : "Recall  ·  back to " + ShipNames.Of(sortie.Carrier) +
                (sortie.Unloaded ? "" : " with its load"), () =>
                {
                    if (!home) Amphib.Recall(sortie);
                });
            if (home) recall.image.color = Theme.AccentFill;
            s.Row(feedView != null && feedView.IsPinned(sortie.Craft) ? "Close its camera feed" : "Pin a camera feed on it", () =>
            {
                if (feedView == null) return;
                feedView.Pin(sortie.Craft, out string reason);
                CommandState.Say(sortie.Name + " · " + reason);
            });
            s.Row("◀  Back", () => s.Show(AmphibPage));
        }

        private static string Tonnes(float kilograms) => (kilograms / 1000f).ToString(kilograms < 10000f ? "0.#" : "0") + " t";

        private static string Bearing(Ship from, Vector3 point)
        {
            if (from == null) return "";
            Vector3 offset = point - from.transform.position;
            offset.y = 0f;
            float bearing = (Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg + 360f) % 360f;
            return UnitConverter.DistanceReading(offset.magnitude) + " at " + bearing.ToString("000") + "°";
        }
    }
}

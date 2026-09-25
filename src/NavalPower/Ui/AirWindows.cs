using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // Air operations: the whole air picture in one window, a window per flight
    // for giving it orders, and the flight deck for putting more up.
    internal sealed partial class CommandUi
    {
        private LoadoutPlan plan;
        private readonly HashSet<string> collapsed = new HashSet<string>();

        // ---- the air picture -------------------------------------------------

        // Every flight we command, grouped by the deck it came off. A fixed
        // strip of six chips ran out of room at about four flights; a list
        // grows, and a group that is not wanted folds away to one line.
        private void AirPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            List<Flight> airborne = FlightOrders.All();
            bool deck = ship != null && CarrierOps.HasDeck(ship);

            s.Title("AIR OPERATIONS  ·  " + airborne.Count + " airborne");

            if (deck)
            {
                DeckTraffic.Hangars(ship, out int ready, out int busy);
                s.Row("LAUNCH AN AIRCRAFT…   ·   " + ready + " hangar(s) ready" +
                    (busy > 0 ? ", " + busy + " working" : ""), () => s.Show(DeckPage));
            }

            if (airborne.Count == 0) s.Info("Nothing airborne.", Theme.TextMuted);

            // Groups in a stable order, so rows do not jump about between
            // refreshes when nothing has actually changed.
            var groups = new SortedDictionary<string, List<Flight>>();
            foreach (Flight flight in airborne)
            {
                string home = flight.HomeName;
                if (!groups.TryGetValue(home, out List<Flight> members)) groups[home] = members = new List<Flight>();
                members.Add(flight);
            }

            foreach (KeyValuePair<string, List<Flight>> group in groups)
            {
                string home = group.Key;
                bool folded = collapsed.Contains(home);
                int trouble = 0;
                foreach (Flight flight in group.Value) if (NeedsYou(flight)) trouble++;

                Button header = s.Row((folded ? "▸  " : "▾  ") + home.ToUpperInvariant() + "   ·   " +
                    group.Value.Count + " flight(s)" +
                    (trouble > 0 ? "   ·   " + UiKit.Tint(trouble + " need attention", Theme.Bad) : ""), () =>
                {
                    if (!collapsed.Remove(home)) collapsed.Add(home);
                });
                header.image.color = Theme.Dim(Theme.Accent, 0.12f);
                if (folded) continue;

                foreach (Flight flight in group.Value)
                {
                    Flight shown = flight;
                    bool selected = CommandState.SelectedFlight == flight;
                    Button row = s.Row("      " + flight.Name + "   ·   " + (flight.Status ?? ShortTask(flight)) +
                        "   ·   " + flight.FuelPercent.ToString("0") + "%   ·   " + flight.StoresSummary,
                        () => OpenFlight(shown));
                    row.GetComponentInChildren<Text>().color =
                        NeedsYou(flight) ? Theme.Bad
                        : flight.Interrupted ? Theme.Warn
                        : flight.Mode == FlightMode.ReturnToBase ? Theme.TextMuted
                        : Theme.Text;
                    row.image.color = selected ? Theme.AccentFill : Theme.Dim(FlightIcons.For(flight), 0.18f);
                }
            }

            if (airborne.Count > 0)
                s.Row("All flights recover", () =>
                {
                    foreach (Flight flight in FlightOrders.All()) FlightOrders.ReturnToBase(flight);
                    CommandState.Say(airborne.Count + " flight(s) recovering");
                });

            if (deck)
            {
                List<DeckMovement> traffic = DeckTraffic.Movements(ship);
                if (traffic.Count > 0)
                {
                    s.Info("DECK TRAFFIC  ·  " + (ship.definition?.unitName ?? ship.name));
                    foreach (DeckMovement movement in traffic)
                    {
                        Button entry = s.Row((movement.Ours ? "▸ " : "") + movement.Name + "  ·  " +
                            Phase(movement.Phase) + "  ·  " + movement.Detail, () => { });
                        entry.GetComponentInChildren<Text>().color =
                            movement.Phase == TrafficPhase.Recovering ? Theme.Warn
                            : movement.Phase == TrafficPhase.Queued ? Theme.TextMuted
                            : movement.Ours ? Theme.Accent : Theme.Text;
                    }
                }
            }
        }

        // Low on fuel, under fire, or out of what it was sent to use: a strike
        // flight with no bombs left needs bringing home even with a full load
        // of air-to-air.
        private static bool NeedsYou(Flight flight) =>
            flight.Threat == FlightThreat.Missile || flight.FuelPercent < 25f ||
            flight.RoundsRemaining <= 0 || flight.AnyRoleExhausted;

        private static string ShortTask(Flight flight)
        {
            switch (flight.Mode)
            {
                case FlightMode.Route: return "route · " + flight.Route.Count + " leg(s)";
                case FlightMode.Orbit: return "on station";
                case FlightMode.Station: return "escort";
                case FlightMode.Strike: return "strike";
                case FlightMode.Jam: return "jamming";
                case FlightMode.Cargo: return "cargo";
                case FlightMode.Egress: return "egress";
                case FlightMode.Engage: return "weapons free";
                default: return "recovering";
            }
        }

        private static string Phase(TrafficPhase phase) =>
            phase == TrafficPhase.Queued ? "queued"
            : phase == TrafficPhase.Launching ? "launching" : "recovering";

        // ---- one flight ------------------------------------------------------

        // Beside the list it was picked from, rather than wherever it was last
        // dragged or on the far side of the screen: the two are used together.
        private void OpenFlight(Flight flight)
        {
            Surface window = Window("flight");
            bool wasOpen = window.IsOpen;
            window.Show(s => FlightPage(s, flight));
            if (wasOpen) return;
            if (windows.TryGetValue("air", out Surface air) && air.IsOpen)
            {
                Vector2 at = air.Panel.anchoredPosition;
                float x = at.x - window.Width - 8f;
                if (x < 8f) x = at.x + air.Width + 8f;       // no room to its left
                window.PlaceAt(new Vector2(x, at.y));
            }
            else window.Place(DropUp(window, ToolFor("air")));
        }

        // While this window is open the map tasks this flight, including from
        // its sub-pages: an altitude change on the way to laying down a route
        // does not hand the map back to the ship.
        private void FlightPage(Surface s, Flight flight)
        {
            if (!Alive(s, flight)) return;
            CommandState.SelectedFlight = flight;

            s.Title(flight.Name.ToUpperInvariant() + "  ·  " + flight.Describe());
            s.Info(flight.TypeName + "   ·   " + flight.FuelPercent.ToString("0") + "% fuel   ·   " +
                flight.StoresSummary + "   ·   from " + flight.HomeName,
                flight.FuelPercent < 25f ? Theme.Bad : Theme.Text);
            s.Info(flight.Stores);
            s.Info(CommandState.AwaitingCargoZone == flight
                    ? UiKit.Tint("WAITING FOR A " + (CommandState.AwaitingAirdrop ? "DROP" : "LANDING") +
                                 " ZONE  ·  right-click the map", Theme.Accent)
                : flight.Route.Count > 0
                    ? "Right-click the map to task it   ·   " + flight.Route.Count + " leg(s) queued"
                    : "Right-click the map to task it   ·   right-click a contact to attack it");

            if (CargoMissions.CanCarry(flight.Aircraft))
            {
                Button cargo = s.Row(
                    CommandState.AwaitingCargoZone == flight
                        ? "CARGO  ·  " + (CommandState.AwaitingAirdrop ? "airdrop" : "landing") + "  ·  waiting for a zone"
                    : flight.Mode == FlightMode.Cargo
                        ? "CARGO  ·  " + (flight.Airdrop ? "airdrop" : "landing") + "  ·  change the zone"
                        : "CARGO  ·  land or airdrop at a point…",
                    () => s.Show(x => CargoPage(x, flight)));
                if (flight.Mode == FlightMode.Cargo || CommandState.AwaitingCargoZone == flight)
                    cargo.image.color = Theme.AccentFill;
            }

            s.Row("Hold here  ·  task area on the aircraft", () =>
            {
                FlightOrders.SetArea(flight, flight.Aircraft.GlobalPosition(), flight.OrbitRadius);
                CommandState.Say(flight.Name + " · holding overhead");
            });
            s.Row("Station on " + flight.HomeName + "  ·  offboard sensor", () =>
            {
                FlightOrders.Station(flight);
                CommandState.Say(flight.Name + " · keeping company");
            });
            s.Row("Clear the route", () =>
            {
                FlightOrders.SetArea(flight, flight.Aircraft.GlobalPosition(), flight.OrbitRadius);
                CommandState.Say(flight.Name + " · route cleared");
            });
            s.Row("Altitude  ·  " + UnitConverter.AltitudeReading(flight.Altitude), () => s.Show(x => AltitudePage(x, flight)));
            s.Row("Task area radius  ·  " + UnitConverter.DistanceReading(flight.OrbitRadius), () => s.Show(x => RadiusPage(x, flight)));
            s.Row("Rules of engagement  ·  " + FlightOrders.Describe(flight.Roe), () => s.Show(x => FlightRoePage(x, flight)));
            s.Row("Engagement  ·  " + (flight.ConfineToArea ? "inside the task area only" : "anywhere in reach"), () =>
            {
                FlightOrders.SetConfined(flight, !flight.ConfineToArea);
                CommandState.Say(flight.Name + (flight.ConfineToArea
                    ? " · will fight only inside its task area"
                    : " · released to engage anywhere in reach"));
            });
            Button free = s.Row("WEAPONS FREE  ·  hand to the AI", () =>
            {
                FlightOrders.Engage(flight);
                CommandState.Say(flight.Name + " · weapons free · it will hunt on its own");
            });
            if (flight.Mode == FlightMode.Engage) free.image.color = Theme.AccentFill;
            Button home = s.Row("Return to base", () =>
            {
                FlightOrders.ReturnToBase(flight);
                CommandState.Say(flight.Name + " · recovering");
            });
            if (flight.Mode == FlightMode.ReturnToBase) home.image.color = Theme.AccentFill;
            s.Row("Callsign  ·  " + flight.Name + "  ·  rename", () => s.Show(x => RenamePage(x, flight)));
            if (feedView != null)
            {
                bool pinned = feedView.IsPinned(flight.Aircraft);
                Button feed = s.Row(pinned ? "Close its camera feed" : "Pin a camera feed on it", () =>
                {
                    feedView.Pin(flight.Aircraft, out string reason);
                    CommandState.Say(flight.Name + " · " + reason);
                });
                if (pinned) feed.image.color = Theme.AccentFill;
            }
            s.Row("TAKE THE CONTROLS  ·  fly it yourself", () =>
            {
                if (!PilotSeat.Take(flight, out string why)) CommandState.Say(flight.Name + " · " + why);
            });
            s.Row("All flights…", () => Open("air", AirPage));
        }

        // A flight whose aircraft is gone has nothing left to order.
        private static bool Alive(Surface s, Flight flight)
        {
            if (flight != null && flight.Aircraft != null && !flight.Aircraft.disabled) return true;
            if (CommandState.SelectedFlight == flight) CommandState.SelectedFlight = null;
            s.Title("FLIGHT LOST");
            s.Info("This flight is no longer flying.", Theme.TextMuted);
            return false;
        }

        private void RenamePage(Surface s, Flight flight)
        {
            if (!Alive(s, flight)) return;
            CommandState.SelectedFlight = flight;
            s.Title(flight.Name.ToUpperInvariant() + "  ·  callsign");
            s.Field(flight.Label, value =>
            {
                FlightOrders.Rename(flight, value);
                CommandState.Say(flight.TypeName + " is now " + flight.Name);
                s.Show(x => FlightPage(x, flight));
            });
            s.Info("It shows on the map, in the hover card and in the kill feed, as a player's name does");
            s.Row("Back", () => s.Show(x => FlightPage(x, flight)));
        }

        private void FlightRoePage(Surface s, Flight flight)
        {
            if (!Alive(s, flight)) return;
            CommandState.SelectedFlight = flight;
            s.Title(flight.Name.ToUpperInvariant() + "  ·  rules of engagement");
            string[] detail =
            {
                "never fights · still evades incoming",
                "fights back at whatever shoots at it",
                "engages hostiles in reach, then resumes"
            };
            for (int i = 0; i < 3; i++)
            {
                var roe = (FlightRoe)i;
                Button row = s.Row(FlightOrders.Describe(roe) + "  ·  " + detail[i], () =>
                {
                    FlightOrders.SetRoe(flight, roe);
                    CommandState.Say(flight.Name + " · " + FlightOrders.Describe(roe));
                    s.Show(x => FlightPage(x, flight));
                });
                if (flight.Roe == roe) row.image.color = Theme.AccentFill;
            }
            s.Row("Back", () => s.Show(x => FlightPage(x, flight)));
        }

        private void CargoPage(Surface s, Flight flight)
        {
            if (!Alive(s, flight)) return;
            CommandState.SelectedFlight = flight;
            s.Title(flight.Name.ToUpperInvariant() + "  ·  cargo");

            // These ask for a zone; they do not order a delivery. The order is
            // placed by the map click that answers them.
            Button land = s.Row("LAND AT A POINT  ·  troops and vehicles get out",
                () => AskForZone(flight, airdrop: false));
            if (Chosen(flight, false)) land.image.color = Theme.AccentFill;
            Button drop = s.Row("AIRDROP AT A POINT  ·  parachute pass, no landing",
                () => AskForZone(flight, airdrop: true));
            if (Chosen(flight, true)) drop.image.color = Theme.AccentFill;

            s.Info(CommandState.AwaitingCargoZone == flight
                ? UiKit.Tint("WAITING FOR A ZONE  ·  right-click the map", Theme.Accent)
                : "Landing is what takes an objective: troops have to get out");

            s.Row("Deliver at " + flight.HomeName, () =>
            {
                if (flight.Parent != null)
                {
                    FlightOrders.Deliver(flight, flight.Parent.GlobalPosition(), flight.Airdrop);
                    CommandState.Say(flight.Name + " · returning cargo to " + flight.HomeName);
                }
                s.Show(x => FlightPage(x, flight));
            });
            s.Row("Cancel the delivery", () =>
            {
                CommandState.AwaitingCargoZone = null;
                FlightOrders.BreakOff(flight);
                CommandState.Say(flight.Name + " · delivery cancelled");
                s.Show(x => FlightPage(x, flight));
            });
            s.Row("Back", () => s.Show(x => FlightPage(x, flight)));
        }

        private static void AskForZone(Flight flight, bool airdrop)
        {
            // Refuse at the button rather than at the map click, so the answer
            // arrives before the work of picking a place for it.
            if (!FlightOrders.CanDeliver(flight.Aircraft))
            {
                CommandState.Say(flight.Name + " · cannot fly a delivery; it has no hover");
                return;
            }
            CommandState.AskForCargoZone(flight, airdrop);
            CommandState.Say(flight.Name + (airdrop
                ? " · right-click the map for the drop zone"
                : " · right-click the map where it should land"));
        }

        // Which kind of delivery this flight is set for: what has been asked
        // for while a zone is still wanted, what was ordered once one is.
        private static bool Chosen(Flight flight, bool airdrop) =>
            CommandState.AwaitingCargoZone == flight
                ? CommandState.AwaitingAirdrop == airdrop
                : flight.Mode == FlightMode.Cargo && flight.Airdrop == airdrop;

        private void AltitudePage(Surface s, Flight flight)
        {
            if (!Alive(s, flight)) return;
            CommandState.SelectedFlight = flight;
            s.Title(flight.Name.ToUpperInvariant() + "  ·  altitude");
            float[] metres = { 100f, 200f, 300f, 600f, 1500f, 3000f, 6000f };
            foreach (float height in metres)
            {
                float chosen = height;
                Button row = s.Row(UnitConverter.AltitudeReading(height) + (height < 400f ? "  ·  terrain following" : ""), () =>
                {
                    FlightOrders.SetAltitude(flight, chosen);
                    s.Show(x => FlightPage(x, flight));
                });
                if (Mathf.Abs(flight.Altitude - height) < 1f) row.image.color = Theme.AccentFill;
            }
            s.Row("Back", () => s.Show(x => FlightPage(x, flight)));
        }

        private void RadiusPage(Surface s, Flight flight)
        {
            if (!Alive(s, flight)) return;
            CommandState.SelectedFlight = flight;
            s.Title(flight.Name.ToUpperInvariant() + "  ·  task area radius");
            float[] metres = { 2000f, 4000f, 8000f, 16000f, 28000f, 45000f };
            foreach (float radius in metres)
            {
                float chosen = radius;
                Button row = s.Row(UnitConverter.DistanceReading(radius), () =>
                {
                    FlightOrders.SetOrbitRadius(flight, chosen);
                    s.Show(x => FlightPage(x, flight));
                });
                if (Mathf.Abs(flight.OrbitRadius - radius) < 1f) row.image.color = Theme.AccentFill;
            }
            s.Row("Back", () => s.Show(x => FlightPage(x, flight)));
        }

        // ---- the flight deck -------------------------------------------------

        private static float Allocation() =>
            GameManager.GetLocalPlayer<NuclearOption.Networking.Player>(out var player) && player != null
                ? player.Allocation : 0f;

        private void DeckPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            if (ship == null || !CarrierOps.HasDeck(ship))
            {
                s.Title("FLIGHT DECK");
                s.Info("This ship has no flight deck.", Theme.TextMuted);
                return;
            }
            DeckAircraft[] available = CarrierOps.Available(ship);
            DeckTraffic.Hangars(ship, out int ready, out int busy);
            bool ownFunds = Settings.LaunchCostFromAllocation.Value;

            s.Title("FLIGHT DECK  ·  " + ready + " ready" + (busy > 0 ? ", " + busy + " working" : "") +
                (ownFunds ? "  ·  " + Allocation().ToString("0") + " available" : ""));

            Button funding = s.Row(ownFunds
                ? "FUNDING  ·  you pay for airframes not in reserve"
                : "FUNDING  ·  the faction pays for everything",
                () => Settings.LaunchCostFromAllocation.Value = !Settings.LaunchCostFromAllocation.Value);
            if (ownFunds) funding.image.color = Theme.AccentFill;

            foreach (DeckAircraft airframe in available)
            {
                DeckAircraft chosen = airframe;
                bool spare = ready > 0;
                bool affordable = airframe.InReserve || !ownFunds || Allocation() >= airframe.Price;
                Button entry = s.Row(airframe.Name + "  ·  " +
                    (airframe.InReserve ? "in reserve  ·  no cost"
                        : ownFunds ? airframe.Price.ToString("0") + " from your allocation"
                        : "purchase " + airframe.Price.ToString("0")) +
                    (spare ? "" : "  ·  no hangar free") +
                    (affordable ? "" : "  ·  cannot afford"), () =>
                    {
                        plan = CarrierOps.PlanFor(chosen.Definition);
                        s.Show(LoadoutPage);
                    });
                if (!spare || !affordable) entry.GetComponentInChildren<Text>().color = Theme.TextMuted;
            }
            s.Row("Back to air operations", () => s.Show(AirPage));
        }

        // Every station listed individually. No presets: a named profile is
        // exactly what picks the wrong weapons.
        private void LoadoutPage(Surface s)
        {
            if (plan == null) { s.Show(DeckPage); return; }
            int armed = 0;
            foreach (LoadoutStation st in plan.Stations) if (st.Selected != null) armed++;
            s.Title((plan.Callsign ?? plan.Definition.unitName).ToUpperInvariant() + "  ·  " +
                plan.Definition.unitName + "  ·  " + (armed > 0 ? armed + " station(s) set" : "clean"));
            foreach (LoadoutStation station in plan.Stations)
            {
                LoadoutStation shown = station;
                Button row = s.Row(station.Name + "   ·   " + station.SelectedName, () => s.Show(x => StationPage(x, shown)));
                if (station.Selected == null) row.GetComponentInChildren<Text>().color = Theme.TextMuted;
            }
            s.Row("Fuel  ·  " + (plan.Fuel * 100f).ToString("0") + "%", () => s.Show(FuelPage));
            s.Row("Callsign  ·  " + (plan.Callsign ?? "none"), () => s.Show(CallsignPage));
            s.Row("Livery  ·  " + plan.LiveryName, () => s.Show(LiveryPage));
            // A launch leaves the window on the deck rather than closing it, so
            // a second can be sent straight after the first.
            Button launch = s.Row("LAUNCH", () =>
            {
                CarrierOps.Launch(CommandState.Ship, plan, out string reason);
                CommandState.Say(reason);
                s.Show(DeckPage);
            });
            launch.image.color = Theme.Dim(Theme.Good, 0.35f);
            s.Row("Back to airframes", () => s.Show(DeckPage));
        }

        private void CallsignPage(Surface s)
        {
            if (plan == null) { s.Show(DeckPage); return; }
            s.Title(plan.Definition.unitName.ToUpperInvariant() + "  ·  callsign");
            s.Field(plan.Callsign, value =>
            {
                value = (value ?? "").Trim();
                if (value.Length > 0) plan.Callsign = value;
                s.Show(LoadoutPage);
            });
            s.Row("Suggest one  ·  " + Callsigns.Suggest(plan.Definition), () =>
            {
                plan.Callsign = Callsigns.Suggest(plan.Definition);
                s.Show(LoadoutPage);
            });
            s.Row("Back", () => s.Show(LoadoutPage));
        }

        // The same list the game's own spawn screen offers for this airframe
        // and faction, workshop skins included.
        private void LiveryPage(Surface s)
        {
            if (plan == null) { s.Show(DeckPage); return; }
            s.Title(plan.Definition.unitName.ToUpperInvariant() + "  ·  livery");
            List<(LiveryKey key, string label)> options = CarrierOps.Liveries(plan.Definition, CommandState.Ship);
            if (options.Count == 0) s.Info("No liveries for this airframe.", Theme.TextMuted);
            foreach ((LiveryKey key, string label) option in options)
            {
                (LiveryKey key, string label) chosen = option;
                Button row = s.Row(option.label, () =>
                {
                    plan.Livery = chosen.key;
                    plan.LiveryName = chosen.label;
                    s.Show(LoadoutPage);
                });
                if (option.key.Equals(plan.Livery)) row.image.color = Theme.AccentFill;
            }
            s.Row("Back", () => s.Show(LoadoutPage));
        }

        private void FuelPage(Surface s)
        {
            float[] levels = { 0.25f, 0.5f, 0.75f, 1f };
            s.Title((plan?.Definition?.unitName ?? "Aircraft").ToUpperInvariant() + "  ·  fuel");
            foreach (float level in levels)
            {
                float chosen = level;
                Button row = s.Row((level * 100f).ToString("0") + "%" +
                    (level <= 0.25f ? "  ·  short legs, lighter" : level >= 1f ? "  ·  full" : ""), () =>
                    {
                        if (plan != null) plan.Fuel = chosen;
                        s.Show(LoadoutPage);
                    });
                if (plan != null && Mathf.Abs(plan.Fuel - level) < 0.01f) row.image.color = Theme.AccentFill;
            }
            s.Row("Back", () => s.Show(LoadoutPage));
        }

        private void StationPage(Surface s, LoadoutStation station)
        {
            s.Title(station.Name.ToUpperInvariant());
            Button empty = s.Row("Empty", () => { station.Selected = null; s.Show(LoadoutPage); });
            if (station.Selected == null) empty.image.color = Theme.AccentFill;
            foreach (WeaponMount option in station.Options)
            {
                if (!CarrierOps.Releasable(CommandState.Ship, option)) continue;
                WeaponMount mount = option;
                string detail = mount.info != null ? "  ·  " + mount.info.weaponName : "";
                if (mount.ammo > 1) detail += " ×" + mount.ammo;
                if (mount.radar) detail += "  ·  RADAR";
                if (mount.countermeasure) detail += "  ·  CM";
                if (mount.Cargo) detail += "  ·  CARGO";
                Button row = s.Row(mount.mountName + detail, () => { station.Selected = mount; s.Show(LoadoutPage); });
                if (station.Selected == mount) row.image.color = Theme.AccentFill;
            }
            s.Row("Back", () => s.Show(LoadoutPage));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // Command bar plus right-click popup menus. Targeting happens on the native
    // map through MapCommand; this surface carries persistent controls, the
    // hover readout, and the context menus that map clicks open.
    internal sealed class CommandUi : MonoBehaviour
    {

        private Font font;
        private Canvas canvas;
        private GameObject root;
        private RectTransform bar, popup, popupContent, hover, weaponRow;
        private Text shipLabel, statusLabel, speedLabel, feedbackLabel, hoverText;
        private Slider speedSlider;
        private bool updatingSlider;
        private float nextRefresh;

        private RectTransform damagePanel;
        private Text damageHeader;
        private readonly List<Button> damageRows = new List<Button>();
        private readonly List<Text> damageLabels = new List<Text>();
        private readonly List<int> damageIds = new List<int>();
        private const int DamageRows = 16;
        private bool damageOpen;

        private LoadoutPlan plan;
        private DeckAircraft[] deckAircraft;

        private Unit contextTarget;
        private bool contextAppend;
        private string popupKey;
        private Text popupHeader;

        private readonly List<Button> weaponButtons = new List<Button>();
        private readonly List<Text> weaponLabels = new List<Text>();
        private readonly List<string> weaponKeys = new List<string>();
        private readonly List<Button> quantityButtons = new List<Button>();
        private readonly List<Button> roeButtons = new List<Button>();
        private static readonly int[] Counts = { 1, 2, 4, 8, 16 };

        internal bool PopupOpen => popup != null && popup.gameObject.activeSelf;

        // The flight panel is a working surface, not a one-shot menu: while it
        // is open the map keeps tasking the selected flight and clicks do not
        // dismiss it, so a multi-leg route can be laid down without reopening
        // anything between points.
        internal bool Pinned => PopupOpen && popupKey == "flight";

        private float nextPinnedRefresh;
        private Flight pinnedFlight;

        internal void RefreshPinned()
        {
            if (!Pinned || pinnedFlight == null) return;
            // Never rebuild under the cursor: recreating rows mid-click eats it.
            if (PointerInside() || Time.unscaledTime < nextPinnedRefresh) return;
            nextPinnedRefresh = Time.unscaledTime + 1f;
            FlightMenu(pinnedFlight);
        }

        internal bool Contains(Transform candidate) =>
            root != null && candidate != null && (candidate == root.transform || candidate.IsChildOf(root.transform));

        internal bool PointerInside()
        {
            if (root == null || !root.activeSelf) return false;
            if (bar != null && RectTransformUtility.RectangleContainsScreenPoint(bar, Input.mousePosition)) return true;
            return PopupOpen && RectTransformUtility.RectangleContainsScreenPoint(popup, Input.mousePosition);
        }

        private void Update()
        {
            if (!CommandState.Active) { if (root != null) root.SetActive(false); return; }
            Ensure();
            root.SetActive(true);
            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.2f; Refresh(); }
            RefreshHover();
        }

        // ---- refresh ------------------------------------------------------

        private void Refresh()
        {
            Ship ship = CommandState.Ship;
            if (ship == null) return;

            shipLabel.text = ship.definition?.unitName ?? ship.name;
            statusLabel.text = WeaponOrders.GetStatus(ship);

            bool silent = Sensors.IsSilent(ship);
            SetPill(emconPill, silent ? "EMCON SILENT" : "RADIATING", silent ? Theme.Good : Theme.Warn);

            EngagementMode roe = EngagementPolicy.GetMode(ship);
            SetPill(roePill, EngagementPolicy.Describe(roe).ToUpperInvariant(),
                roe == EngagementMode.WeaponsFree ? Theme.Bad : roe == EngagementMode.WeaponsTight ? Theme.Warn : Theme.Good);

            DamageSnapshot damage = DamageControl.GetSnapshot(ship);
            float reserve = damage.DamageControlPoolMax > 0.01f
                ? Mathf.Clamp01(damage.DamageControlPool / damage.DamageControlPoolMax) * 100f : 100f;
            SetPill(damagePill, "DC " + reserve.ToString("0") + "%", Theme.Scale(reserve));

            int own = TrackPicture.OwnCount(ship);
            int esm = Esm.GetContacts(ship).Length;
            SetPill(trackPill, own + " OWN  ·  " + esm + " ESM",
                own > 0 ? Theme.OwnTrack : esm > 0 ? Theme.Passive : Theme.TextMuted);

            NavigationSnapshot nav = NavigationOrders.GetSnapshot(ship);
            if (nav != null)
            {
                speedLabel.text = "Actual " + UnitConverter.SpeedReadingGround(
                        nav.ActualSpeedKnots * CommandableShip.MetresPerSecondPerKnot) +
                    "  /  Ordered " + UnitConverter.SpeedReadingGround(
                        nav.OrderedSpeedKnots * CommandableShip.MetresPerSecondPerKnot);
                updatingSlider = true;
                speedSlider.minValue = nav.MinimumSpeedKnots;
                speedSlider.maxValue = Mathf.Max(nav.MinimumSpeedKnots + 0.1f, nav.MaximumSpeedKnots);
                speedSlider.value = Mathf.Clamp(nav.OrderedSpeedKnots, speedSlider.minValue, speedSlider.maxValue);
                updatingSlider = false;
            }

            if (CommandState.SelectedFlight != null)
                SetPill(trackPill, "TASKING " + CommandState.SelectedFlight.Name.ToUpperInvariant(), Theme.Accent);

            if (CarrierOps.HasDeck(ship))
            {
                int queued = 0, launching = 0, recovering = 0;
                foreach (DeckMovement movement in DeckTraffic.Movements(ship))
                {
                    if (movement.Phase == TrafficPhase.Queued) queued++;
                    else if (movement.Phase == TrafficPhase.Launching) launching++;
                    else recovering++;
                }
                if (queued + launching + recovering > 0)
                    statusLabel.text = "DECK  ·  " + queued + " queued, " + launching + " launching, " +
                        recovering + " recovering   ·   " + statusLabel.text;
            }

            string say = CommandState.Feedback;
            feedbackLabel.text = say ?? ((nav != null ? nav.Status + "  ·  " : "") +
                (CommandState.Armed
                    ? "Right-click a contact to engage with " + (CommandState.SelectedWeapon()?.Name ?? "")
                    : "Right-click the map for a waypoint · shift to append · right-click a contact for options"));

            RefreshWeapons();
            RefreshQuantities();
            RefreshDamage();
            EngagementMode mode = EngagementPolicy.GetMode(ship);
            for (int i = 0; i < roeButtons.Count; i++)
                roeButtons[i].image.color = (EngagementMode)i == mode ? Theme.AccentFill : Theme.Control;
        }

        private void RefreshWeapons()
        {
            WeaponCommandInfo[] weapons = WeaponOrders.GetWeapons(CommandState.Ship);
            while (weaponButtons.Count < weapons.Length)
            {
                int index = weaponButtons.Count;
                Button button = MakeButton(weaponRow, "", index * 250, 0, 244, 38, () => SelectWeapon(index, null));
                weaponButtons.Add(button);
                weaponLabels.Add(button.GetComponentInChildren<Text>());
            }
            weaponKeys.Clear();
            for (int i = 0; i < weaponButtons.Count; i++)
            {
                bool live = i < weapons.Length;
                weaponButtons[i].gameObject.SetActive(live);
                if (!live) continue;
                WeaponCommandInfo weapon = weapons[i];
                weaponKeys.Add(weapon.Key);
                weaponLabels[i].text = weapon.Name + "\n" + weapon.Readiness +
                    (weapon.Continuous ? " · continuous" : " · " + weapon.Ammo);
                bool selected = weapon.Key == CommandState.SelectedKey;
                weaponButtons[i].image.color = selected ? Theme.AccentFill : Theme.Control;
                weaponLabels[i].color = weapon.Readiness == "Ready" ? (selected ? Theme.Text : Theme.Text)
                    : weapon.Readiness == "Reloading" ? Theme.Warn : Theme.TextFaint;
            }
        }

        private void RefreshQuantities()
        {
            WeaponCommandInfo selected = CommandState.SelectedWeapon();
            bool relevant = selected != null && !selected.Continuous;
            for (int i = 0; i < quantityButtons.Count; i++)
            {
                quantityButtons[i].gameObject.SetActive(relevant);
                quantityButtons[i].image.color = Counts[i] == CommandState.Quantity ? Theme.AccentFill : Theme.Control;
            }
        }

        private void RefreshHover()
        {
            Unit unit = MapCommand.Instance?.HoverUnit;
            EsmContact estimate = MapCommand.Instance?.HoverEsm;
            if (estimate == null && (unit == null || unit == CommandState.Ship))
            { hover.gameObject.SetActive(false); return; }
            hover.gameObject.SetActive(true);
            hoverText.text = estimate != null
                ? TrackReadout.DescribeEsm(CommandState.Ship, estimate)
                : TrackReadout.Describe(CommandState.Ship, unit, CommandState.SelectedWeapon());
            Vector2 size = new Vector2(Mathf.Max(260f, hoverText.preferredWidth + 24f), hoverText.preferredHeight + 18f);
            hover.sizeDelta = size;
            float scale = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
            Vector2 pixels = size * scale;
            Vector2 point = Input.mousePosition;
            // Flip toward the screen centre so the card never leaves the view.
            float x = point.x + 18f, y = point.y - 18f;
            if (x + pixels.x > Screen.width) x = point.x - 18f - pixels.x;
            if (y - pixels.y < 0f) y = point.y + 18f + pixels.y;
            hover.position = new Vector2(x, y);
        }

        // ---- actions ------------------------------------------------------

        private void SelectWeapon(int index, Unit immediateTarget)
        {
            if (index >= weaponKeys.Count) return;
            string key = weaponKeys[index];
            CommandState.SelectedKey = CommandState.SelectedKey == key && immediateTarget == null ? null : key;
            if (CommandState.SelectedKey != null && immediateTarget != null)
            {
                WeaponOrders.Attack(CommandState.Ship, CommandState.SelectedKey, immediateTarget,
                    CommandState.Quantity, out string reason, contextAppend);
                CommandState.Say(reason);
                ClosePopup();
            }
            else
            {
                CommandState.Say(CommandState.SelectedKey == null
                    ? "Weapon deselected" : "Right-click a contact to engage");
            }
            Refresh();
        }

        // ---- popup menus --------------------------------------------------

        internal void OpenContext(Vector2 screenPosition, Unit target, bool append)
        {
            contextTarget = target;
            contextAppend = append;
            popupKey = "context";
            string title = target != null
                ? (target.definition?.unitName ?? target.name)
                : (CommandState.Ship?.definition?.unitName ?? "Ship");
            int rows = CarrierOps.HasDeck(CommandState.Ship) ? 7 : 6;
            if (target != null && FlightOrders.For(CommandState.Ship).Count > 0) rows = 8;
            StartPopup(title, screenPosition, rows);
            Row("Engage with…", 1, WeaponMenu);
            if (target != null && FlightOrders.For(CommandState.Ship).Count > 0)
                Row("Strike with flights…", 8, () => StrikeMenu(target));
            Row("Navigate / speed…", 2, NavigationMenu);
            Row("Engagement permissions…", 3, RoeMenu);
            Row("Sensors / EMCON…", 6, SensorMenu);
            if (CarrierOps.HasDeck(CommandState.Ship)) Row("Flight deck…", 7, DeckMenu);
            Row("Cease fire", 4, () =>
            {
                WeaponOrders.CeaseFire(CommandState.Ship, out string reason);
                CommandState.SelectedKey = null;
                CommandState.Say(reason);
                ClosePopup();
            });
            Row("Close", 5, ClosePopup);
        }

        private void WeaponMenu()
        {
            WeaponCommandInfo[] weapons = WeaponOrders.GetWeapons(CommandState.Ship);
            StartPopup(contextTarget != null
                ? "Engage " + (contextTarget.definition?.unitName ?? contextTarget.name)
                : "Select weapon", null, weapons.Length + 1);
            for (int i = 0; i < weapons.Length; i++)
            {
                WeaponCommandInfo weapon = weapons[i];
                int index = i;
                bool capable = contextTarget == null || WeaponOrders.Opportunity(
                    WeaponOrders.StationsFor(CommandState.Ship, weapon.Key).FirstOrDefault()?.WeaponInfo, contextTarget) > 0.01f;
                Button row = Row(weapon.Name + "  ·  " + weapon.Readiness +
                    (weapon.Continuous ? " · continuous" : " · " + weapon.Ammo + " remaining") +
                    (capable ? "" : "  ·  ineffective"), i + 1, () =>
                    {
                        weaponKeys.Clear();
                        foreach (WeaponCommandInfo w in WeaponOrders.GetWeapons(CommandState.Ship)) weaponKeys.Add(w.Key);
                        SelectWeapon(index, contextTarget);
                    });
                if (!capable) row.GetComponentInChildren<Text>().color = Theme.TextFaint;
            }
            Row("Close", weapons.Length + 1, ClosePopup);
        }

        private void NavigationMenu()
        {
            StartPopup("Navigate", null, 8);
            string[] presets = { "All stop", "Ahead 1/3", "Ahead 2/3", "Ahead full", "Ahead flank", "Back 1/3" };
            float[] fractions = { 0f, 1f / 3f, 2f / 3f, 0.9f, 1f, -1f / 3f };
            for (int i = 0; i < presets.Length; i++)
            {
                float fraction = fractions[i];
                Row(presets[i], i + 1, () =>
                {
                    NavigationOrders.SetOrderedSpeedKnots(CommandState.Ship,
                        CommandableShip.MaximumSpeedKnots(CommandState.Ship) * fraction, out string reason);
                    CommandState.Say(reason);
                    ClosePopup();
                });
            }
            Row("Clear route", 7, () =>
            {
                NavigationOrders.ClearWaypoints(CommandState.Ship, out string reason);
                CommandState.Say(reason);
                ClosePopup();
            });
            Row("Close", 8, ClosePopup);
        }

        internal void OpenEsmContext(Vector2 screenPosition, EsmContact contact)
        {
            popupKey = "esm";
            StartPopup("ESM " + contact.Id + " · " + contact.Type, screenPosition, 3);
            Row("Steer toward this bearing", 1, () =>
            {
                NavigationOrders.ReplaceWaypoint(CommandState.Ship, contact.Position, out string reason);
                CommandState.Say(reason);
                ClosePopup();
            });
            Row("Append leg toward bearing", 2, () =>
            {
                NavigationOrders.AppendWaypoint(CommandState.Ship, contact.Position, out string reason);
                CommandState.Say(reason);
                ClosePopup();
            });
            Row("Close", 3, ClosePopup);
        }

        // ---- flights --------------------------------------------------------

        private void StrikeMenu(Unit target)
        {
            List<Flight> capable = FlightOrders.CapableOf(CommandState.Ship, target);
            List<Flight> all = FlightOrders.For(CommandState.Ship);
            string name = target.definition?.unitName ?? target.name;
            StartPopup("Strike " + name, null, all.Count + 3);

            Row(capable.Count > 0 ? "ALL CAPABLE  ·  " + capable.Count + " flight(s)" : "No flight can hurt this target",
                1, () =>
                {
                    foreach (Flight flight in capable) FlightOrders.Strike(flight, target);
                    CommandState.Say(capable.Count + " flight(s) striking " + name);
                    ClosePopup();
                });

            for (int i = 0; i < all.Count; i++)
            {
                Flight flight = all[i];
                bool able = capable.Contains(flight);
                Button row = Row(flight.Name + "   ·   " + (able ? flight.Describe() : "cannot engage this target"),
                    i + 2, () =>
                    {
                        if (!able) { CommandState.Say(flight.Name + " carries nothing that can hurt " + name); return; }
                        FlightOrders.Strike(flight, target);
                        CommandState.Say(flight.Name + " striking " + name);
                        ClosePopup();
                    });
                if (!able) row.GetComponentInChildren<Text>().color = Theme.TextFaint;
            }
            Row("Close", all.Count + 2, ClosePopup);
        }

        private void FlightsMenu()
        {
            List<Flight> airborne = FlightOrders.For(CommandState.Ship);
            StartPopup("Flights  ·  " + airborne.Count + " airborne", null, airborne.Count + 2);
            if (airborne.Count == 0)
            {
                Row("Nothing airborne from this deck", 1, ClosePopup);
                Row("Close", 2, ClosePopup);
                return;
            }
            for (int i = 0; i < airborne.Count; i++)
            {
                Flight flight = airborne[i];
                bool selected = CommandState.SelectedFlight == flight;
                Button row = Row((selected ? "▸ " : "") + flight.Name + "   ·   " + flight.Describe(),
                    i + 1, () => FlightMenu(flight));
                if (selected) row.image.color = Theme.AccentFill;
                row.GetComponentInChildren<Text>().color =
                    flight.Threat == FlightThreat.Missile ? Theme.Bad
                    : flight.Interrupted || flight.Mode == FlightMode.Engage ? Theme.Warn
                    : flight.Mode == FlightMode.ReturnToBase ? Theme.TextMuted
                    : Theme.Text;
            }
            Row("Close", airborne.Count + 1, ClosePopup);
        }

        private void FlightMenu(Flight flight)
        {
            CommandState.SelectedFlight = flight;
            pinnedFlight = flight;
            popupKey = "flight";

            string legs = flight.Route.Count > 0 ? flight.Route.Count + " leg(s) queued" : "no route";
            StartPopup(flight.Name + "  ·  " + flight.Describe(), null, 11);

            // The panel stays open, so this reads as standing guidance rather
            // than an instruction to be dismissed.
            Button tasking = Row("TASKING  ·  right-click the map to add a leg  ·  " + legs, 1, () =>
            {
                flight.Route.Clear();
                FlightOrders.Orbit(flight, flight.Aircraft.GlobalPosition());
                CommandState.Say(flight.Name + " · route cleared");
                FlightMenu(flight);
            });
            tasking.image.color = Theme.AccentFill;
            tasking.GetComponentInChildren<Text>().color = Theme.Text;

            Row("Orbit here", 2, () =>
            {
                FlightOrders.Orbit(flight, flight.Aircraft.GlobalPosition());
                CommandState.Say(flight.Name + " · holding overhead");
                FlightMenu(flight);
            });
            Row("Station on the ship  ·  offboard sensor", 3, () =>
            {
                FlightOrders.Station(flight);
                CommandState.Say(flight.Name + " · keeping company");
                FlightMenu(flight);
            });
            Row("Altitude  ·  " + UnitConverter.AltitudeReading(flight.Altitude), 4, () => AltitudeMenu(flight));
            Row("Orbit radius  ·  " + UnitConverter.DistanceReading(flight.OrbitRadius), 5, () => RadiusMenu(flight));
            Row("WEAPONS FREE  ·  hand to the AI", 6, () =>
            {
                FlightOrders.Engage(flight);
                CommandState.Say(flight.Name + " · weapons free · it will hunt on its own");
                FlightMenu(flight);
            });
            Row("Rules of engagement  ·  " + FlightOrders.Describe(flight.Roe), 7, () => FlightRoeMenu(flight));
            Row(flight.Mode == FlightMode.Strike ? "Break off the attack" : "Break off  ·  no attack running", 8, () =>
            {
                FlightOrders.BreakOff(flight);
                CommandState.Say(flight.Name + " · breaking off");
                FlightMenu(flight);
            });
            Row("Return to base", 9, () =>
            {
                FlightOrders.ReturnToBase(flight);
                CommandState.Say(flight.Name + " · recovering");
                FlightMenu(flight);
            });
            Row("Other flights", 10, FlightsMenu);
            Row("DONE  ·  return the map to the ship", 11, ClosePopup);
        }

        private void FlightRoeMenu(Flight flight)
        {
            StartPopup(flight.Name + " · rules of engagement", null, 5);
            string[] detail =
            {
                "never fights · still evades incoming",
                "fights back at whatever shoots at it",
                "engages hostiles in reach, then resumes"
            };
            for (int i = 0; i < 3; i++)
            {
                var roe = (FlightRoe)i;
                Button row = Row(FlightOrders.Describe(roe) + "  ·  " + detail[i], i + 1, () =>
                {
                    FlightOrders.SetRoe(flight, roe);
                    CommandState.Say(flight.Name + " · " + FlightOrders.Describe(roe));
                    FlightMenu(flight);
                });
                if (flight.Roe == roe) row.image.color = Theme.AccentFill;
            }
            Row("Back", 4, () => FlightMenu(flight));
            Row("Close", 5, ClosePopup);
        }

        private void AltitudeMenu(Flight flight)
        {
            float[] metres = { 100f, 200f, 300f, 600f, 1500f, 3000f, 6000f };
            StartPopup(flight.Name + " · altitude", null, metres.Length + 1);
            for (int i = 0; i < metres.Length; i++)
            {
                float height = metres[i];
                Row(UnitConverter.AltitudeReading(height) + (height < 400f ? "  ·  terrain following" : ""), i + 1, () =>
                {
                    FlightOrders.SetAltitude(flight, height);
                    FlightMenu(flight);
                });
            }
            Row("Back", metres.Length + 1, () => FlightMenu(flight));
        }

        private void RadiusMenu(Flight flight)
        {
            float[] metres = { 2000f, 4000f, 8000f, 16000f, 28000f };
            StartPopup(flight.Name + " · orbit radius", null, metres.Length + 1);
            for (int i = 0; i < metres.Length; i++)
            {
                float radius = metres[i];
                Row(UnitConverter.DistanceReading(radius), i + 1, () =>
                {
                    FlightOrders.SetOrbitRadius(flight, radius);
                    FlightMenu(flight);
                });
            }
            Row("Back", metres.Length + 1, () => FlightMenu(flight));
        }

        // ---- flight deck ----------------------------------------------------

        private void DeckMenu()
        {
            Ship ship = CommandState.Ship;
            if (!CarrierOps.HasDeck(ship))
            {
                StartPopup("Flight deck", null, 1);
                Row("This ship has no flight deck", 1, ClosePopup);
                return;
            }
            deckAircraft = CarrierOps.Available(ship);
            List<DeckMovement> traffic = DeckTraffic.Movements(ship);
            DeckTraffic.Hangars(ship, out int ready, out int busy);

            int rows = deckAircraft.Length + traffic.Count + (traffic.Count > 0 ? 3 : 2);
            StartPopup("Flight deck  ·  " + ready + " ready" + (busy > 0 ? ", " + busy + " working" : ""), null, rows);

            int row = 1;
            for (int i = 0; i < deckAircraft.Length; i++)
            {
                DeckAircraft airframe = deckAircraft[i];
                bool spare = ready > 0;
                Button entry = Row(airframe.Name + "  ·  " +
                    (airframe.InReserve ? "in reserve" : "purchase " + airframe.Price.ToString("0")) +
                    (spare ? "" : "  ·  no hangar free"), row++, () =>
                    {
                        plan = CarrierOps.PlanFor(airframe.Definition);
                        LoadoutMenu();
                    });
                if (!spare) entry.GetComponentInChildren<Text>().color = Theme.TextMuted;
            }

            if (traffic.Count > 0)
            {
                InformationRow("DECK TRAFFIC", row++);
                foreach (DeckMovement movement in traffic)
                {
                    Button entry = Row((movement.Ours ? "▸ " : "") + movement.Name + "  ·  " +
                        Phase(movement.Phase) + "  ·  " + movement.Detail, row++, () => { });
                    entry.GetComponentInChildren<Text>().color =
                        movement.Phase == TrafficPhase.Recovering ? Theme.Warn
                        : movement.Phase == TrafficPhase.Queued ? Theme.TextMuted
                        : movement.Ours ? Theme.Accent : Theme.Text;
                }
            }
            Row("Close", row, ClosePopup);
        }

        private static string Phase(TrafficPhase phase) =>
            phase == TrafficPhase.Queued ? "queued"
            : phase == TrafficPhase.Launching ? "launching" : "recovering";

        // A non-interactive caption row inside a popup.
        private void InformationRow(string value, int row)
        {
            Text label = Label(popupContent, value, Theme.LabelSize, TextAnchor.MiddleLeft, Theme.TextFaint);
            Place(label.rectTransform, 10, (row - 1) * 38 + 10, 376, 20);
        }

        // Every station listed individually. No presets: a named profile is
        // exactly what picks the wrong weapons.
        private void LoadoutMenu()
        {
            if (plan == null) { DeckMenu(); return; }
            int armed = 0;
            foreach (LoadoutStation st in plan.Stations) if (st.Selected != null) armed++;
            int rows = plan.Stations.Count + 3;
            StartPopup(plan.Definition.unitName + " · loadout" +
                (armed > 0 ? "  ·  " + armed + " station(s) set" : "  ·  clean"), null, rows);
            for (int i = 0; i < plan.Stations.Count; i++)
            {
                LoadoutStation station = plan.Stations[i];
                Button row = Row(station.Name + "   ·   " + station.SelectedName, i + 1, () => StationMenu(station));
                if (station.Selected == null) row.GetComponentInChildren<Text>().color = Theme.TextMuted;
            }
            // Just the verb: the loadout is listed row by row directly above,
            // and spelling it out again overran the button.
            Row("LAUNCH", plan.Stations.Count + 1, () =>
            {
                CarrierOps.Launch(CommandState.Ship, plan, out string reason);
                CommandState.Say(reason);
                ClosePopup();
            });
            Row("Back to airframes", plan.Stations.Count + 2, DeckMenu);
            Row("Close", rows, ClosePopup);
        }

        private void StationMenu(LoadoutStation station)
        {
            StartPopup(station.Name, null, CountReleasable(station) + 2);
            Row("Empty", 1, () => { station.Selected = null; LoadoutMenu(); });
            var allowed = new List<WeaponMount>();
            foreach (WeaponMount option in station.Options)
                if (CarrierOps.Releasable(CommandState.Ship, option)) allowed.Add(option);
            for (int i = 0; i < allowed.Count; i++)
            {
                WeaponMount mount = allowed[i];
                string detail = mount.info != null ? "  ·  " + mount.info.weaponName : "";
                if (mount.ammo > 1) detail += " ×" + mount.ammo;
                if (mount.radar) detail += "  ·  RADAR";
                if (mount.countermeasure) detail += "  ·  CM";
                if (mount.Cargo) detail += "  ·  CARGO";
                Row(mount.mountName + detail, i + 2, () => { station.Selected = mount; LoadoutMenu(); });
            }
            Row("Close", allowed.Count + 2, ClosePopup);
        }

        private static int CountReleasable(LoadoutStation station)
        {
            int count = 0;
            foreach (WeaponMount option in station.Options)
                if (CarrierOps.Releasable(CommandState.Ship, option)) count++;
            return count;
        }

        private void SensorMenu()
        {
            SensorSnapshot[] sensors = Sensors.GetSensors(CommandState.Ship);
            bool silent = Sensors.IsSilent(CommandState.Ship);
            StartPopup((silent ? "Sensors · EMCON SILENT" : "Sensors") +
                "   ·   own tracks " + TrackPicture.OwnCount(CommandState.Ship) +
                "   ·   ESM " + Esm.GetContacts(CommandState.Ship).Length +
                (silent ? "   (picture is datalink only)" : ""),
                null, sensors.Length + 3);
            Row("EMCON · all emitters silent", 1, () =>
            {
                Sensors.SetAllEmitting(CommandState.Ship, false, out string reason);
                CommandState.Say(reason);
                SensorMenu();
            });
            Row("Radiate · all emitters on", 2, () =>
            {
                Sensors.SetAllEmitting(CommandState.Ship, true, out string reason);
                CommandState.Say(reason);
                SensorMenu();
            });
            for (int i = 0; i < sensors.Length; i++)
            {
                SensorSnapshot sensor = sensors[i];
                string state = !sensor.Operational ? "UNAVAILABLE"
                    : !sensor.IsEmitter ? "passive"
                    : sensor.Active ? (sensor.Jammed ? "RADIATING · JAMMED" : "RADIATING")
                    : "silent";
                string detail = sensor.RangeMetres > 1f
                    ? "  ·  " + UnitConverter.DistanceReading(sensor.RangeMetres) : "";
                if (sensor.DetectedCount >= 0) detail += "  ·  " + sensor.DetectedCount + " tracked";
                Button row = Row(sensor.Name + "  ·  " + state + detail, i + 3, () =>
                {
                    if (!sensor.IsEmitter) { CommandState.Say(sensor.Name + " is passive; it emits nothing to shut down."); return; }
                    Sensors.SetEmitting(CommandState.Ship, sensor.Id, !sensor.Active, out string reason);
                    CommandState.Say(reason);
                    SensorMenu();
                });
                row.GetComponentInChildren<Text>().color =
                    !sensor.Operational ? Theme.TextFaint
                    : !sensor.IsEmitter ? Theme.Datalink
                    : sensor.Jammed ? Theme.Bad
                    : sensor.Active ? Theme.Warn      // radiating is a risk, not a success
                    : Theme.Good;                      // silent is the safe state
            }
            Row("Close", sensors.Length + 3, ClosePopup);
        }

        private void RoeMenu()
        {
            StartPopup("Engagement permissions", null, 4);
            for (int i = 0; i < 3; i++)
            {
                var mode = (EngagementMode)i;
                Row(EngagementPolicy.Describe(mode), i + 1, () =>
                {
                    EngagementPolicy.SetMode(CommandState.Ship, mode, out string reason);
                    CommandState.Say(reason);
                    ClosePopup();
                    Refresh();
                });
            }
            Row("Close", 4, ClosePopup);
        }

        private void StartPopup(string title, Vector2? screenPosition, int rows)
        {
            Ensure();
            for (int i = popupContent.childCount - 1; i >= 0; i--)
            {
                Transform child = popupContent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
            popup.gameObject.SetActive(true);
            float height = rows * 38f + 46f;
            popup.sizeDelta = new Vector2(392, height);
            // sizeDelta is in canvas units, Input/Screen are in pixels, and the
            // two only agree at the 1920 reference width. Convert before placing.
            float scale = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
            float pixelWidth = 392f * scale, pixelHeight = height * scale;

            // Every menu opens down the left edge rather than wherever it was
            // invoked. A popup over the middle of the map covers the thing the
            // order is about; the damage panel already owns the right side, so
            // the left stays clear for menus. The header names the contact, so
            // nothing is lost by not appearing under the cursor.
            // Pivot is bottom-left and the panel extends upward, so the bottom
            // edge must leave room for the whole height above it.
            popup.position = new Vector2(
                16f * scale,
                Mathf.Clamp(Theme.BarHeight * scale + 16f, 4f, Mathf.Max(4f, Screen.height - pixelHeight - 4f)));
            popupHeader.text = title;
        }

        private Button Row(string label, int row, Action action) =>
            MakeButton(popupContent, label, 8, (row - 1) * 38, 376, 34, action);

        internal void ClosePopup()
        {
            if (popup == null) return;
            popup.gameObject.SetActive(false);
            contextTarget = null;
            popupKey = null;
            // Closing the flight panel is what hands the map back to the ship.
            pinnedFlight = null;
            CommandState.SelectedFlight = null;
        }

        private void TogglePopup(string key, Action open)
        {
            if (PopupOpen && popupKey == key) { ClosePopup(); return; }
            ClosePopup();
            popupKey = key;
            open();
        }

        // ---- construction -------------------------------------------------

        private void Ensure()
        {
            if (root != null) return;

            foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
                if (text.font != null) { font = text.font; break; }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (ArgumentException) { }
                if (font == null) try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (ArgumentException) { }
            }
            if (font == null) throw new InvalidOperationException("No native UI font is available.");

            root = new GameObject("Naval Power", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0f;

            BuildOverlay();
            BuildBar();
            BuildDamagePanel();
            BuildPopup();
            BuildHover();
        }

        // Drawn first so map strokes sit behind the bar and popups.
        private void BuildOverlay()
        {
            var go = new GameObject("Map orders", typeof(RectTransform), typeof(MapOverlay));
            go.transform.SetParent(root.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            go.GetComponent<MapOverlay>().raycastTarget = false;
        }

        private Text emconPill, roePill, damagePill, trackPill;

        private void BuildBar()
        {
            bar = Box("Command bar", (RectTransform)root.transform, Theme.Surface);
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0);
            bar.pivot = new Vector2(0.5f, 0);
            bar.sizeDelta = new Vector2(0, Theme.BarHeight);
            bar.anchoredPosition = Vector2.zero;

            // A hairline along the top edge separates the bar from the world
            // without drawing a box around everything.
            RectTransform edge = Box("edge", bar, Theme.Dim(Theme.Accent, 0.5f));
            edge.anchorMin = new Vector2(0, 1); edge.anchorMax = new Vector2(1, 1);
            edge.pivot = new Vector2(0.5f, 1);
            edge.sizeDelta = new Vector2(0, 2f);
            edge.anchoredPosition = Vector2.zero;

            // Band A: identity and standing status.
            shipLabel = Label(bar, "", Theme.TitleSize, TextAnchor.MiddleLeft);
            Place(shipLabel.rectTransform, 16, 12, 330, 26);

            emconPill = Pill(bar, 356, 13, 150, 24);
            roePill = Pill(bar, 514, 13, 130, 24);
            damagePill = Pill(bar, 652, 13, 120, 24);
            trackPill = Pill(bar, 780, 13, 190, 24);

            statusLabel = Label(bar, "", Theme.CaptionSize, TextAnchor.MiddleLeft, Theme.TextMuted);
            Place(statusLabel.rectTransform, 986, 13, 560, 24);

            MakeButton(bar, "Flights", 1470, 11, 80, 28, () => TogglePopup("flights", FlightsMenu));
            MakeButton(bar, "Flight deck", 1556, 11, 116, 28, () => TogglePopup("deck", DeckMenu));
            MakeButton(bar, "Damage", 1678, 11, 92, 28, () => { damageOpen = !damageOpen; Refresh(); });
            MakeButton(bar, "Sensors", 1776, 11, 92, 28, () => TogglePopup("sensors", SensorMenu));

            // Band B: navigation.
            Section("Navigation", 16, 46, 1180);
            speedLabel = Label(bar, "", Theme.BodySize, TextAnchor.MiddleLeft);
            Place(speedLabel.rectTransform, 16, 64, 300, 28);
            speedSlider = MakeSlider(bar, 322, 70, 260, 16);
            speedSlider.onValueChanged.AddListener(value =>
            {
                if (updatingSlider || !CommandState.Active) return;
                NavigationOrders.SetOrderedSpeedKnots(CommandState.Ship, Mathf.Round(value * 10f) / 10f, out string reason);
                CommandState.Say(reason);
            });

            string[] presets = { "All stop", "1/3", "2/3", "Full", "Flank" };
            float[] fractions = { 0f, 1f / 3f, 2f / 3f, 0.9f, 1f };
            for (int i = 0; i < presets.Length; i++)
            {
                float fraction = fractions[i];
                MakeButton(bar, presets[i], 600 + i * 84, 64, 78, 28, () =>
                {
                    NavigationOrders.SetOrderedSpeedKnots(CommandState.Ship,
                        CommandableShip.MaximumSpeedKnots(CommandState.Ship) * fraction, out string reason);
                    CommandState.Say(reason);
                });
            }
            MakeButton(bar, "Clear route", 1028, 64, 108, 28, () =>
            {
                NavigationOrders.ClearWaypoints(CommandState.Ship, out string reason);
                CommandState.Say(reason);
            });

            // Band B right: engagement permissions, grouped away from movement.
            Section("Engagement", 1208, 46, 696);
            for (int i = 0; i < 3; i++)
            {
                var mode = (EngagementMode)i;
                roeButtons.Add(MakeButton(bar, EngagementPolicy.Describe(mode), 1208 + i * 142, 64, 134, 28, () =>
                {
                    EngagementPolicy.SetMode(CommandState.Ship, mode, out string reason);
                    CommandState.Say(reason);
                    Refresh();
                }));
            }
            Button cease = MakeButton(bar, "CEASE FIRE", 1640, 64, 128, 28, () =>
            {
                WeaponOrders.CeaseFire(CommandState.Ship, out string reason);
                CommandState.SelectedKey = null;
                CommandState.Say(reason);
            });
            cease.image.color = Theme.Dim(Theme.Bad, 0.55f);
            MakeButton(bar, "Exit", 1776, 64, 92, 28, () => MapCommand.Instance?.LeaveForNativeFlow());

            // Band C: weapons.
            Section("Weapons", 16, 98, 1888);
            weaponRow = Box("Weapons", bar, new Color(0, 0, 0, 0));
            Place(weaponRow, 16, 116, 1480, 32);

            for (int i = 0; i < Counts.Length; i++)
            {
                int count = Counts[i];
                quantityButtons.Add(MakeButton(bar, count == 1 ? "Single" : "x" + count,
                    1514 + i * 72, 116, 66, 32, () => { CommandState.Quantity = count; Refresh(); }));
            }

            feedbackLabel = Label(bar, "", Theme.CaptionSize, TextAnchor.MiddleLeft, Theme.TextMuted);
            Place(feedbackLabel.rectTransform, 16, 146, 1888, 22);
        }

        private void BuildDamagePanel()
        {
            damagePanel = Box("Damage control", (RectTransform)root.transform, Theme.Surface);
            damagePanel.anchorMin = new Vector2(1, 0); damagePanel.anchorMax = new Vector2(1, 1);
            damagePanel.pivot = new Vector2(1, 0);
            damagePanel.sizeDelta = new Vector2(520, -240);
            damagePanel.anchoredPosition = new Vector2(-12, 178);

            damageHeader = Label(damagePanel, "", 15, TextAnchor.UpperLeft, Theme.TextMuted);
            Place(damageHeader.rectTransform, 12, 8, 496, 76);
            damageHeader.verticalOverflow = VerticalWrapMode.Overflow;

            MakeButton(damagePanel, "DC: work whole ship", 12, 88, 240, 28, () =>
            {
                DamageControl.ClearPriorities(CommandState.Ship, out string reason);
                CommandState.Say(reason);
            });
            MakeButton(damagePanel, "Close", 400, 88, 108, 28, () => { damageOpen = false; Refresh(); });

            for (int i = 0; i < DamageRows; i++)
            {
                int index = i;
                Button button = MakeButton(damagePanel, "", 8, 122 + i * 34, 504, 30, () => ToggleCompartment(index));
                damageRows.Add(button);
                damageLabels.Add(button.GetComponentInChildren<Text>());
                damageLabels[i].alignment = TextAnchor.MiddleLeft;
            }
            damagePanel.gameObject.SetActive(false);
        }

        private void ToggleCompartment(int row)
        {
            if (row >= damageIds.Count) return;
            bool seal = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            string reason;
            if (seal) DamageControl.SealCompartment(CommandState.Ship, damageIds[row], out reason);
            else DamageControl.TogglePriority(CommandState.Ship, damageIds[row], out reason);
            CommandState.Say(reason);
            Refresh();
        }

        private void RefreshDamage()
        {
            if (damagePanel == null) return;
            damagePanel.gameObject.SetActive(damageOpen);
            if (!damageOpen) return;

            DamageSnapshot damage = DamageControl.GetSnapshot(CommandState.Ship);
            damageIds.Clear();

            float pool = damage.DamageControlPoolMax > 0.01f
                ? Mathf.Clamp01(damage.DamageControlPool / damage.DamageControlPoolMax) * 100f : 0f;
            damageHeader.text = damage.ShipState +
                "\nDamage control reserve " + pool.ToString("0") + "%" +
                "   ·   list " + damage.ListDegrees.ToString("0.0") + "°   ·   trim " + damage.TrimDegrees.ToString("0.0") + "°" +
                "\nFlooding " + damage.Flooding + "   ·   critical " + damage.Critical + "   ·   lost " + damage.Lost +
                "\nClick: prioritise damage control   ·   shift-click: seal off";

            // Worst first: a list of forty sound compartments helps nobody.
            var ordered = new List<CompartmentSnapshot>(damage.Compartments);
            ordered.Sort((a, b) => Severity(b).CompareTo(Severity(a)));

            int rows = 0;
            foreach (CompartmentSnapshot compartment in ordered)
            {
                if (rows >= DamageRows) break;
                if (Severity(compartment) <= 0f) continue;
                damageIds.Add(compartment.Id);
                string flooded = float.IsNaN(compartment.FloodedPercent) ? "" :
                    compartment.FloodedPercent > 0.5f ? "  ·  " + compartment.FloodedPercent.ToString("0") + "% flooded" : "";
                string integrity = float.IsNaN(compartment.IntegrityPercent) ? "" :
                    "  ·  hull " + compartment.IntegrityPercent.ToString("0") + "%";
                damageLabels[rows].text = (compartment.Priority ? "▲ " : "") + compartment.Name +
                    "  ·  " + compartment.State + integrity + flooded;
                damageLabels[rows].color =
                    compartment.Submerged || compartment.Detached || compartment.Removed ? Theme.TextFaint
                    : compartment.LeakRate > 0.01f ? Theme.Bad
                    : compartment.Sealed ? Theme.Warn
                    : Theme.Scale(compartment.IntegrityPercent);
                damageRows[rows].image.color = compartment.Priority ? Theme.AccentFill : Theme.Control;
                damageRows[rows].gameObject.SetActive(true);
                rows++;
            }
            if (rows == 0 && damageIds.Count == 0)
            {
                damageLabels[0].text = "No damage.";
                damageLabels[0].color = Theme.TextMuted;
                damageRows[0].image.color = Theme.Control;
                damageRows[0].gameObject.SetActive(true);
                rows = 1;
            }
            for (int i = rows; i < DamageRows; i++) damageRows[i].gameObject.SetActive(false);
        }

        // Ordering weight: flooding outranks structural damage, because
        // flooding is what capsizes the ship.
        private static float Severity(CompartmentSnapshot c)
        {
            if (c.Removed || c.Detached) return 20f;
            float score = 0f;
            if (c.LeakRate > 0.01f) score += 100f + c.LeakRate;
            if (!float.IsNaN(c.FloodedPercent)) score += c.FloodedPercent;
            if (c.Submerged) score += 60f;
            if (c.Sealed) score += 30f;
            if (!float.IsNaN(c.IntegrityPercent)) score += 100f - c.IntegrityPercent;
            return score;
        }

        private void BuildPopup()
        {
            popup = Box("Popup", (RectTransform)root.transform, Theme.SurfaceRaised);
            popup.anchorMin = popup.anchorMax = new Vector2(0, 0);
            popup.pivot = new Vector2(0, 0);
            popupHeader = Label(popup, "", Theme.CaptionSize + 1, TextAnchor.MiddleLeft, Theme.Accent);
            Place(popupHeader.rectTransform, 12, 9, 368, 26);
            RectTransform popupRule = Box("rule", popup, Theme.Divider);
            Place(popupRule, 8, 37, 376, 1f);

            popupContent = Box("Popup content", popup, new Color(0, 0, 0, 0));
            popupContent.anchorMin = new Vector2(0, 1); popupContent.anchorMax = new Vector2(1, 1);
            popupContent.pivot = new Vector2(0, 1);
            popupContent.anchoredPosition = new Vector2(0, -42);
            popupContent.sizeDelta = new Vector2(0, 0);
            popup.gameObject.SetActive(false);
        }

        private void BuildHover()
        {
            hover = Box("Hover", (RectTransform)root.transform, Theme.SurfaceRaised);
            RectTransform hoverEdge = Box("edge", hover, Theme.Dim(Theme.Accent, 0.65f));
            hoverEdge.anchorMin = new Vector2(0, 0); hoverEdge.anchorMax = new Vector2(0, 1);
            hoverEdge.pivot = new Vector2(0, 0.5f);
            hoverEdge.sizeDelta = new Vector2(2f, 0f);
            hoverEdge.anchoredPosition = Vector2.zero;
            hover.anchorMin = hover.anchorMax = new Vector2(0, 0);
            hover.pivot = new Vector2(0, 1);
            hoverText = Label(hover, "", 15, TextAnchor.UpperLeft);
            hoverText.rectTransform.anchorMin = Vector2.zero;
            hoverText.rectTransform.anchorMax = Vector2.one;
            hoverText.rectTransform.offsetMin = new Vector2(12, 9);
            hoverText.rectTransform.offsetMax = new Vector2(-12, -9);
            hoverText.verticalOverflow = VerticalWrapMode.Overflow;
            hover.gameObject.SetActive(false);
        }

        // ---- uGUI helpers -------------------------------------------------

        // A small uppercase caption above a group of controls. Grouping is
        // what makes a dense bar readable; boxes and borders just add noise.
        private void Section(string title, float x, float y, float width)
        {
            Text label = Label(bar, title.ToUpperInvariant(), Theme.LabelSize, TextAnchor.LowerLeft, Theme.TextFaint);
            Place(label.rectTransform, x, y, width, 14f);
            RectTransform rule = Box("rule", bar, Theme.Divider);
            Place(rule, x, y + 15f, width, 1f);
        }

        // Status as colour plus a word, never colour alone.
        private Text Pill(RectTransform parent, float x, float y, float width, float height)
        {
            RectTransform back = Box("pill", parent, Theme.Dim(Theme.Text, 0.06f));
            Place(back, x, y, width, height);
            Text text = Label(back, "", Theme.CaptionSize, TextAnchor.MiddleCenter, Theme.Text);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero; text.rectTransform.offsetMax = Vector2.zero;
            return text;
        }

        private static void SetPill(Text pill, string value, Color color)
        {
            pill.text = value;
            pill.color = color;
            Image back = pill.rectTransform.parent.GetComponent<Image>();
            if (back != null) back.color = Theme.Dim(color, 0.14f);
        }

        private RectTransform Box(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        private Text Label(RectTransform parent, string value, int size, TextAnchor alignment) =>
            Label(parent, value, size, alignment, Theme.Text);

        private Text Label(RectTransform parent, string value, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font; text.fontSize = size; text.color = color; text.text = value; text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private Button MakeButton(RectTransform parent, string label, float x, float y, float width, float height, Action action)
        {
            RectTransform rect = Box(string.IsNullOrEmpty(label) ? "Button" : label, parent, Theme.Control);
            Place(rect, x, y, width, height);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.22f, 1.22f, 1.22f);
            colors.pressedColor = new Color(0.78f, 0.92f, 0.96f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => { if (CommandState.Active) action(); });
            Text text = Label(rect, label, 15, TextAnchor.MiddleCenter);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(6, 0); text.rectTransform.offsetMax = new Vector2(-6, 0);
            return button;
        }

        private Slider MakeSlider(RectTransform parent, float x, float y, float width, float height)
        {
            RectTransform rect = Box("Ordered speed", parent, new Color(0.25f, 0.3f, 0.34f));
            Place(rect, x, y, width, height);
            var slider = rect.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            var handleArea = new GameObject("Handle area", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(rect, false);
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(6, 0); handleArea.offsetMax = new Vector2(-6, 0);
            RectTransform handle = Box("Handle", handleArea, new Color(0.3f, 0.8f, 0.86f));
            handle.sizeDelta = new Vector2(13, 26);
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            return slider;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private void OnDestroy() { if (root != null) Destroy(root); }
    }
}

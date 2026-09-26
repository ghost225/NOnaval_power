using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // The right-click menu: orders about one thing. What it offers depends
    // on what was clicked -- your own ship, a friendly ship, one of your
    // flights, a hostile on a live track, a stale one, a passive bearing, or
    // a bare point on the map -- because the one menu for everything offered
    // "engage" on your own escorts and window shortcuts on an enemy's.
    //
    // It opens beside the cursor and closes once an order is given, because
    // the order is the point of opening it.
    internal sealed partial class CommandUi
    {
        private enum Relation { Own, FriendlyShip, OurFlight, Friendly, Hostile, Stale }

        private Unit contextTarget;
        private bool contextAppend;
        private Action<Surface> contextRoot;

        internal void OpenContext(Vector2 screenPosition, Unit target, bool append)
        {
            Ensure();
            contextTarget = target;
            contextAppend = append;
            ShowContext(UnitPage, screenPosition);
        }

        internal void OpenEsmContext(Vector2 screenPosition, EsmContact contact)
        {
            Ensure();
            contextTarget = null;
            ShowContext(s => EsmPage(s, contact), screenPosition);
        }

        internal void OpenPointContext(Vector2 screenPosition, GlobalPosition point)
        {
            Ensure();
            contextTarget = null;
            ShowContext(s => PointPage(s, point), screenPosition);
        }

        // Down and right of the cursor, kept on screen, and never over the
        // docked map: a menu about something on the map must not cover it.
        private void ShowContext(Action<Surface> page, Vector2 screenPosition)
        {
            contextRoot = page;
            context.Show(page);
            float scale = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
            Vector2 at = screenPosition / scale + new Vector2(14f, -10f);
            if (MapDocked && mapWindow != null && mapWindow.IsOpen)
            {
                var corners = new Vector3[4];
                mapWindow.Panel.GetWorldCorners(corners);
                Rect map = Rect.MinMaxRect(corners[0].x / scale, corners[0].y / scale, corners[2].x / scale, corners[2].y / scale);
                if (at.x < map.xMax && at.x + context.Width > map.xMin) at.x = map.xMax + 8f;
            }
            context.PlaceAt(at);
        }

        private void Back(Surface s) => s.Row("Back", () => s.Show(contextRoot ?? UnitPage));

        // ---- what was clicked -------------------------------------------------

        private static Relation RelationOf(Unit unit)
        {
            FactionHQ hq = CommandState.Hq;
            if (unit == CommandState.Ship) return Relation.Own;
            if (hq != null && unit.NetworkHQ == hq)
            {
                if (unit is Aircraft aircraft && FlightOrders.Of(aircraft) != null) return Relation.OurFlight;
                if (unit is Ship ship && CommandableShip.CanCommand(ship, out _)) return Relation.FriendlyShip;
                return Relation.Friendly;
            }
            return TrackReadout.IsCurrent(unit) ? Relation.Hostile : Relation.Stale;
        }

        private static string NameOf(Unit unit) => unit.definition?.unitName ?? unit.name;

        // Where the faction believes it is: exact for our own, the track otherwise.
        private static GlobalPosition KnownPosition(Unit unit)
        {
            FactionHQ hq = CommandState.Hq;
            if (hq != null && unit.NetworkHQ != hq && hq.TryGetKnownPosition(unit, out GlobalPosition known)) return known;
            return unit.GlobalPosition();
        }

        private static string BearingRange(GlobalPosition to)
        {
            Vector3 offset = to - CommandState.PostPosition;
            offset.y = 0f;
            float bearing = (Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg + 360f) % 360f;
            return bearing.ToString("000") + "°  " + UnitConverter.DistanceReading(offset.magnitude);
        }

        private void UnitPage(Surface s)
        {
            Unit unit = contextTarget ?? CommandState.Ship;
            if (unit == null) { PointlessPage(s); return; }
            if (unit.disabled && unit != CommandState.Ship)
            {
                s.Title(NameOf(unit).ToUpperInvariant());
                s.Info("Destroyed.", Theme.TextMuted);
                return;
            }
            switch (RelationOf(unit))
            {
                case Relation.Own: OwnShipPage(s); break;
                case Relation.FriendlyShip: FriendlyShipPage(s, (Ship)unit); break;
                case Relation.OurFlight: OurFlightPage(s, FlightOrders.Of((Aircraft)unit)); break;
                case Relation.Friendly: FriendlyPage(s, unit); break;
                case Relation.Hostile: HostilePage(s, unit); break;
                default: StalePage(s, unit); break;
            }
        }

        private static void PointlessPage(Surface s)
        {
            s.Title("NOTHING SELECTED");
            s.Info("Right-click a unit, or ctrl-right-click the map.", Theme.TextMuted);
        }

        // ---- your own ship -----------------------------------------------------

        private void OwnShipPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            NavigationSnapshot nav = NavigationOrders.GetSnapshot(ship);
            int legs = nav?.Waypoints != null ? nav.Waypoints.Length : 0;
            s.Title(NameOf(ship).ToUpperInvariant() + "  ·  your ship");
            if (nav != null)
                s.Info(Speed(nav.ActualSpeedKnots) + "  ·  course " +
                    ((ship.transform.eulerAngles.y + 360f) % 360f).ToString("000") + "°" +
                    (legs > 0 ? "  ·  " + legs + " leg(s) queued" : ""), Theme.TextMuted);

            s.Row("Speed…", () => s.Show(SpeedPage));
            s.Row("Hold position  ·  all stop, clear the route", () =>
            {
                NavigationOrders.ClearWaypoints(ship, out _);
                NavigationOrders.SetOrderedSpeedKnots(ship, 0f, out string reason);
                CommandState.Say(reason);
                s.Close();
            });
            if (legs > 0)
                s.Row("Clear the route  ·  " + legs + " leg(s)", () =>
                {
                    NavigationOrders.ClearWaypoints(ship, out string reason);
                    CommandState.Say(reason);
                    s.Close();
                });
            s.Row("Arm a weapon for a manual shot…", () => s.Show(EngagePage));
            s.Row("Rules of engagement  ·  " + EngagementPolicy.Describe(EngagementPolicy.GetMode(ship)) + "…",
                () => s.Show(RoePage));
            bool silent = Sensors.IsSilent(ship);
            s.Row(silent ? "Radiate  ·  sensors back on" : "Go silent  ·  EMCON", () =>
            {
                Sensors.SetAllEmitting(ship, silent, out string reason);
                CommandState.Say(reason);
                s.Close();
            });
            if (CommandState.Airfield != null)
                s.Row("Launch an aircraft…", () => { Open("air", DeckPage); s.Close(); });
            FeedRow(s, ship);
            Button cease = s.Row("CEASE FIRE", () =>
            {
                WeaponOrders.CeaseFire(ship, out string reason);
                CommandState.SelectedKey = null;
                CommandState.Say(reason);
                s.Close();
            });
            cease.image.color = Theme.Dim(Theme.Bad, 0.55f);
        }

        private void SpeedPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            if (ship == null) return;
            NavigationSnapshot nav = NavigationOrders.GetSnapshot(ship);
            s.Title("SPEED  ·  " + NameOf(ship).ToUpperInvariant());
            if (nav != null)
                s.Info("Actual " + Speed(nav.ActualSpeedKnots) + "  ·  ordered " + Speed(nav.OrderedSpeedKnots), Theme.TextMuted);
            float max = CommandableShip.MaximumSpeedKnots(ship);
            float[] fractions = { 0f, 1f / 3f, 2f / 3f, 0.9f, 1f };
            Button[] presets = s.Group(new[] { "Stop", "1/3", "2/3", "Full", "Flank" }, i =>
            {
                NavigationOrders.SetOrderedSpeedKnots(ship, max * fractions[i], out string reason);
                CommandState.Say(reason);
            });
            for (int i = 0; i < fractions.Length; i++)
                if (nav != null && max > 0.1f && Mathf.Abs(nav.OrderedSpeedKnots - max * fractions[i]) < 0.6f)
                    presets[i].image.color = Theme.AccentFill;
            s.Row("Astern  ·  back one third", () =>
            {
                NavigationOrders.SetOrderedSpeedKnots(ship, -max / 3f, out string reason);
                CommandState.Say(reason);
            });
            Back(s);
        }

        private void RoePage(Surface s)
        {
            Ship ship = CommandState.Ship;
            if (ship == null) return;
            EngagementMode current = EngagementPolicy.GetMode(ship);
            s.Title("RULES OF ENGAGEMENT");
            string[] detail =
            {
                "automatic engagement unrestricted",
                "inbound weapons, and units that have fired on us",
                "point defence against inbound weapons only"
            };
            for (int i = 0; i < 3; i++)
            {
                var mode = (EngagementMode)i;
                Button row = s.Row(EngagementPolicy.Describe(mode) + "  ·  " + detail[i], () =>
                {
                    EngagementPolicy.SetMode(ship, mode, out string reason);
                    CommandState.Say(reason);
                    s.Close();
                });
                if (mode == current) row.image.color = Theme.AccentFill;
            }
            Back(s);
        }

        // ---- friendly units ------------------------------------------------------

        private void FriendlyShipPage(Surface s, Ship ship)
        {
            s.Title(NameOf(ship).ToUpperInvariant() + "  ·  friendly");
            s.Info(BearingRange(ship.GlobalPosition()), Theme.TextMuted);
            s.Row("Take command of it", () =>
            {
                s.Close();
                SceneSingleton<CameraStateManager>.i?.SetFollowingUnit(ship);
            });
            CoverRow(s, ship);
            FeedRow(s, ship);
        }

        private void OurFlightPage(Surface s, Flight flight)
        {
            if (flight == null || !Alive(s, flight)) return;
            s.Title(flight.Name.ToUpperInvariant() + "  ·  " + flight.TypeName);
            s.Info((flight.Status ?? flight.Describe()) + "  ·  " + flight.FuelPercent.ToString("0") + "% fuel", Theme.TextMuted);
            s.Row("Orders…", () => { OpenFlight(flight); s.Close(); });
            s.Row("Hold here", () =>
            {
                WingOrders.SetArea(flight, flight.Aircraft.GlobalPosition(), flight.OrbitRadius);
                CommandState.Say(flight.Name + " · holding overhead");
                s.Close();
            });
            s.Row("Weapons free  ·  hand to the AI", () =>
            {
                WingOrders.Engage(flight);
                CommandState.Say(flight.Name + " · weapons free");
                s.Close();
            });
            s.Row("Return to base", () =>
            {
                WingOrders.ReturnToBase(flight);
                CommandState.Say(flight.Name + " · recovering");
                s.Close();
            });
            s.Row("Take the controls", () =>
            {
                s.Close();
                if (!PilotSeat.Take(flight, out string why)) CommandState.Say(flight.Name + " · " + why);
            });
            FeedRow(s, flight.Aircraft);
        }

        private void FriendlyPage(Surface s, Unit unit)
        {
            s.Title(NameOf(unit).ToUpperInvariant() + "  ·  friendly");
            s.Info(BearingRange(unit.GlobalPosition()), Theme.TextMuted);
            CoverRow(s, unit);
            FeedRow(s, unit);
        }

        // A flight sent to work an area on it: cover for a convoy, a strike
        // package's escort, a ship under air attack.
        private void CoverRow(Surface s, Unit unit)
        {
            if (FlightOrders.All().Count == 0) return;
            s.Row("Cover it with a flight…", () => s.Show(x => FlightsToPage(x,
                "COVER " + NameOf(unit).ToUpperInvariant(), unit.GlobalPosition(), 0f, "covering " + NameOf(unit))));
        }

        // ---- hostiles --------------------------------------------------------------

        private void HostilePage(Surface s, Unit target)
        {
            string name = NameOf(target);
            bool hostile = target.NetworkHQ != null;
            s.Title(name.ToUpperInvariant() + (hostile ? "  ·  hostile" : "  ·  unknown"));
            s.Info("Tracked  ·  " + BearingRange(KnownPosition(target)), Theme.TextMuted);

            if (CommandState.Ship != null) s.Row("Engage with…", () => s.Show(EngagePage));
            if (FlightOrders.All().Count > 0)
            {
                int capable = FlightOrders.CapableOf(target).Count;
                s.Row("Strike with flights…  ·  " + capable + " capable", () => s.Show(x => StrikePage(x, target)));
            }
            if (FlightOrders.Jammers().Count > 0)
                s.Row("Jam it…", () => s.Show(x => JamPage(x, target)));
            if (CommandState.Ship != null)
            {
                s.Row("Close the range  ·  steer toward it", () => SteerRelative(s, KnownPosition(target), toward: true));
                s.Row("Open the range  ·  steer away", () => SteerRelative(s, KnownPosition(target), toward: false));
            }
            FeedRow(s, target);
        }

        // Last seen, not seen: nothing here fires, strikes or watches on
        // knowledge the faction does not have. What it can do is go and look.
        private void StalePage(Surface s, Unit target)
        {
            FactionHQ hq = CommandState.Hq;
            TrackingInfo track = hq?.GetTrackingData(target.persistentID);
            float age = track != null ? Time.timeSinceLevelLoad - track.lastSpottedTime : -1f;
            GlobalPosition last = KnownPosition(target);
            s.Title(NameOf(target).ToUpperInvariant() + "  ·  stale track");
            s.Info((age >= 0f ? "Last seen " + age.ToString("0") + " s ago" : "No track") + "  ·  " +
                BearingRange(last), Theme.TextMuted);
            if (FlightOrders.All().Count > 0)
                s.Row("Send a flight to search…", () => s.Show(x => FlightsToPage(x,
                    "SEARCH FOR " + NameOf(target).ToUpperInvariant(), last, 5000f, "searching")));
            if (CommandState.Ship != null)
                s.Row("Steer toward where it was", () => SteerRelative(s, last, toward: true));
            s.Info("No firing solution or camera on a stale track.", Theme.TextFaint);
        }

        private void SteerRelative(Surface s, GlobalPosition target, bool toward)
        {
            Ship ship = CommandState.Ship;
            if (ship == null) return;
            GlobalPosition goal = target;
            if (!toward)
            {
                Vector3 away = ship.GlobalPosition() - target;
                away.y = 0f;
                if (away.sqrMagnitude < 1f) away = -ship.transform.forward;
                goal = ship.GlobalPosition() + away.normalized * 15000f;
            }
            NavigationOrders.ReplaceWaypoint(ship, goal, out string reason);
            CommandState.Say(reason);
            s.Close();
        }

        private void EngagePage(Surface s)
        {
            Ship ship = CommandState.Ship;
            Unit target = contextTarget != null && RelationOf(contextTarget) == Relation.Hostile ? contextTarget : null;
            if (ship == null) return;
            float range = target != null ? Vector3.Distance(KnownPosition(target).AsVector3(), ship.GlobalPosition().AsVector3()) : 0f;
            s.Title(target != null ? "ENGAGE " + NameOf(target).ToUpperInvariant() : "ARM A WEAPON");
            if (target == null) s.Info("Then right-click a contact to fire.", Theme.TextMuted);

            foreach (WeaponCommandInfo weapon in WeaponOrders.GetWeapons(ship))
            {
                string key = weapon.Key;
                bool capable = target == null || WeaponOrders.Opportunity(
                    WeaponOrders.StationsFor(ship, key).FirstOrDefault()?.WeaponInfo, target) > 0.01f;
                string reach = target == null ? ""
                    : range > weapon.MaxRange ? "  ·  out of range"
                    : range < weapon.MinRange ? "  ·  too close" : "  ·  in range";
                Button row = s.Row(weapon.Name + "  ·  " + weapon.Readiness +
                    (weapon.Continuous ? "" : "  ·  " + weapon.Ammo + " left") +
                    (capable ? reach : "  ·  ineffective"), () =>
                {
                    CommandState.SelectedKey = key;
                    if (target == null) CommandState.Say(weapon.Name + " armed · right-click a contact to fire");
                    else
                    {
                        WeaponOrders.Attack(ship, key, target, CommandState.Quantity, out string reason, contextAppend);
                        CommandState.Say(reason);
                    }
                    s.Close();
                });
                if (!capable) row.GetComponentInChildren<Text>().color = Theme.TextFaint;
                else if (target != null && (range > weapon.MaxRange || range < weapon.MinRange))
                    row.GetComponentInChildren<Text>().color = Theme.TextMuted;
            }
            s.Info("Salvo " + (CommandState.Quantity == 1 ? "single" : "×" + CommandState.Quantity) +
                "  ·  change it in the weapons window", Theme.TextFaint);
            Back(s);
        }

        // ---- flights, sent somewhere -------------------------------------------------

        private void StrikePage(Surface s, Unit target)
        {
            List<Flight> capable = FlightOrders.CapableOf(target);
            string name = NameOf(target);
            s.Title("STRIKE " + name.ToUpperInvariant());
            if (capable.Count > 1)
                s.Row("ALL CAPABLE  ·  " + capable.Count + " flights", () =>
                {
                    foreach (Flight flight in capable) WingOrders.Strike(flight, target);
                    CommandState.Say(capable.Count + " flights striking " + name);
                    s.Close();
                });
            if (capable.Count == 0) s.Info("No flight carries anything that can hurt it.", Theme.TextMuted);
            foreach (Flight flight in FlightOrders.All())
            {
                Flight shown = flight;
                bool able = capable.Contains(flight);
                Button row = s.Row(flight.Name + "  ·  " + (able ? flight.StoresSummary : "cannot engage this"), () =>
                {
                    if (!able) { CommandState.Say(shown.Name + " carries nothing that can hurt " + name); return; }
                    s.Show(x => StrikeWeaponPage(x, shown, target));
                });
                if (!able) row.GetComponentInChildren<Text>().color = Theme.TextFaint;
            }
            Back(s);
        }

        // Which store to spend on this target. Left to the analyser a flight
        // reaches for whatever scores highest, which is not always what you
        // want spent on a truck.
        private void StrikeWeaponPage(Surface s, Flight flight, Unit target)
        {
            string name = NameOf(target);
            s.Title(flight.Name.ToUpperInvariant() + "  ·  strike " + name);
            s.Row("Best available  ·  let the flight choose", () =>
            {
                WingOrders.Strike(flight, target);
                CommandState.Say(flight.Name + " striking " + name);
                s.Close();
            });
            foreach (WeaponStation station in FlightOrders.ArmedStations(flight.Aircraft))
            {
                WeaponInfo info = station.WeaponInfo;
                float worth = WeaponOrders.Opportunity(info, target);
                bool releasable = FlightOrders.CanReleaseNow(flight.Aircraft, info, target);
                Button row = s.Row(info.weaponName + "  ·  " + station.Ammo + " left  ·  " +
                    (worth > 0.01f ? "effective " + worth.ToString("0.00") : "poor match") +
                    (releasable ? "" : "  ·  must close for a track"), () =>
                {
                    WingOrders.Strike(flight, target, info.name);
                    CommandState.Say(flight.Name + " striking " + name + " with " + info.weaponName);
                    s.Close();
                });
                if (worth <= 0.01f) row.GetComponentInChildren<Text>().color = Theme.TextMuted;
            }
            s.Row("Back", () => s.Show(x => StrikePage(x, target)));
        }

        private void JamPage(Surface s, Unit target)
        {
            s.Title("JAM " + NameOf(target).ToUpperInvariant());
            foreach (Flight flight in FlightOrders.Jammers())
            {
                Flight shown = flight;
                s.Row(flight.Name + "  ·  " + flight.TypeName, () =>
                {
                    WingOrders.Jam(shown, target);
                    CommandState.Say(shown.Name + " jamming " + NameOf(target));
                    s.Close();
                });
            }
            Back(s);
        }

        // Any flight, sent to work an area: covering a friendly, searching for
        // a lost contact, investigating a bearing, or a bare point on the map.
        private void FlightsToPage(Surface s, string title, GlobalPosition where, float radius, string doing)
        {
            s.Title(title);
            s.Info(BearingRange(where), Theme.TextMuted);
            foreach (Flight flight in FlightOrders.All())
            {
                Flight shown = flight;
                s.Row(flight.Name + "  ·  " + flight.TypeName + "  ·  " + flight.FuelPercent.ToString("0") + "%", () =>
                {
                    WingOrders.SetArea(shown, where, radius > 0f ? Mathf.Max(radius, shown.OrbitRadius) : shown.OrbitRadius);
                    CommandState.Say(shown.Name + " · " + doing);
                    s.Close();
                });
            }
            Back(s);
        }

        private void CargoToPage(Surface s, GlobalPosition where, bool airdrop)
        {
            s.Title((airdrop ? "AIRDROP" : "LAND CARGO") + " HERE");
            List<Flight> carriers = FlightOrders.Carriers();
            if (carriers.Count == 0) s.Info("No flight is carrying cargo.", Theme.TextMuted);
            foreach (Flight flight in carriers)
            {
                Flight shown = flight;
                s.Row(flight.Name + "  ·  " + flight.TypeName, () =>
                {
                    WingOrders.Deliver(shown, where, airdrop);
                    CommandState.Say(shown.Name + " · " + (airdrop ? "airdropping" : "landing cargo") + " at the marked point");
                    s.Close();
                });
            }
            Back(s);
        }

        // ---- a point on the map --------------------------------------------------------

        private void PointPage(Surface s, GlobalPosition point)
        {
            s.Title("MAP POINT  ·  " + BearingRange(point));
            Ship ship = CommandState.Ship;
            if (ship != null)
            {
                s.Row("Steer here", () =>
                {
                    NavigationOrders.ReplaceWaypoint(ship, point, out string reason);
                    CommandState.Say(reason);
                    s.Close();
                });
                s.Row("Add a leg here", () =>
                {
                    NavigationOrders.AppendWaypoint(ship, point, out string reason);
                    CommandState.Say(reason);
                    s.Close();
                });
            }
            if (FlightOrders.All().Count > 0)
                s.Row("Send a flight to work this area…",
                    () => s.Show(x => FlightsToPage(x, "SEND A FLIGHT", point, 0f, "task area set")));
            if (FlightOrders.Carriers().Count > 0)
            {
                s.Row("Land cargo here…", () => s.Show(x => CargoToPage(x, point, false)));
                s.Row("Airdrop cargo here…", () => s.Show(x => CargoToPage(x, point, true)));
            }
            if (ship == null && FlightOrders.All().Count == 0)
                s.Info("Nothing to send. Launch from AIR.", Theme.TextMuted);
        }

        // ---- passive bearings ------------------------------------------------------------

        private void EsmPage(Surface s, EsmContact contact)
        {
            s.Title("ESM " + contact.Id + "  ·  " + contact.Type);
            s.Info("Bearing " + contact.BearingDegrees.ToString("000") + "°  ·  about " +
                UnitConverter.DistanceReading(contact.RangeMetres) + (contact.Stale ? "  ·  stale" : ""), Theme.TextMuted);
            Ship ship = CommandState.Ship;
            if (ship != null)
            {
                s.Row("Steer toward the emitter", () =>
                {
                    NavigationOrders.ReplaceWaypoint(ship, contact.Position, out string reason);
                    CommandState.Say(reason);
                    s.Close();
                });
                s.Row("Add a leg toward it", () =>
                {
                    NavigationOrders.AppendWaypoint(ship, contact.Position, out string reason);
                    CommandState.Say(reason);
                    s.Close();
                });
            }
            if (FlightOrders.All().Count > 0)
                s.Row("Send a flight to investigate…", () => s.Show(x => FlightsToPage(x,
                    "INVESTIGATE ESM " + contact.Id, contact.Position,
                    Mathf.Max(contact.RadialUncertaintyMetres, 3000f), "investigating ESM " + contact.Id)));
        }

        // ---- shared rows ------------------------------------------------------------------

        private void FeedRow(Surface s, Unit unit)
        {
            if (feedView == null || unit == null) return;
            bool pinned = feedView.IsPinned(unit);
            s.Row(pinned ? "Close its camera feed" : "Pin a camera feed on it", () =>
            {
                feedView.Pin(unit, out string reason);
                CommandState.Say(reason);
                s.Close();
            });
        }
    }
}

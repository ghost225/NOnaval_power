using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // The right-click menu: orders about one contact. Unlike the standing
    // windows it closes once an order is given, because the order is the point
    // of opening it.
    internal sealed partial class CommandUi
    {
        private Unit contextTarget;
        private bool contextAppend;

        internal void OpenContext(Vector2 screenPosition, Unit target, bool append)
        {
            Ensure();
            contextTarget = target;
            contextAppend = append;
            ShowContext(ContextPage);
        }

        internal void OpenEsmContext(Vector2 screenPosition, EsmContact contact)
        {
            Ensure();
            contextTarget = null;
            ShowContext(s => EsmPage(s, contact));
        }

        // Down the left edge rather than under the cursor: a menu over the
        // middle of the map covers the thing the order is about, and the header
        // names the contact, so nothing is lost by not appearing beside it.
        private void ShowContext(System.Action<Surface> page)
        {
            bool wasOpen = context.IsOpen;
            context.Show(page);
            if (!wasOpen) context.Place(new Vector2(16f, menuLayer.rect.height - Surface.ReservedTop));
        }

        private void ContextPage(Surface s)
        {
            Unit target = contextTarget;
            s.Title(target != null
                ? (target.definition?.unitName ?? target.name).ToUpperInvariant()
                : CommandState.PostName.ToUpperInvariant());

            bool ship = CommandState.Ship != null;
            if (ship) s.Row("Engage with…", () => s.Show(EngagePage));
            if (target != null && feedView != null)
                s.Row(feedView.IsPinned(target) ? "Close its camera feed" : "Pin a camera feed on it", () =>
                {
                    feedView.Pin(target, out string reason);
                    CommandState.Say(reason);
                    s.Close();
                });
            if (target != null && FlightOrders.All().Count > 0)
                s.Row("Strike with flights…", () => s.Show(x => StrikePage(x, target)));

            // Shortcuts to the standing windows, for when the menu is where your
            // hand already is.
            if (ship)
            {
                s.Row("Navigation…", () => { Open("nav", NavigationPage); s.Close(); });
                s.Row("Rules of engagement…", () => { Open("roe", EngagementPage); s.Close(); });
                s.Row("Sensors / EMCON…", () => { Open("sns", SensorsPage); s.Close(); });
            }
            s.Row("Air operations…", () => { Open("air", AirPage); s.Close(); });
            if (!ship) return;
            Button cease = s.Row("CEASE FIRE", () =>
            {
                WeaponOrders.CeaseFire(CommandState.Ship, out string reason);
                CommandState.SelectedKey = null;
                CommandState.Say(reason);
                s.Close();
            });
            cease.image.color = Theme.Dim(Theme.Bad, 0.55f);
        }

        private void EngagePage(Surface s)
        {
            Ship ship = CommandState.Ship;
            Unit target = contextTarget;
            if (ship == null) return;
            s.Title(target != null ? "ENGAGE " + (target.definition?.unitName ?? target.name).ToUpperInvariant()
                : "SELECT WEAPON");

            foreach (WeaponCommandInfo weapon in WeaponOrders.GetWeapons(ship))
            {
                string key = weapon.Key;
                bool capable = target == null || WeaponOrders.Opportunity(
                    WeaponOrders.StationsFor(ship, key).FirstOrDefault()?.WeaponInfo, target) > 0.01f;
                Button row = s.Row(weapon.Name + "   ·   " + weapon.Readiness +
                    (weapon.Continuous ? "  ·  continuous" : "  ·  " + weapon.Ammo + " remaining") +
                    (capable ? "" : "  ·  ineffective"), () =>
                {
                    CommandState.SelectedKey = key;
                    if (target == null)
                    {
                        CommandState.Say("Right-click a contact to engage");
                    }
                    else
                    {
                        WeaponOrders.Attack(CommandState.Ship, key, target, CommandState.Quantity,
                            out string reason, contextAppend);
                        CommandState.Say(reason);
                    }
                    s.Close();
                });
                if (!capable) row.GetComponentInChildren<Text>().color = Theme.TextFaint;
            }
            s.Info("Salvo " + (CommandState.Quantity == 1 ? "single" : "×" + CommandState.Quantity) +
                "   ·   change it in the weapons window");
            s.Row("Back", () => s.Show(ContextPage));
        }

        // ---- strikes by flights ----------------------------------------------

        private void StrikePage(Surface s, Unit target)
        {
            List<Flight> capable = FlightOrders.CapableOf(target);
            List<Flight> all = FlightOrders.All();
            string name = target.definition?.unitName ?? target.name;
            s.Title("STRIKE " + name.ToUpperInvariant());

            s.Row(capable.Count > 0 ? "ALL CAPABLE  ·  " + capable.Count + " flight(s)" : "No flight can hurt this target", () =>
            {
                foreach (Flight flight in capable) FlightOrders.Strike(flight, target);
                CommandState.Say(capable.Count + " flight(s) striking " + name);
                s.Close();
            });
            foreach (Flight flight in all)
            {
                Flight shown = flight;
                bool able = capable.Contains(flight);
                Button row = s.Row(flight.Name + "   ·   " + (able ? flight.Describe() : "cannot engage this target"), () =>
                {
                    if (!able) { CommandState.Say(shown.Name + " carries nothing that can hurt " + name); return; }
                    s.Show(x => StrikeWeaponPage(x, shown, target));
                });
                if (!able) row.GetComponentInChildren<Text>().color = Theme.TextFaint;
            }
            s.Row("Back", () => s.Show(ContextPage));
        }

        // Which store to spend on this target. Left to the analyser a flight
        // reaches for whatever scores highest, which is not always what you
        // want spent on a truck.
        private void StrikeWeaponPage(Surface s, Flight flight, Unit target)
        {
            string name = target.definition?.unitName ?? target.name;
            s.Title(flight.Name.ToUpperInvariant() + "  ·  strike " + name);
            s.Row("Best available  ·  let the flight choose", () =>
            {
                FlightOrders.Strike(flight, target);
                CommandState.Say(flight.Name + " striking " + name);
                s.Close();
            });
            foreach (WeaponStation station in FlightOrders.ArmedStations(flight.Aircraft))
            {
                WeaponInfo info = station.WeaponInfo;
                float worth = WeaponOrders.Opportunity(info, target);
                bool releasable = FlightOrders.CanReleaseNow(flight.Aircraft, info, target);
                Button row = s.Row(info.weaponName + "  ·  " + station.Ammo + " remaining  ·  " +
                    (worth > 0.01f ? "effective " + worth.ToString("0.00") : "poor match") +
                    (releasable ? "" : "  ·  needs to close for a track"), () =>
                {
                    FlightOrders.Strike(flight, target, info.name);
                    CommandState.Say(flight.Name + " striking " + name + " with " + info.weaponName);
                    s.Close();
                });
                if (worth <= 0.01f) row.GetComponentInChildren<Text>().color = Theme.TextMuted;
            }
            s.Row("Back", () => s.Show(x => StrikePage(x, target)));
        }

        // ---- passive bearings ------------------------------------------------

        private void EsmPage(Surface s, EsmContact contact)
        {
            s.Title("ESM " + contact.Id + "  ·  " + contact.Type);
            s.Row("Steer toward this bearing", () =>
            {
                NavigationOrders.ReplaceWaypoint(CommandState.Ship, contact.Position, out string reason);
                CommandState.Say(reason);
                s.Close();
            });
            s.Row("Append a leg toward it", () =>
            {
                NavigationOrders.AppendWaypoint(CommandState.Ship, contact.Position, out string reason);
                CommandState.Say(reason);
                s.Close();
            });
        }
    }
}

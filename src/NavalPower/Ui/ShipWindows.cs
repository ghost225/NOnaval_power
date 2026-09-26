using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // The ship's own windows: how it moves, what it shoots with, when it may
    // shoot, what it radiates, what is broken, and what it needs restocking.
    internal sealed partial class CommandUi
    {
        private static readonly int[] Salvo = { 1, 2, 4, 8, 16 };

        // ---- navigation ------------------------------------------------------

        private void NavigationPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            if (ship == null) return;
            NavigationSnapshot nav = NavigationOrders.GetSnapshot(ship);
            s.Title("NAVIGATION  ·  " + ShipNames.Of(ship));
            if (nav == null) { s.Info("No navigation for this ship."); return; }

            int legs = nav.Waypoints != null ? nav.Waypoints.Length : 0;
            s.Info("Actual " + Speed(nav.ActualSpeedKnots) + "   ·   ordered " + Speed(nav.OrderedSpeedKnots) +
                (legs > 0 ? "   ·   " + legs + " leg(s) queued" : ""), Theme.Text);
            s.SliderRow(nav.MinimumSpeedKnots, nav.MaximumSpeedKnots, nav.OrderedSpeedKnots, value =>
            {
                NavigationOrders.SetOrderedSpeedKnots(CommandState.Ship, Mathf.Round(value * 10f) / 10f, out string reason);
                CommandState.Say(reason);
            });

            float[] fractions = { 0f, 1f / 3f, 2f / 3f, 0.9f, 1f };
            Button[] presets = s.Group(new[] { "Stop", "1/3", "2/3", "Full", "Flank" }, i =>
            {
                NavigationOrders.SetOrderedSpeedKnots(CommandState.Ship,
                    CommandableShip.MaximumSpeedKnots(CommandState.Ship) * fractions[i], out string reason);
                CommandState.Say(reason);
            });
            // The preset that matches the order, lit, so the row doubles as a
            // readout of what was asked for.
            float max = CommandableShip.MaximumSpeedKnots(ship);
            for (int i = 0; i < fractions.Length; i++)
                if (max > 0.1f && Mathf.Abs(nav.OrderedSpeedKnots - max * fractions[i]) < 0.6f)
                    presets[i].image.color = Theme.AccentFill;

            s.Row("Astern  ·  back one third", () =>
            {
                NavigationOrders.SetOrderedSpeedKnots(CommandState.Ship,
                    -CommandableShip.MaximumSpeedKnots(CommandState.Ship) / 3f, out string reason);
                CommandState.Say(reason);
            });
            s.Row(legs > 0 ? "Clear the route  ·  " + legs + " leg(s)" : "Clear the route", () =>
            {
                NavigationOrders.ClearWaypoints(CommandState.Ship, out string reason);
                CommandState.Say(reason);
            });
            s.Info(nav.Status);
            s.Info("Right-click the map for a waypoint   ·   shift appends a leg");
        }

        // ---- weapons ---------------------------------------------------------

        // Manual fire. Choosing a weapon arms it; the next right-click on a
        // contact fires the salvo chosen below.
        private void WeaponsPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            if (ship == null) return;
            WeaponCommandInfo[] weapons = WeaponOrders.GetWeapons(ship);
            s.Title("WEAPONS  ·  manual fire");

            if (weapons.Length == 0) s.Info("No weapons under command.");
            foreach (WeaponCommandInfo weapon in weapons)
            {
                string key = weapon.Key;
                bool armed = key == CommandState.SelectedKey;
                Button row = s.Row((armed ? "▸ " : "") + weapon.Name + "   ·   " + weapon.Readiness +
                    (weapon.Continuous ? "  ·  continuous" : "  ·  " + weapon.Ammo), () =>
                {
                    CommandState.SelectedKey = CommandState.SelectedKey == key ? null : key;
                    CommandState.Say(CommandState.SelectedKey == null
                        ? "Weapon released" : "Right-click a contact to engage");
                });
                if (armed) row.image.color = Theme.AccentFill;
                row.GetComponentInChildren<Text>().color =
                    weapon.Readiness == "Ready" ? Theme.Text
                    : weapon.Readiness == "Reloading" ? Theme.Warn : Theme.TextFaint;
            }

            WeaponCommandInfo selected = CommandState.SelectedWeapon();
            if (selected != null && !selected.Continuous)
            {
                s.Info("SALVO");
                var labels = new string[Salvo.Length];
                for (int i = 0; i < Salvo.Length; i++) labels[i] = Salvo[i] == 1 ? "Single" : "×" + Salvo[i];
                Button[] group = s.Group(labels, i => CommandState.Quantity = Salvo[i]);
                for (int i = 0; i < Salvo.Length; i++)
                    if (Salvo[i] == CommandState.Quantity) group[i].image.color = Theme.AccentFill;
            }

            Button cease = s.Row("CEASE FIRE", () =>
            {
                WeaponOrders.CeaseFire(CommandState.Ship, out string reason);
                CommandState.SelectedKey = null;
                CommandState.Say(reason);
            });
            cease.image.color = Theme.Dim(Theme.Bad, 0.55f);
            s.Info(selected != null
                ? "Right-click a contact to fire " + selected.Name
                : "Choose a weapon, then right-click a contact");
        }

        // ---- engagement ------------------------------------------------------

        private void EngagementPage(Surface s)
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
                Button row = s.Row(EngagementPolicy.Describe(mode) + "   ·   " + detail[i], () =>
                {
                    EngagementPolicy.SetMode(CommandState.Ship, mode, out string reason);
                    CommandState.Say(reason);
                });
                if (mode == current) row.image.color = Theme.AccentFill;
            }
            Button cease = s.Row("CEASE FIRE", () =>
            {
                WeaponOrders.CeaseFire(CommandState.Ship, out string reason);
                CommandState.SelectedKey = null;
                CommandState.Say(reason);
            });
            cease.image.color = Theme.Dim(Theme.Bad, 0.55f);
        }

        // ---- sensors ---------------------------------------------------------

        private void SensorsPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            if (ship == null) return;
            SensorSnapshot[] sensors = Sensors.GetSensors(ship);
            bool silent = Sensors.IsSilent(ship);
            s.Title(silent ? "SENSORS  ·  " + UiKit.Tint("EMCON SILENT", Theme.Good) : "SENSORS");
            s.Info("Own tracks " + TrackPicture.OwnCount(ship) + "   ·   ESM " + Esm.GetContacts(ship).Length +
                (silent ? "   ·   picture is datalink only" : ""), Theme.Text);

            Button[] modes = s.Group(new[] { "EMCON · all silent", "Radiate · all on" }, i =>
            {
                Sensors.SetAllEmitting(CommandState.Ship, i == 1, out string reason);
                CommandState.Say(reason);
            });
            modes[silent ? 0 : 1].image.color = Theme.AccentFill;

            foreach (SensorSnapshot sensor in sensors)
            {
                SensorSnapshot shown = sensor;
                string state = !sensor.Operational ? "UNAVAILABLE"
                    : !sensor.IsEmitter ? "passive"
                    : sensor.Active ? (sensor.Jammed ? "RADIATING · JAMMED" : "RADIATING")
                    : "silent";
                string detail = sensor.RangeMetres > 1f ? "  ·  " + UnitConverter.DistanceReading(sensor.RangeMetres) : "";
                if (sensor.DetectedCount >= 0) detail += "  ·  " + sensor.DetectedCount + " tracked";
                Button row = s.Row(sensor.Name + "   ·   " + state + detail, () =>
                {
                    if (!shown.IsEmitter) { CommandState.Say(shown.Name + " is passive; it emits nothing to shut down."); return; }
                    Sensors.SetEmitting(CommandState.Ship, shown.Id, !shown.Active, out string reason);
                    CommandState.Say(reason);
                });
                row.GetComponentInChildren<Text>().color =
                    !sensor.Operational ? Theme.TextFaint
                    : !sensor.IsEmitter ? Theme.Datalink
                    : sensor.Jammed ? Theme.Bad
                    : sensor.Active ? Theme.Warn       // radiating is a risk, not a success
                    : Theme.Good;                       // silent is the safe state
            }
        }

        // ---- damage control ----------------------------------------------------

        private void DamagePage(Surface s)
        {
            Ship ship = CommandState.Ship;
            if (ship == null) return;
            DamageSnapshot damage = DamageControl.GetSnapshot(ship);
            float pool = damage.DamageControlPoolMax > 0.01f
                ? Mathf.Clamp01(damage.DamageControlPool / damage.DamageControlPoolMax) * 100f : 0f;
            // Totals that say whether damage control is winning: how much water
            // is coming in against how much is being put out.
            float intake = 0f;
            int prioritised = 0;
            foreach (CompartmentSnapshot c in damage.Compartments)
            {
                intake += c.LeakRate;
                if (c.Priority) prioritised++;
            }

            s.Title("DAMAGE CONTROL  ·  " + damage.ShipState);
            s.Info("Reserve " + pool.ToString("0") + "%   ·   list " + damage.ListDegrees.ToString("0.0") +
                "°   ·   trim " + damage.TrimDegrees.ToString("0.0") + "°", Theme.Scale(pool));
            s.Info("Flooding " + damage.Flooding + " compartment(s)" +
                (intake > 0.001f ? "   ·   " + UiKit.Tint("taking water", Theme.Bad) : "   ·   no active leaks") +
                (prioritised > 0 ? "   ·   " + prioritised + " prioritised" : ""), Theme.Text);
            s.Row("Work the whole ship  ·  clear priorities", () =>
            {
                DamageControl.ClearPriorities(CommandState.Ship, out string reason);
                CommandState.Say(reason);
            });

            // Worst first: a list of forty sound compartments helps nobody.
            var ordered = new List<CompartmentSnapshot>(damage.Compartments);
            ordered.Sort((a, b) => Severity(b).CompareTo(Severity(a)));
            int shown = 0;
            foreach (CompartmentSnapshot compartment in ordered)
            {
                if (Severity(compartment) <= 0f) continue;
                int id = compartment.Id;
                string flooded = float.IsNaN(compartment.FloodedPercent) ? "" :
                    compartment.FloodedPercent > 0.5f ? "  ·  " + compartment.FloodedPercent.ToString("0") + "% flooded" : "";
                string integrity = float.IsNaN(compartment.IntegrityPercent) ? "" :
                    "  ·  hull " + compartment.IntegrityPercent.ToString("0") + "%";
                // A falling leak is the only visible sign the parties are
                // winning, so show it rather than leave it to be inferred.
                string leak = compartment.LeakRate > 0.001f
                    ? "  ·  leak " + compartment.LeakPercentOfMax.ToString("0") + "%" + (compartment.Working ? " and falling" : "")
                    : "";
                Button row = s.Row((compartment.Priority ? "▲ " : "") + compartment.Name + "  ·  " +
                    compartment.State + integrity + flooded + leak, () =>
                {
                    bool seal = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    string reason;
                    if (seal) DamageControl.SealCompartment(CommandState.Ship, id, out reason);
                    else DamageControl.TogglePriority(CommandState.Ship, id, out reason);
                    CommandState.Say(reason);
                });
                row.GetComponentInChildren<Text>().color =
                    compartment.Submerged || compartment.Detached || compartment.Removed ? Theme.TextFaint
                    : compartment.LeakRate > 0.01f ? Theme.Bad
                    : compartment.Sealed ? Theme.Warn
                    : Theme.Scale(compartment.IntegrityPercent);
                if (compartment.Priority) row.image.color = Theme.AccentFill;
                shown++;
            }
            if (shown == 0) s.Info("No damage.", Theme.TextMuted);
            else s.Info("Click a compartment to concentrate damage control   ·   shift-click to seal it off");
        }

        // Ordering weight: flooding outranks structural damage, because flooding
        // is what capsizes the ship.
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

        // ---- replenishment -----------------------------------------------------

        private void ReplenishmentPage(Surface s)
        {
            Ship ship = CommandState.Ship;
            if (ship == null) return;
            RearmSnapshot status = Replenishment.Status(ship);
            s.Title("REPLENISHMENT");
            s.Info(status.Reason, Theme.Text);
            s.Info(status.StationsShort + " station(s) below capacity");
            Button request = s.Row(status.Requested ? "Requested  ·  waiting" : "Request rearm", () =>
            {
                Replenishment.Request(CommandState.Ship, out string reason);
                CommandState.Say(reason);
            });
            if (status.Requested) request.image.color = Theme.AccentFill;
            else if (status.StationsShort == 0) request.GetComponentInChildren<Text>().color = Theme.TextMuted;
        }
    }
}

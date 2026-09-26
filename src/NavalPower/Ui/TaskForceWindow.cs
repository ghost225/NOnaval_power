using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NavalPower
{
    // The Task Force window: Sea Power's formation manager with the orders
    // folded in. Every ship in the force on a row -- guide first -- with its
    // station and state; clicking a row takes command of that ship.
    internal sealed partial class CommandUi
    {
        private static readonly string[] FormationNames = { "Screen", "Column", "Abreast", "Box" };

        private void TaskForcePage(Surface s)
        {
            Ship ship = CommandState.Ship;
            if (ship == null) { s.Title("TASK FORCE"); s.Info("Command a ship to form a task force.", Theme.TextMuted); return; }
            TaskForce force = TaskForces.Of(ship);
            if (force == null) { NoForcePage(s, ship); return; }

            s.Title("TASK FORCE " + force.Name.ToUpperInvariant() + "  ·  " + force.Count + " ships");
            Button[] shapes = s.Group(FormationNames, i => TaskForces.SetFormation(force, (Formation)i));
            if (force.Formation != Formation.Custom) shapes[(int)force.Formation].image.color = Theme.AccentFill;
            s.Row("Edit formation…" + (force.Formation == Formation.Custom ? "  ·  custom stations" : ""),
                () => Open("tfedit", FormationEditorPage));
            s.Info("Spacing ×" + force.Spacing.ToString("0.0") + "  ·  basic gap " +
                UnitConverter.DistanceReading(TaskForces.Gap(force)) +
                (force.Formation == Formation.Screen ? "  ·  pickets face the nearest known threat" : ""), Theme.TextMuted);
            s.SliderRow(0.5f, 3f, force.Spacing, value => TaskForces.SetSpacing(force, Mathf.Round(value * 10f) / 10f));
            Button north = s.Row(force.FixedNorth ? "Stations fixed to north" : "Stations turn with the guide's course",
                () => force.FixedNorth = !force.FixedNorth);
            if (force.FixedNorth) north.image.color = Theme.AccentFill;

            // Speed for the whole force, against what its slowest ship allows.
            float formation = TaskForces.FormationSpeed(force, out Ship slowest);
            NavigationSnapshot guideNav = NavigationOrders.GetSnapshot(force.Guide);
            s.Info("Formation speed  ·  up to " + Speed(formation) + (slowest != null && force.Escorts.Count > 0
                ? " (" + ShipNames.Of(slowest) + ")" : "") +
                (guideNav != null ? "  ·  ordered " + Speed(Mathf.Min(guideNav.OrderedSpeedKnots, formation)) : ""), Theme.TextMuted);
            float[] fractions = { 0f, 1f / 3f, 2f / 3f, 1f };
            Button[] speeds = s.Group(new[] { "Stop", "1/3", "2/3", "Full" }, i =>
            {
                TaskForces.SetSpeed(force, fractions[i]);
                CommandState.Say(force.Name + " · " + Speed(formation * fractions[i]));
            });
            for (int i = 0; i < fractions.Length; i++)
                if (guideNav != null && formation > 0.1f && Mathf.Abs(Mathf.Min(guideNav.OrderedSpeedKnots, formation) - formation * fractions[i]) < 0.6f)
                    speeds[i].image.color = Theme.AccentFill;

            // The guide, then each escort: click to take command of it.
            NavigationSnapshot nav = NavigationOrders.GetSnapshot(force.Guide);
            ShipRow(s, force.Guide, "GUIDE  ·  " + ShipNames.Of(force.Guide) + Class(force.Guide) + "  ·  " +
                (nav != null ? Speed(nav.ActualSpeedKnots) : "") + (force.UnderFire ? "  ·  under fire" : ""), Theme.Text);
            int detached = 0;
            foreach (Escort escort in force.Escorts)
            {
                if (escort.Detached) detached++;
                string where = (escort.ThreatArc ? "picket " : "") + escort.Bearing.ToString("000") + "°  " +
                    UnitConverter.DistanceReading(escort.Range);
                ShipRow(s, escort.Ship, "      " + ShipNames.Of(escort.Ship) + Class(escort.Ship) + "  ·  " + where + "  ·  " +
                    TaskForces.Describe(force, escort),
                    escort.Detached ? Theme.Warn : escort.GivingWay ? Theme.Warn : Theme.Text);
            }
            if (detached > 0)
                s.Row("Return " + detached + " detached ship(s) to formation", () =>
                {
                    foreach (Escort escort in force.Escorts) if (escort.Detached) TaskForces.Rejoin(escort.Ship);
                    CommandState.Say(force.Name + " · detached ships returning to formation");
                });
            if (force.Guide != ship)
                s.Row("Make " + ShipNames.Of(ship) + " the guide", () => TaskForces.MakeGuide(ship));

            // The whole force at once.
            s.Info("WHOLE FORCE", Theme.Accent);
            EngagementMode roe = EngagementPolicy.GetMode(force.Guide);
            Button[] rules = s.Group(new[] { "Weapons free", "Weapons tight", "Weapons hold" }, i =>
            {
                foreach (Ship member in force.Ships()) EngagementPolicy.SetMode(member, (EngagementMode)i, out _);
                CommandState.Say(force.Name + " · " + EngagementPolicy.Describe((EngagementMode)i));
            });
            rules[(int)roe].image.color = Theme.AccentFill;
            bool silent = Sensors.IsSilent(force.Guide);
            Button[] emcon = s.Group(new[] { "Radiate", "Silent" }, i =>
            {
                foreach (Ship member in force.Ships()) Sensors.SetAllEmitting(member, i == 0, out _);
                CommandState.Say(force.Name + (i == 0 ? " · radiating" : " · silent"));
            });
            emcon[silent ? 1 : 0].image.color = Theme.AccentFill;
            Button cease = s.Row("CEASE FIRE, all ships", () =>
            {
                foreach (Ship member in force.Ships()) WeaponOrders.CeaseFire(member, out _);
                CommandState.SelectedKey = null;
                CommandState.Say(force.Name + " · cease fire");
            });
            cease.image.color = Theme.Dim(Theme.Bad, 0.5f);

            s.Row("Add ships…", () => s.Show(x => AddShipsPage(x, force)));
            if (TaskForces.All.Count > 1) s.Row("All task forces…", () => s.Show(AllForcesPage));
            s.Row("Leave the task force  ·  " + ShipNames.Of(ship), () =>
            {
                TaskForces.Remove(ship);
                CommandState.Say(ShipNames.Of(ship) + " · left " + force.Name);
            });
            s.Row("Disband " + force.Name, () =>
            {
                TaskForces.Disband(force);
                CommandState.Say(force.Name + " disbanded");
            });
            s.Info("[ and ] step through the force's ships", Theme.TextFaint);
        }

        // A ship's class beside its name, quieter than the name.
        private static string Class(Ship ship) =>
            ship != null && ShipNames.IsNamed(ship) ? "  " + UiKit.Tint(ShipNames.TypeOf(ship), Theme.TextMuted) : "";

        // Every force, Sea Power's formation manager: one click to its guide.
        private void AllForcesPage(Surface s)
        {
            s.Title("TASK FORCES  ·  " + TaskForces.All.Count);
            if (TaskForces.All.Count == 0) s.Info("No task forces.", Theme.TextMuted);
            foreach (TaskForce force in TaskForces.All)
            {
                if (force.Guide == null) continue;
                Ship guide = force.Guide;
                NavigationSnapshot nav = NavigationOrders.GetSnapshot(guide);
                Button row = s.Row(force.Name + "  ·  " + force.Count + " ships  ·  guide " + ShipNames.Of(guide) + Class(guide) +
                    (nav != null ? "  ·  " + Speed(nav.ActualSpeedKnots) : "") + (force.UnderFire ? "  ·  under fire" : ""), () =>
                    {
                        if (guide != CommandState.Ship) SceneSingleton<CameraStateManager>.i?.SetFollowingUnit(guide);
                    });
                if (TaskForces.Of(CommandState.Ship) == force) row.image.color = Theme.AccentFill;
            }
            s.Row("Back", () => s.Show(TaskForcePage));
        }

        private void ShipRow(Surface s, Ship ship, string label, Color colour)
        {
            Ship shown = ship;
            Button row = s.Row(label, () =>
            {
                if (shown != CommandState.Ship) SceneSingleton<CameraStateManager>.i?.SetFollowingUnit(shown);
            });
            row.GetComponentInChildren<Text>().color = colour;
            if (ship == CommandState.Ship) row.image.color = Theme.AccentFill;
        }

        private void NoForcePage(Surface s, Ship ship)
        {
            s.Title("TASK FORCE");
            s.Info(ShipNames.Of(ship) + " sails alone.", Theme.TextMuted);
            if (TaskForces.All.Count > 0) s.Row("All task forces…", () => s.Show(AllForcesPage));
            s.Row("Form a task force on " + ShipNames.Of(ship) + "…", () =>
            {
                TaskForce created = TaskForces.Create(ship);
                s.Show(x => AddShipsPage(x, created));
            });
            foreach (TaskForce force in TaskForces.All)
            {
                if (force.Guide == null || force.Guide.NetworkHQ != ship.NetworkHQ) continue;
                TaskForce chosen = force;
                s.Row("Join " + force.Name + "  ·  guide " + ShipNames.Of(force.Guide) + Class(force.Guide) + "  ·  " +
                    UnitConverter.DistanceReading(FastMath.Distance(ship.GlobalPosition(), force.Guide.GlobalPosition())), () =>
                    {
                        if (TaskForces.Add(chosen, ship, out string reason)) CommandState.Say(ShipNames.Of(ship) + " · joining " + chosen.Name);
                        else CommandState.Say(reason);
                    });
            }
        }

        // Commandable friendly ships nearby, nearest first.
        private void AddShipsPage(Surface s, TaskForce force)
        {
            if (force == null || force.Guide == null) { s.Show(TaskForcePage); return; }
            s.Title("ADD TO " + force.Name.ToUpperInvariant());
            var candidates = new List<Ship>();
            foreach (Unit unit in UnitRegistry.allUnits)
                if (unit is Ship other && !other.disabled && TaskForces.Of(other) != force &&
                    other.NetworkHQ == force.Guide.NetworkHQ && CommandableShip.CanCommand(other, out _) &&
                    FastMath.Distance(other.GlobalPosition(), force.Guide.GlobalPosition()) < 60000f)
                    candidates.Add(other);
            candidates.Sort((a, b) => FastMath.Distance(a.GlobalPosition(), force.Guide.GlobalPosition())
                .CompareTo(FastMath.Distance(b.GlobalPosition(), force.Guide.GlobalPosition())));
            if (candidates.Count == 0) s.Info("No other ship you can command within 60 km.", Theme.TextMuted);
            foreach (Ship other in candidates)
            {
                Ship chosen = other;
                TaskForce current = TaskForces.Of(other);
                s.Row(ShipNames.Of(other) + Class(other) + "  ·  " +
                    UnitConverter.DistanceReading(FastMath.Distance(other.GlobalPosition(), force.Guide.GlobalPosition())) +
                    (current != null ? "  ·  in " + current.Name : ""), () =>
                    {
                        if (TaskForces.Add(force, chosen, out string reason)) CommandState.Say(ShipNames.Of(chosen) + " · joining " + force.Name);
                        else CommandState.Say(reason);
                    });
            }
            s.Row("Done", () => s.Show(TaskForcePage));
        }

        // "[" and "]": the previous and next ship in the force.
        private void CycleTaskForce()
        {
            if (!CommandState.Active || CommandState.Ship == null || CursorManager.GetFlag(CursorFlags.Chat)) return;
            int step = Settings.NextInForce.Value.IsDown() ? 1 : Settings.PreviousInForce.Value.IsDown() ? -1 : 0;
            if (step == 0) return;
            TaskForce force = TaskForces.Of(CommandState.Ship);
            if (force == null) { CommandState.Say(ShipNames.Of(CommandState.Ship) + " is not in a task force"); return; }
            var ships = new List<Ship>(force.Ships());
            int index = ships.IndexOf(CommandState.Ship);
            Ship next = ships[((index + step) % ships.Count + ships.Count) % ships.Count];
            if (next != CommandState.Ship) SceneSingleton<CameraStateManager>.i?.SetFollowingUnit(next);
        }

        // "TF ALPHA 2/4", for the strip.
        private static string ForceTag(Ship ship)
        {
            TaskForce force = TaskForces.Of(ship);
            if (force == null) return "";
            var ships = new List<Ship>(force.Ships());
            return "  " + UiKit.Tint("TF " + force.Name.ToUpperInvariant() + " " + (ships.IndexOf(ship) + 1) + "/" + ships.Count, Theme.Accent);
        }
    }
}

using System.Collections.Generic;
using NuclearOption.SavedMission;
using UnityEngine;

namespace NavalPower
{
    public sealed class DeckAircraft
    {
        public AircraftDefinition Definition;
        public string Name;
        public bool InReserve;
        public float Price;
    }

    // One weapon station on an airframe, with everything it will accept.
    // Loadout.weapons is index-parallel to WeaponManager.hardpointSets, so a
    // station is addressed by its index and nothing has to be inferred.
    public sealed class LoadoutStation
    {
        public int Index;
        public string Name;
        public List<WeaponMount> Options = new List<WeaponMount>();
        public WeaponMount Selected;

        public string SelectedName => Selected == null ? "empty" : Selected.mountName;
    }

    // A per-station loadout for one airframe. Deliberately not a preset: the
    // point is to choose each station rather than accept whatever a named
    // profile happens to pick.
    public sealed class LoadoutPlan
    {
        public AircraftDefinition Definition;
        public List<LoadoutStation> Stations = new List<LoadoutStation>();
        // Fraction of internal fuel. The airframe's own default is sometimes
        // low enough that the pilot's fuel check fails on the first frame and
        // the flight turns straight back for home, so this is worth choosing.
        public float Fuel = 1f;

        public Loadout Build()
        {
            var loadout = new Loadout();
            int count = 0;
            foreach (LoadoutStation station in Stations) count = Mathf.Max(count, station.Index + 1);
            for (int i = 0; i < count; i++) loadout.weapons.Add(null);
            foreach (LoadoutStation station in Stations) loadout.weapons[station.Index] = station.Selected;
            return loadout;
        }

        public string Summary()
        {
            var parts = new List<string>();
            foreach (LoadoutStation station in Stations)
                if (station.Selected != null) parts.Add(station.Selected.mountName);
            return parts.Count == 0 ? "clean" : string.Join(", ", parts.ToArray());
        }
    }

    // Flight deck operations for any ship carrying an Airbase. Nuclear Option
    // models a ship-borne deck as an Airbase component with AttachedAirbase
    // set -- the same component a runway uses -- so nothing here is specific to
    // carriers, and a destroyer's helipad works the same way.
    public static class CarrierOps
    {
        public static Airbase Deck(Ship ship)
        {
            if (ship == null) return null;
            var airbase = ship.GetComponent<Airbase>();
            return airbase != null && !airbase.disabled ? airbase : null;
        }

        public static bool HasDeck(Ship ship) => Deck(ship) != null;

        public static string DeckStatus(Ship ship)
        {
            Airbase deck = Deck(ship);
            if (deck == null) return "No flight deck";
            int functional = 0, total = 0;
            foreach (Hangar hangar in ship.GetComponentsInChildren<Hangar>(true))
            {
                total++;
                if (hangar.IsFunctional()) functional++;
            }
            return functional + " of " + total + " hangar" + (total == 1 ? "" : "s") + " serviceable";
        }

        public static DeckAircraft[] Available(Ship ship)
        {
            var rows = new List<DeckAircraft>();
            Airbase deck = Deck(ship);
            if (deck == null || ship.NetworkHQ == null) return rows.ToArray();
            List<AircraftDefinition> available = deck.GetAvailableAircraft();
            if (available == null) return rows.ToArray();
            foreach (AircraftDefinition definition in available)
            {
                if (definition == null) continue;
                rows.Add(new DeckAircraft
                {
                    Definition = definition,
                    Name = definition.unitName,
                    InReserve = ship.NetworkHQ.GetUnitSupply(definition) > 0,
                    Price = definition.value
                });
            }
            return rows.ToArray();
        }

        // What was last loaded on each airframe, so a second sortie starts from
        // the previous choice rather than from empty stations.
        private static readonly Dictionary<AircraftDefinition, Dictionary<int, string>> remembered =
            new Dictionary<AircraftDefinition, Dictionary<int, string>>();

        internal static void Remember(LoadoutPlan plan)
        {
            if (plan == null || plan.Definition == null) return;
            var record = new Dictionary<int, string>();
            foreach (LoadoutStation station in plan.Stations)
                record[station.Index] = station.Selected != null ? station.Selected.name : null;
            remembered[plan.Definition] = record;
        }

        // Every station the airframe has, with every mount it will accept.
        public static LoadoutPlan PlanFor(AircraftDefinition definition)
        {
            var plan = new LoadoutPlan { Definition = definition, Fuel = Mathf.Clamp01(Settings.DefaultFuel.Value) };
            if (definition == null || definition.unitPrefab == null) return plan;
            var prefab = definition.unitPrefab.GetComponent<Aircraft>();
            HardpointSet[] sets = prefab != null && prefab.weaponManager != null ? prefab.weaponManager.hardpointSets : null;
            if (sets == null) return plan;
            for (int i = 0; i < sets.Length; i++)
            {
                HardpointSet set = sets[i];
                if (set == null) continue;
                var station = new LoadoutStation
                {
                    Index = i,
                    Name = string.IsNullOrEmpty(set.name) ? "Station " + (i + 1) : Naming.Pretty(set.name)
                };
                if (set.weaponOptions != null)
                    foreach (WeaponMount mount in set.weaponOptions)
                        if (mount != null) station.Options.Add(mount);
                // Restore the last choice for this station when there was one,
                // matched by name so it survives a different mount list.
                if (remembered.TryGetValue(definition, out Dictionary<int, string> record) &&
                    record.TryGetValue(i, out string chosen) && chosen != null)
                {
                    foreach (WeaponMount option in station.Options)
                        if (option.name == chosen && Releasable(null, option)) { station.Selected = option; break; }
                }
                plan.Stations.Add(station);
            }
            return plan;
        }

        // Mirrors Loadout.AllowedByHQ per mount, so a station never offers a
        // weapon the faction would refuse at launch -- nuclear stores before
        // release authority, and anything on the HQ's restricted list.
        public static bool Releasable(Ship ship, WeaponMount mount)
        {
            if (mount == null) return false;
            FactionHQ hq = ship != null ? ship.NetworkHQ : null;
            if (hq != null && hq.restrictedWeapons != null && hq.restrictedWeapons.Contains(mount.name)) return false;
            if (mount.info == null || !mount.info.nuclear) return true;
            if (!MissionManager.AllowTactical()) return false;
            if (mount.info.strategic && !MissionManager.AllowStrategic()) return false;
            return true;
        }

        public static bool Launch(Ship ship, LoadoutPlan plan, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            Airbase deck = Deck(ship);
            if (deck == null) { reason = "This ship has no flight deck."; return false; }
            if (plan == null || plan.Definition == null) { reason = "Choose an airframe first."; return false; }
            FactionHQ hq = ship.NetworkHQ;
            if (hq == null) { reason = "No faction."; return false; }

            bool serviceable = false;
            foreach (Hangar hangar in ship.GetComponentsInChildren<Hangar>(true))
                if (hangar.IsFunctional()) { serviceable = true; break; }
            if (!serviceable) { reason = "No serviceable hangar."; return false; }

            if (!deck.CanSpawnAircraft(plan.Definition))
            { reason = "The deck cannot launch that airframe right now."; return false; }

            Loadout loadout = plan.Build();
            var prefab = plan.Definition.unitPrefab != null ? plan.Definition.unitPrefab.GetComponent<Aircraft>() : null;
            if (prefab != null && prefab.weaponManager != null && !loadout.AllowedByHQ(prefab.weaponManager, hq))
            { reason = "The faction will not release that loadout."; return false; }

            // Taken from the reserve when one is held, bought otherwise, which
            // is how the faction economy expects aircraft to be drawn.
            bool purchased = false;
            if (hq.GetUnitSupply(plan.Definition) <= 0)
            {
                if (hq.factionFunds < plan.Definition.value)
                { reason = "No airframe in reserve and insufficient funds."; return false; }
                hq.AddFunds(-plan.Definition.value);
                hq.ModifyUnitSupply(plan.Definition, 1);
                purchased = true;
            }

            Airbase.TrySpawnResult result = deck.TrySpawnAircraft(null, plan.Definition,
                new LiveryKey(0), loadout, Mathf.Clamp01(plan.Fuel));
            if (!result.Allowed)
            {
                if (purchased)
                {
                    hq.ModifyUnitSupply(plan.Definition, -1);
                    hq.AddFunds(plan.Definition.value);
                }
                reason = "The deck rejected the launch.";
                return false;
            }

            Remember(plan);
            FlightOrders.ExpectLaunch(ship, plan.Definition, loadout);
            reason = "Launching " + plan.Definition.unitName + " · " + (plan.Fuel * 100f).ToString("0") + "% fuel · " + plan.Summary() +
                (purchased ? " · purchased" : " · from reserve");
            Plugin.Log.LogInfo("[deck] " + ship.definition?.unitName + ": " + reason);
            return true;
        }
    }
}

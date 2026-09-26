using HarmonyLib;
using System.Collections.Generic;
using NuclearOption.Networking;
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

        public string Callsign;
        // How many to launch with this loadout; more than one is a wing.
        public int Count = 1;
        // The skin, from the same list the game's own spawn screen offers:
        // faction liveries plus app-data and workshop skins.
        public LiveryKey Livery = new LiveryKey(0);
        public string LiveryName = "Default";

        // A copy the launch queue can hold: editing the loadout page after
        // pressing LAUNCH must not change aircraft still waiting for a hangar.
        public LoadoutPlan Snapshot()
        {
            var copy = (LoadoutPlan)MemberwiseClone();
            copy.Stations = new List<LoadoutStation>();
            foreach (LoadoutStation station in Stations)
                copy.Stations.Add(new LoadoutStation
                {
                    Index = station.Index,
                    Name = station.Name,
                    Options = station.Options,
                    Selected = station.Selected
                });
            return copy;
        }

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

    // Flight operations for any Airbase: a land field, or a ship's deck.
    // Nuclear Option models a ship-borne deck as an Airbase component with
    // AttachedAirbase set -- the same component a runway uses -- so nothing
    // here is specific to carriers, and a destroyer's helipad and a land
    // field work the same way. What a field can launch is its own business:
    // GetAvailableAircraft already filters by what its hangars can host,
    // which is how runway-only airframes appear at a land base and not at sea.
    public static class CarrierOps
    {
        public static Airbase Deck(Ship ship)
        {
            if (ship == null) return null;
            var airbase = ship.GetComponent<Airbase>();
            return airbase != null && !airbase.disabled ? airbase : null;
        }

        public static DeckAircraft[] Available(Airbase field)
        {
            var rows = new List<DeckAircraft>();
            FactionHQ hq = field != null ? field.CurrentHQ : null;
            if (field == null || hq == null) return rows.ToArray();
            List<AircraftDefinition> available = field.GetAvailableAircraft();
            if (available == null) return rows.ToArray();
            foreach (AircraftDefinition definition in available)
            {
                if (definition == null) continue;
                rows.Add(new DeckAircraft
                {
                    Definition = definition,
                    Name = definition.unitName,
                    InReserve = hq.GetUnitSupply(definition) > 0,
                    Price = definition.value
                });
            }
            return rows.ToArray();
        }

        // What was last loaded on each airframe, so a second sortie starts from
        // the previous choice rather than from empty stations.
        private static readonly Dictionary<AircraftDefinition, Dictionary<int, string>> remembered =
            new Dictionary<AircraftDefinition, Dictionary<int, string>>();
        private static readonly Dictionary<AircraftDefinition, (LiveryKey key, string name)> rememberedLivery =
            new Dictionary<AircraftDefinition, (LiveryKey, string)>();
        private static readonly Dictionary<AircraftDefinition, int> rememberedCount =
            new Dictionary<AircraftDefinition, int>();

        internal static void Remember(LoadoutPlan plan)
        {
            if (plan == null || plan.Definition == null) return;
            var record = new Dictionary<int, string>();
            foreach (LoadoutStation station in plan.Stations)
                record[station.Index] = station.Selected != null ? station.Selected.name : null;
            remembered[plan.Definition] = record;
            rememberedLivery[plan.Definition] = (plan.Livery, plan.LiveryName);
            rememberedCount[plan.Definition] = plan.Count;
        }

        // Exactly what the native spawn screen lists for this airframe and
        // faction -- LoadoutSelector builds it as a public static, workshop
        // skins included.
        internal static List<(LiveryKey key, string label)> Liveries(AircraftDefinition definition, FactionHQ hq)
        {
            var options = new List<(LiveryKey key, string label)>();
            if (definition == null) return options;
            string faction = hq != null && hq.faction != null ? hq.faction.factionName : "";
            try { LoadoutSelector.GetLiveryOptions(options, definition, faction, allowFactionLivery: true); }
            catch (System.Exception ex) { Plugin.Log.LogWarning("[deck] could not list liveries: " + ex.Message); }
            return options;
        }

        // Every station the airframe has, with every mount it will accept.
        public static LoadoutPlan PlanFor(AircraftDefinition definition)
        {
            var plan = new LoadoutPlan { Definition = definition, Fuel = Mathf.Clamp01(Settings.DefaultFuel.Value) };
            plan.Callsign = Callsigns.Suggest(definition);
            if (definition != null && rememberedLivery.TryGetValue(definition, out var livery))
            {
                plan.Livery = livery.key;
                plan.LiveryName = livery.name;
            }
            if (definition != null && rememberedCount.TryGetValue(definition, out int count)) plan.Count = count;
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
        public static bool Releasable(FactionHQ hq, WeaponMount mount)
        {
            if (mount == null) return false;
            if (hq != null && hq.restrictedWeapons != null && hq.restrictedWeapons.Contains(mount.name)) return false;
            if (mount.info == null || !mount.info.nuclear) return true;
            if (!MissionManager.AllowTactical()) return false;
            if (mount.info.strategic && !MissionManager.AllowStrategic()) return false;
            return true;
        }

        // One aircraft, now, from a hangar that is free. Called by the launch
        // queue, which waits for the hangar; a wing is several of these.
        public static bool Launch(Airbase deck, LoadoutPlan plan, string callsign, string wing, out string reason)
        {
            if (deck == null || deck.disabled) { reason = "No flight deck or field."; return false; }
            Ship ship = Airfields.ShipOf(deck);
            if (ship != null ? !CommandableShip.CanCommand(ship, out reason) : !Airfields.CanCommand(deck, out reason))
                return false;
            if (plan == null || plan.Definition == null) { reason = "Choose an airframe first."; return false; }
            FactionHQ hq = deck.CurrentHQ;
            if (hq == null) { reason = "No faction."; return false; }

            bool serviceable = false;
            foreach (Hangar hangar in deck.hangars)
                if (Airfields.Serviceable(hangar)) { serviceable = true; break; }
            if (!serviceable) { reason = "No serviceable hangar."; return false; }

            if (!deck.CanSpawnAircraft(plan.Definition))
            { reason = "The deck cannot launch that airframe right now."; return false; }

            Loadout loadout = plan.Build();
            var prefab = plan.Definition.unitPrefab != null ? plan.Definition.unitPrefab.GetComponent<Aircraft>() : null;
            if (prefab != null && prefab.weaponManager != null && !loadout.AllowedByHQ(prefab.weaponManager, hq))
            { reason = "The faction will not release that loadout."; return false; }

            // Who pays. With the air wing funded from your own allocation the
            // faction supplies the airframe but no longer buys it: the cost of
            // the sortie is yours, which is what makes bringing one back worth
            // anything. Otherwise it is drawn the way the faction economy
            // expects, from the reserve when one is held and bought when not.
            float price = plan.Definition.value;
            Player payer = null;
            bool purchased = false, stocked = false;

            // An airframe already in the reserve is one the faction has bought
            // and is holding for you; flying it costs nothing. Only one that
            // has to be produced is charged for, and with the wing funded from
            // your own allocation it is charged to you rather than to the
            // faction, which is what makes recovering one worth anything.
            if (hq.GetUnitSupply(plan.Definition) > 0)
            {
                // Drawn from stock. Nobody pays.
            }
            else if (Settings.LaunchCostFromAllocation.Value &&
                GameManager.GetLocalPlayer<Player>(out payer) && payer != null)
            {
                if (payer.Allocation < price)
                { reason = "You cannot afford a " + plan.Definition.unitName + "."; return false; }
                payer.AddAllocation(0f - price);
                hq.ModifyUnitSupply(plan.Definition, 1);
                stocked = true;
            }
            else
            {
                payer = null;
                if (hq.factionFunds < price)
                { reason = "No airframe in reserve and insufficient funds."; return false; }
                hq.AddFunds(0f - price);
                hq.ModifyUnitSupply(plan.Definition, 1);
                purchased = true;
            }

            Airbase.TrySpawnResult result = deck.TrySpawnAircraft(null, plan.Definition,
                plan.Livery, loadout, Mathf.Clamp01(plan.Fuel));
            if (!result.Allowed)
            {
                // Nothing left the deck, so nothing was spent.
                if (payer != null) payer.AddAllocation(price);
                if (stocked || purchased) hq.ModifyUnitSupply(plan.Definition, -1);
                if (purchased) hq.AddFunds(price);
                reason = "The deck rejected the launch.";
                return false;
            }

            Remember(plan);
            FlightOrders.ExpectLaunch(deck, plan.Definition, loadout, callsign, wing);
            reason = "Launching " + (string.IsNullOrEmpty(callsign) ? "" : callsign + " · ") +
                plan.Definition.unitName + " · " + (plan.Fuel * 100f).ToString("0") + "% fuel · " + plan.Summary() +
                (payer != null ? " · " + price.ToString("0") + " from your allocation"
                    : purchased ? " · purchased" : " · from reserve");
            Plugin.Log.LogInfo("[deck] " + Airfields.NameOf(deck) + ": " + reason);
            return true;
        }
    }

    // Bringing one home.
    //
    // The game pays a successful sortie bonus out of an aircraft's sortieScore,
    // and that score accrues in exactly one place: FactionHQ.RewardPlayer, onto
    // player.Aircraft -- the aircraft the player is personally sitting in. A
    // flight flown from a ship's bridge is never that aircraft, so its score
    // stays at zero however much it achieves, and the bonus it earns by landing
    // safely is zero times the rate.
    //
    // Rather than invent a score for it, the bonus is taken against what the
    // airframe is worth, at the mission's own rate. Recovering a flight you
    // paid for returns a share of its cost; losing it returns nothing. That is
    // the distinction the bonus exists to draw.
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.ReturnToInventory))]
    internal static class SortieBonusPatch
    {
        private const string Name = "Sortie bonus";

        private static void Postfix(Aircraft __instance) => Guard.Run(Name, () => Pay(__instance));

        private static void Pay(Aircraft aircraft)
        {
            if (!Settings.SortieBonusOnRecovery.Value || aircraft == null) return;
            // Ours to reward: a flight this ship launched and commanded.
            Flight flight = FlightOrders.Of(aircraft);
            if (flight == null || aircraft.definition == null) return;
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null) return;

            float rate = MissionManager.CurrentMission != null
                ? MissionManager.CurrentMission.missionSettings.successfulSortieBonus : 0f;
            if (rate <= 0f) return;

            float bonus = aircraft.definition.value * rate;
            if (bonus <= 0f) return;
            player.AddAllocation(bonus);
            player.AddScore(bonus);
            CommandState.Say(flight.Name + " · recovered · sortie bonus " + bonus.ToString("0"));
            Plugin.Log.LogInfo("[deck] " + flight.Name + " recovered · sortie bonus " + bonus.ToString("0"));
        }
    }
}

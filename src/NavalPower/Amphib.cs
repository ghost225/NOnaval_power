using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace NavalPower
{
    // Landing craft from a ship with a well deck (the Annex class).
    //
    // The hold is the game's own UnitStorage: an inventory of unit types, the
    // landing craft among them. What is new is choosing: which vehicles go in
    // a craft, where it lands, when it goes, buying what the hold lacks, and
    // calling a craft home. The game still does the rest -- the well deck's
    // rail steers the craft out, LandingCraftAI sails, beaches, unloads, and
    // docks back into the hold, which then holds it again.
    //
    // The landing point is the commander's call and the commander's risk: the
    // craft comes in at about 34 m/s and a wall at the waterline kills it and
    // its load. We only show where it will really come ashore, since the game
    // snaps an order to the nearest sea lane and road before running in.
    //
    // Purchases are priced exactly as the game prices a convoy bought from the
    // aircraft selection screen -- a unit's value plus the value of its
    // ammunition -- and come out of the player's allocation.
    internal static class Amphib
    {
        // ---- the ship and its hold ---------------------------------------------

        internal sealed class WellDeck
        {
            internal Ship Ship;
            internal UnitStorage Hold;
            internal UnitDefinition Craft;
            internal UnitStorage CraftHold;          // on the craft's prefab: capacity and what fits
        }

        private static readonly Dictionary<Ship, WellDeck> decks = new Dictionary<Ship, WellDeck>();
        private static readonly HashSet<Ship> without = new HashSet<Ship>();

        private static readonly FieldInfo DeployableTypes = AccessTools.Field(typeof(UnitStorage), "deployableTypes");
        private static readonly FieldInfo DeployTransform = AccessTools.Field(typeof(UnitStorage), "deployTransform");
        private static readonly FieldInfo LastDeployed = AccessTools.Field(typeof(UnitStorage), "lastDeployedUnit");
        private static readonly FieldInfo HomeDock = AccessTools.Field(typeof(LandingCraftAI), "homeDock");
        private static readonly FieldInfo LastDestination = AccessTools.Field(typeof(ShipAI), "lastDestinationSelected");
        private static readonly FieldInfo Destination = AccessTools.Field(typeof(ShipAI), "destination");
        private static readonly FieldInfo ShoreDirection = AccessTools.Field(typeof(LandingCraftAI), "shoreDirection");
        private static readonly FieldInfo Cushion = AccessTools.Field(typeof(LandingCraftAI), "airCushion");
        private static readonly MethodInfo WaitDeployUnits = AccessTools.Method(typeof(LandingCraftAI), "WaitDeployUnits");

        internal static WellDeck Deck(Ship ship)
        {
            if (ship == null || without.Contains(ship)) return null;
            if (decks.TryGetValue(ship, out WellDeck known) && known.Hold != null) return known;
            foreach (UnitStorage storage in ship.GetComponentsInChildren<UnitStorage>(true))
            {
                if (!(DeployableTypes?.GetValue(storage) is List<UnitDefinition> types)) continue;
                foreach (UnitDefinition type in types)
                {
                    if (type?.unitPrefab == null || type.unitPrefab.GetComponent<LandingCraftAI>() == null) continue;
                    var deck = new WellDeck
                    {
                        Ship = ship, Hold = storage, Craft = type,
                        CraftHold = type.unitPrefab.GetComponentInChildren<UnitStorage>(true)
                    };
                    decks[ship] = deck;
                    return deck;
                }
            }
            without.Add(ship);
            return null;
        }

        internal static bool HasWellDeck(Ship ship) => Deck(ship) != null;

        internal static UnitDefinition Lookup(string key) =>
            key != null && Encyclopedia.Lookup != null && Encyclopedia.Lookup.TryGetValue(key, out UnitDefinition found) ? found : null;

        internal static int Count(UnitStorage storage, UnitDefinition type)
        {
            if (storage?.GetStoredList() == null || type == null) return 0;
            foreach (UnitCount entry in storage.GetStoredList()) if (entry.UnitType == type.jsonKey) return entry.Count;
            return 0;
        }

        // Vehicles in the hold -- everything but the landing craft -- in a
        // steady order.
        internal static List<KeyValuePair<UnitDefinition, int>> Vehicles(WellDeck deck)
        {
            var list = new List<KeyValuePair<UnitDefinition, int>>();
            if (deck?.Hold?.GetStoredList() == null) return list;
            foreach (UnitCount entry in deck.Hold.GetStoredList())
            {
                UnitDefinition type = Lookup(entry.UnitType);
                if (type == null || type == deck.Craft || entry.Count <= 0) continue;
                list.Add(new KeyValuePair<UnitDefinition, int>(type, entry.Count));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Key.unitName, b.Key.unitName));
            return list;
        }

        internal static float CraftCapacity(WellDeck deck) => deck?.CraftHold != null ? deck.CraftHold.MassLimit : 80000f;

        internal static bool Fits(WellDeck deck, UnitDefinition type) =>
            type != null && (deck?.CraftHold == null || deck.CraftHold.CanFit(type));

        // ---- buying ------------------------------------------------------------

        // The convoy price: value plus the ammunition it comes with.
        internal static float Price(UnitDefinition type)
        {
            if (type == null) return 0f;
            float ammo = 0f;
            try { ammo = type.unitPrefab != null ? type.unitPrefab.GetComponent<Unit>()?.GetAmmoValue().Total ?? 0f : 0f; }
            catch { }
            return type.value + ammo;
        }

        internal static float Allocation() =>
            GameManager.GetLocalPlayer<NuclearOption.Networking.Player>(out var player) && player != null ? player.Allocation : 0f;

        // The landing craft, and the ground vehicles the faction fields in its
        // convoys that a craft can carry.
        internal static List<UnitDefinition> Catalogue(WellDeck deck)
        {
            var list = new List<UnitDefinition>();
            if (deck == null) return list;
            list.Add(deck.Craft);
            var vehicles = new List<UnitDefinition>();
            foreach (var group in deck.Ship.NetworkHQ?.faction?.GetConvoyGroups() ?? new List<Faction.ConvoyGroup>())
                foreach (var unit in group.Constituents)
                    if (unit?.Type != null && !vehicles.Contains(unit.Type) && Fits(deck, unit.Type)) vehicles.Add(unit.Type);
            vehicles.Sort((a, b) => string.CompareOrdinal(a.unitName, b.unitName));
            list.AddRange(vehicles);
            return list;
        }

        internal static bool Buy(Ship ship, UnitDefinition type, int count, out string reason)
        {
            WellDeck deck = Deck(ship);
            if (deck == null) { reason = "This ship has no well deck."; return false; }
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            if (!GameManager.GetLocalPlayer<NuclearOption.Networking.Player>(out var player) || player == null)
            { reason = "A local player is required."; return false; }
            float cost = Price(type) * count;
            if (player.Allocation < cost)
            { reason = "Not enough allocation · " + count + " × " + type.unitName + " costs " + cost.ToString("0"); return false; }
            player.AddAllocation(-cost);
            deck.Hold.AddOrRemoveUnit(type, count);
            bought.Add(ship);
            reason = count + " × " + type.unitName + " into the hold · " + cost.ToString("0") + " from your allocation";
            Plugin.Log.LogInfo("[amphib] " + ShipNames.Of(ship) + ": " + reason);
            return true;
        }

        // ---- a craft being made ready ---------------------------------------------

        internal sealed class Plan
        {
            internal readonly Dictionary<UnitDefinition, int> Load = new Dictionary<UnitDefinition, int>();
            internal bool HasPoint;
            internal Vector3 Point, Ashore;
            internal bool Predicted;
            internal string Hint;
        }

        private static readonly Dictionary<Ship, Plan> plans = new Dictionary<Ship, Plan>();

        internal static Plan PlanFor(Ship ship)
        {
            if (!plans.TryGetValue(ship, out Plan plan)) plans[ship] = plan = new Plan();
            return plan;
        }

        internal static float LoadMass(Plan plan)
        {
            float mass = 0f;
            foreach (var entry in plan.Load) mass += entry.Key.mass * entry.Value;
            return mass;
        }

        internal static int LoadCount(Plan plan)
        {
            int count = 0;
            foreach (var entry in plan.Load) count += entry.Value;
            return count;
        }

        // One more of this vehicle, if the hold has it and the craft can take
        // it; past either, back to none.
        internal static void Cycle(WellDeck deck, Plan plan, UnitDefinition type)
        {
            plan.Load.TryGetValue(type, out int loaded);
            bool more = loaded < Count(deck.Hold, type) && LoadMass(plan) + type.mass <= CraftCapacity(deck) + 0.5f;
            if (more) plan.Load[type] = loaded + 1;
            else plan.Load.Remove(type);
        }

        // The preview under the cursor, re-solved only when the cursor moves
        // far enough to matter.
        private static Vector3 previewFor = new Vector3(float.NaN, 0f, 0f);
        private static Vector3 previewAshore;
        private static bool previewLands;
        private static string previewHint;

        internal static bool Preview(Vector3 point, out Vector3 ashore, out string hint)
        {
            if (float.IsNaN(previewFor.x) || (point - previewFor).sqrMagnitude > 25f * 25f)
            {
                previewFor = point;
                previewLands = AmphibSurvey.Assess(point, out previewAshore, out previewHint);
            }
            ashore = previewAshore;
            hint = previewHint;
            return previewLands;
        }

        internal static void SetLandingPoint(Ship ship, Vector3 point)
        {
            Plan plan = PlanFor(ship);
            plan.HasPoint = true;
            plan.Point = point;
            plan.Predicted = AmphibSurvey.Assess(point, out plan.Ashore, out plan.Hint);
        }

        // ---- launching -------------------------------------------------------------

        internal sealed class Sortie
        {
            internal Ship Carrier, Craft;
            internal LandingCraftAI Ai;
            internal UnitStorage Hold;
            internal string Name, Load;
            internal Vector3 Point, Ashore;
            internal float LaunchedAt;
            internal bool Ordered, Unloaded, Recalled, Launching = true;
            internal int Orders;
            internal float NextOrder;
        }

        private static readonly List<Sortie> sorties = new List<Sortie>();
        private static readonly HashSet<Ship> deckBusy = new HashSet<Ship>();

        internal static IEnumerable<Sortie> SortiesFrom(Ship carrier)
        {
            foreach (Sortie sortie in sorties) if (sortie.Carrier == carrier) yield return sortie;
        }

        internal static bool DeckBusy(Ship ship) => deckBusy.Contains(ship);

        // A carrier the player is working with: commanded now, or with a
        // plan, purchases or craft of ours out.
        internal static bool Managed(Ship ship)
        {
            if (ship == null) return false;
            if (ship == CommandState.Ship || plans.ContainsKey(ship) || bought.Contains(ship)) return true;
            foreach (Sortie sortie in sorties) if (sortie.Carrier == ship) return true;
            return false;
        }

        private static readonly HashSet<Ship> bought = new HashSet<Ship>();

        internal static bool Launch(Ship ship, out string reason)
        {
            WellDeck deck = Deck(ship);
            if (deck == null) { reason = "This ship has no well deck."; return false; }
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            Plan plan = PlanFor(ship);
            if (Count(deck.Hold, deck.Craft) <= 0) { reason = "No landing craft in the hold · buy one first."; return false; }
            if (LoadCount(plan) == 0) { reason = "Load at least one vehicle."; return false; }
            if (!plan.HasPoint) { reason = "Choose a landing point on the map first."; return false; }
            if (deckBusy.Contains(ship)) { reason = "The well deck is still clearing the last craft."; return false; }
            foreach (var entry in plan.Load)
                if (Count(deck.Hold, entry.Key) < entry.Value) { reason = "The hold no longer has " + entry.Value + " × " + entry.Key.unitName + "."; return false; }

            var load = new Dictionary<UnitDefinition, int>(plan.Load);
            deckBusy.Add(ship);
            var runner = ship.gameObject.GetComponent<AmphibRunner>() ?? ship.gameObject.AddComponent<AmphibRunner>();
            runner.StartCoroutine(LaunchCraft(deck, load, plan.Point, plan.Predicted ? plan.Ashore : plan.Point));
            plan.Load.Clear();
            reason = deck.Craft.unitName + " launching · " + Describe(load);
            return true;
        }

        internal static string Describe(Dictionary<UnitDefinition, int> load)
        {
            var text = new StringBuilder();
            foreach (var entry in load)
                text.Append(text.Length > 0 ? ", " : "").Append(entry.Value).Append(" × ").Append(entry.Key.unitName);
            return text.Length > 0 ? text.ToString() : "empty";
        }

        // The game's own deploy, one craft at a time and with a load we chose:
        // gate open, the craft spawned at the hold's deploy point (not the
        // stern gate, which GetDoorTransform returns) and handed to the rail,
        // its load put aboard, launched, then sent to the point once its own
        // launch run has cleared the ship.
        private static IEnumerator LaunchCraft(WellDeck deck, Dictionary<UnitDefinition, int> load, Vector3 point, Vector3 ashore)
        {
            Ship carrier = deck.Ship;
            UnitStorage hold = deck.Hold;
            float waited = 0f;
            while (!hold.DoorsOpen() && waited < 30f)
            {
                hold.OpenDoors();
                waited += 0.5f;
                yield return new WaitForSeconds(0.5f);
                if (carrier == null || carrier.disabled) { deckBusy.Remove(carrier); yield break; }
            }

            // Out of the hold only now it is really going.
            foreach (var entry in load)
                if (Count(hold, entry.Key) < entry.Value)
                {
                    CommandState.Say("Launch stopped · the hold no longer has " + entry.Value + " × " + entry.Key.unitName);
                    deckBusy.Remove(carrier);
                    yield break;
                }
            hold.AddOrRemoveUnit(deck.Craft, -1);
            foreach (var entry in load) hold.AddOrRemoveUnit(entry.Key, -entry.Value);

            Transform at = DeployTransform?.GetValue(hold) as Transform ?? hold.GetDoorTransform();
            Unit spawned = NetworkSceneSingleton<Spawner>.i.SpawnUnit(deck.Craft, at.position, at.rotation,
                carrier.rb != null ? carrier.rb.GetPointVelocity(at.position) : Vector3.zero, carrier, null);
            var craft = spawned as Ship;
            if (craft == null)
            {
                // Nothing went: put it all back.
                hold.AddOrRemoveUnit(deck.Craft, 1);
                foreach (var entry in load) hold.AddOrRemoveUnit(entry.Key, entry.Value);
                CommandState.Say("The landing craft could not be launched");
                deckBusy.Remove(carrier);
                yield break;
            }
            LastDeployed?.SetValue(hold, craft);
            hold.enabled = true;                                   // the rail runs while the craft is close
            UnitStorage cargo = craft.GetComponentInChildren<UnitStorage>(true);
            if (cargo != null) foreach (var entry in load) cargo.AddOrRemoveUnit(entry.Key, entry.Value);
            craft.Launch();

            var sortie = new Sortie
            {
                Carrier = carrier, Craft = craft, Ai = craft.GetComponent<LandingCraftAI>(), Hold = hold,
                Name = ShipNames.Of(craft), Load = Describe(load), Point = point, Ashore = ashore,
                LaunchedAt = Time.timeSinceLevelLoad
            };
            sorties.Add(sortie);
            Plugin.Log.LogInfo("[amphib] " + ShipNames.Of(carrier) + " launched " + sortie.Name + " · " + sortie.Load);

            // The gate stays open while it clears, as the game keeps it.
            while (Time.timeSinceLevelLoad - sortie.LaunchedAt < 12f && craft != null && !craft.disabled)
            {
                hold.OpenDoors();
                yield return new WaitForSeconds(1f);
            }
            deckBusy.Remove(carrier);
            sortie.Launching = false;
        }

        internal static void Recall(Sortie sortie)
        {
            if (sortie?.Ai == null || sortie.Craft == null || sortie.Craft.disabled) return;
            sortie.Recalled = true;
            HomeDock?.SetValue(sortie.Ai, sortie.Hold);
            LastDestination?.SetValue(sortie.Ai, -100f);
            sortie.Ai.state = ShipAI.ShipAIState.returning;
            CommandState.Say(sortie.Name + " · returning to " + ShipNames.Of(sortie.Carrier));
        }

        internal static string Status(Sortie sortie)
        {
            if (sortie.Craft == null || sortie.Craft.disabled) return "gone";
            if (sortie.Launching && !sortie.Ordered) return "leaving the well deck";
            float toBeach = Vector3.Distance(sortie.Craft.transform.position, sortie.Ashore);
            float toShip = sortie.Carrier != null ? Vector3.Distance(sortie.Craft.transform.position, sortie.Carrier.transform.position) : 0f;
            switch (sortie.Ai != null ? sortie.Ai.state : ShipAI.ShipAIState.holding)
            {
                case ShipAI.ShipAIState.launching: return "leaving the well deck";
                case ShipAI.ShipAIState.landing:
                case ShipAI.ShipAIState.navigating: return "heading in · " + UnitConverter.DistanceReading(toBeach) + " to the beach";
                case ShipAI.ShipAIState.unloading: return "ashore · unloading";
                case ShipAI.ShipAIState.returning: return "returning · " + UnitConverter.DistanceReading(toShip);
                case ShipAI.ShipAIState.docking: return "docking";
                case ShipAI.ShipAIState.docked: return "docked";
                default: return sortie.Unloaded ? "waiting" : "holding · " + UnitConverter.DistanceReading(toBeach) + " short";
            }
        }

        // Sent to the point, then its destination moved on inland along its
        // own run-in. The craft stops and holds once within its radius plus
        // 100 m of its destination, a check that comes before its beaching
        // check, and its destination is the very point its line meets the
        // shore -- so a craft reaching that close while still over the
        // shallows held there for good, never landing and never unloading.
        private const float PushInland = 150f;

        private static void Order(Sortie sortie)
        {
            Ship craft = sortie.Craft;
            craft.UnitCommand.SetDestination(sortie.Point.ToGlobalPosition(), true);
            if (sortie.Ai == null || sortie.Ai.state != ShipAI.ShipAIState.landing || Destination == null || ShoreDirection == null) return;
            if (!(ShoreDirection.GetValue(sortie.Ai) is Vector3 inward) || inward.sqrMagnitude < 1f) return;
            inward.y = 0f;
            GlobalPosition goal = (GlobalPosition)Destination.GetValue(sortie.Ai);
            Destination.SetValue(sortie.Ai, goal + inward.normalized * (craft.maxRadius + PushInland));
        }

        private static bool OnLand(Sortie sortie) =>
            sortie.Ai != null && Cushion?.GetValue(sortie.Ai) is AirCushion cushion && cushion.Landed();

        // What LandingCraftAI does itself when it touches down in its landing
        // run: let the cushion down, unload, and go home when done.
        private static void Beach(Sortie sortie)
        {
            if (!(Cushion?.GetValue(sortie.Ai) is AirCushion cushion) || WaitDeployUnits == null) return;
            cushion.Deflate();
            WaitDeployUnits.Invoke(sortie.Ai, null);
            sortie.Ai.state = ShipAI.ShipAIState.unloading;
            Plugin.Log.LogInfo("[amphib] " + sortie.Name + " held on the beach; landing it");
        }

        // ---- every frame -------------------------------------------------------------

        private static object tickLevel;

        internal static void Tick()
        {
            // A mission ending takes every craft with it; that is not a loss
            // to report, and nothing from it carries into the next mission.
            if (!MissionManager.IsRunning) return;
            object level = NetworkSceneSingleton<LevelInfo>.i;
            if (!ReferenceEquals(level, tickLevel))
            {
                tickLevel = level;
                sorties.Clear();
                plans.Clear();
                decks.Clear();
                without.Clear();
                bought.Clear();
                deckBusy.Clear();
                return;
            }
            for (int i = sorties.Count - 1; i >= 0; i--)
            {
                Sortie sortie = sorties[i];
                Ship craft = sortie.Craft;
                if (craft == null || craft.disabled)
                {
                    bool docked = craft != null && craft.unitState == Unit.UnitState.Returned;
                    CommandState.Say(sortie.Name + (docked ? " · back aboard " + ShipNames.Of(sortie.Carrier)
                        : sortie.Unloaded ? " · lost after landing its load" : " · lost with its load"));
                    Plugin.Log.LogInfo("[amphib] " + sortie.Name + (docked ? " docked" : " lost") + (sortie.Unloaded ? " after unloading" : " before unloading"));
                    sorties.RemoveAt(i);
                    continue;
                }
                sortie.Name = ShipNames.Of(craft);                 // named a moment after it spawns
                ShipAI.ShipAIState state = sortie.Ai != null ? sortie.Ai.state : ShipAI.ShipAIState.holding;
                if (state == ShipAI.ShipAIState.unloading && !sortie.Unloaded)
                {
                    sortie.Unloaded = true;
                    CommandState.Say(sortie.Name + " · ashore, unloading");
                }
                float age = Time.timeSinceLevelLoad - sortie.LaunchedAt;

                // Its own launch run done, off to the beach -- and home is
                // this ship, whatever else is nearer when it looks for one.
                if (!sortie.Ordered && !sortie.Recalled && age > 12f)
                {
                    sortie.Ordered = true;
                    sortie.Orders = 1;
                    HomeDock?.SetValue(sortie.Ai, sortie.Hold);
                    Order(sortie);
                }
                // Holding without having unloaded. On land already: land it,
                // the way the game does when it beaches properly. Short of the
                // beach on the water: send it in again, a few times.
                if (sortie.Ordered && !sortie.Unloaded && !sortie.Recalled && state == ShipAI.ShipAIState.holding && age > 30f)
                {
                    if (OnLand(sortie)) Beach(sortie);
                    else if (sortie.Orders < 5 && Time.timeSinceLevelLoad >= sortie.NextOrder)
                    {
                        sortie.Orders++;
                        sortie.NextOrder = Time.timeSinceLevelLoad + 8f;
                        Order(sortie);
                    }
                }
                // On its way back, home stays home.
                if ((state == ShipAI.ShipAIState.returning || state == ShipAI.ShipAIState.docking) && sortie.Hold != null)
                    HomeDock?.SetValue(sortie.Ai, sortie.Hold);
            }
        }
    }

    // Hosts the launch coroutines on the carrier.
    internal sealed class AmphibRunner : MonoBehaviour { }

    // A carrier the player is working with does not empty its own hold: the
    // game's amphibious AI deploys everything aboard once it is near an
    // objective, which would launch what the player bought and was saving.
    // Other Annexes of the faction keep their own AI.
    [HarmonyPatch(typeof(AssaultCarrierAI), "DeployCargo")]
    internal static class HoldOwnCargoPatch
    {
        private const string Name = "Amphibious hold";
        private static readonly FieldInfo ShipOf = AccessTools.Field(typeof(ShipAI), "ship");

        private static bool Prefix(AssaultCarrierAI __instance)
        {
            if (!Guard.Ok(Name)) return true;
            try
            {
                return !(ShipOf?.GetValue(__instance) is Ship ship && Amphib.Managed(ship));
            }
            catch (Exception ex) { Guard.Failed(Name, ex); return true; }
        }
    }
}

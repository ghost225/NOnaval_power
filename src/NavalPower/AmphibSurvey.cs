using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace NavalPower
{
    // Survey for landing craft (1.0.2 plan, phase 1). Logs, once per mission
    // and again whenever command moves to a ship with a hold:
    //
    //  - every ship with a UnitStorage: its hold's limits, what it holds,
    //    which types it deploys, and which AI drives it;
    //  - every landing craft in the scene, and its own hold;
    //  - a first pass of the beach finder: sea-lane points whose nearest
    //    road leads ashore over low, gently sloped ground -- the same snap
    //    and shore linecast LandingCraftAI.SetDestination does -- clustered,
    //    with bearing and range from each amphibious ship.
    //
    // Nothing here changes the game; it only reads.
    internal static class AmphibSurvey
    {
        private static readonly FieldInfo DeployableTypes = AccessTools.Field(typeof(UnitStorage), "deployableTypes");
        private static readonly FieldInfo CurrentMass = AccessTools.Field(typeof(UnitStorage), "currentMass");
        private static readonly FieldInfo Deploying = AccessTools.Field(typeof(UnitStorage), "deployingUnits");

        private static object surveyedLevel;
        private static float surveyAt = -1f;
        private static readonly HashSet<Ship> described = new HashSet<Ship>();
        private static Ship lastCommanded;

        internal static void Tick()
        {
            if (!MissionManager.IsRunning) return;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null) return;

            // A new mission: survey it once, after things have settled.
            if (!ReferenceEquals(level, surveyedLevel))
            {
                surveyedLevel = level;
                described.Clear();
                surveyAt = Time.timeSinceLevelLoad + 15f;
            }
            if (surveyAt > 0f && Time.timeSinceLevelLoad >= surveyAt)
            {
                surveyAt = -1f;
                Guard.Run("Amphibious survey", Survey);
            }

            // Command moved to a ship with a hold: describe it again, now.
            Ship ship = CommandState.Ship;
            if (ship != lastCommanded)
            {
                lastCommanded = ship;
                if (ship != null && ship.GetComponentInChildren<UnitStorage>(true) != null)
                    Guard.Run("Amphibious survey", () => Plugin.Log.LogInfo(DescribeShip(ship)));
            }
        }

        private static void Survey()
        {
            var ships = new List<Ship>();
            var craft = new List<Ship>();
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Ship ship) || ship.disabled) continue;
                if (ship.GetComponentInChildren<UnitStorage>(true) == null) continue;
                if (ship.GetComponent<LandingCraftAI>() != null) craft.Add(ship);
                else ships.Add(ship);
            }

            var text = new StringBuilder("[amphib] survey · " + ships.Count + " ship(s) with a hold, " +
                craft.Count + " landing craft in the scene");
            foreach (Ship ship in ships) text.Append('\n').Append(DescribeShip(ship));
            foreach (Ship ship in craft) text.Append('\n').Append(DescribeShip(ship));
            Plugin.Log.LogInfo(text.ToString());

            Plugin.Log.LogInfo(Beaches(ships));
            Plugin.Log.LogInfo(Catalogue());
        }

        // Every ship type the game knows -- the base game's and every mod's,
        // since mods register theirs in the same encyclopedia -- checked on its
        // prefab rather than in the scene: which have a hold, what they
        // launch, whether the hold has doors that animate, and what AI drives
        // them.
        private static string Catalogue()
        {
            if (Encyclopedia.Lookup == null) return "[amphib] catalogue · encyclopedia not loaded";
            int ships = 0;
            var text = new StringBuilder();
            var seen = new HashSet<UnitDefinition>();
            foreach (UnitDefinition definition in Encyclopedia.Lookup.Values)
            {
                if (definition == null || !seen.Add(definition) || definition.unitPrefab == null) continue;
                if (definition.unitPrefab.GetComponent<Ship>() == null) continue;
                ships++;
                UnitStorage[] holds = definition.unitPrefab.GetComponentsInChildren<UnitStorage>(true);
                if (holds.Length == 0) continue;
                text.Append("\n    ").Append(definition.unitName).Append(" (").Append(definition.jsonKey).Append(")")
                    .Append(" · AI ").Append(definition.unitPrefab.GetComponent<ShipAI>()?.GetType().Name ?? "none");
                foreach (UnitStorage hold in holds)
                {
                    var types = DeployableTypes?.GetValue(hold) as List<UnitDefinition>;
                    text.Append("\n      hold '").Append(hold.name).Append("' · ")
                        .Append((hold.MassLimit / 1000f).ToString("0")).Append(" t · doors ")
                        .Append(AmphibSurveyAccess.DoorCount(hold)).Append(" · launches ")
                        .Append(types == null ? "?" : types.Count == 0 ? "anything"
                            : string.Join(", ", types.ConvertAll(t => t != null ? t.unitName : "null")));
                }
            }
            return "[amphib] catalogue · " + ships + " ship type(s) known" +
                (text.Length == 0 ? " · none has a hold" : text.ToString());
        }

        private static string DescribeShip(Ship ship)
        {
            var text = new StringBuilder("[amphib] " + ShipNames.Of(ship) + " [" + ShipNames.TypeOf(ship) + "]" +
                " · " + (ship.NetworkHQ?.faction?.factionName ?? "no faction") +
                " · AI " + (ship.GetComponent<ShipAI>()?.GetType().Name ?? "none") +
                " · holding " + ship.holdPosition);
            foreach (UnitStorage storage in ship.GetComponentsInChildren<UnitStorage>(true))
            {
                text.Append("\n    hold '").Append(storage.name).Append("'")
                    .Append(" · mass limit ").Append(storage.MassLimit.ToString("0")).Append(" kg")
                    .Append(" · counted ").Append(Read<float>(CurrentMass, storage).ToString("0")).Append(" kg")
                    .Append(" · deploying ").Append(Read<bool>(Deploying, storage))
                    .Append(" · doors ").Append(storage.DoorsOpen() ? "open" : "closed")
                    .Append(" · incoming ").Append(storage.CheckIncomingCount());

                var types = DeployableTypes?.GetValue(storage) as List<UnitDefinition>;
                text.Append("\n      deploys: ").Append(types == null ? "?" : types.Count == 0 ? "everything"
                    : string.Join(", ", types.ConvertAll(t => t != null ? t.unitName + " (" + t.jsonKey + ")" : "null")));

                List<UnitCount> stored = storage.GetStoredList();
                if (stored == null || stored.Count == 0) { text.Append("\n      holds: nothing"); continue; }
                text.Append("\n      holds:");
                foreach (UnitCount entry in stored)
                {
                    UnitDefinition definition = null;
                    if (Encyclopedia.Lookup != null) Encyclopedia.Lookup.TryGetValue(entry.UnitType, out definition);
                    text.Append("\n        ").Append(entry.Count).Append(" × ")
                        .Append(definition != null ? definition.unitName : "?").Append(" (").Append(entry.UnitType).Append(")");
                    if (definition != null)
                        text.Append(" · ").Append(definition.mass.ToString("0")).Append(" kg each")
                            .Append(" · value ").Append(definition.value.ToString("0"))
                            .Append(" · fits ").Append(storage.CanFit(definition));
                }
            }
            return text.ToString();
        }

        // ---- beaches -----------------------------------------------------------

        private const float SampleEvery = 250f;       // metres between sea-lane samples
        private const float ReachFromSea = 3000f;     // a road this far from the lane is not this beach's
        private const float MaxHeight = 10f;          // above sea level, a little inland
        private const float InlandProbe = 40f;        // how far past the waterline the ground is judged
        private const float MinUpright = 0.9f;        // ground normal: about 25 degrees of slope at most
        private const float ClusterRadius = 600f;

        internal sealed class Beach
        {
            internal Vector3 Point;         // where the shore line strikes land
            internal Vector3 Inland;        // a little further in: where to send a craft
            internal float Height, Slope;   // of the ground inland, which the craft must climb onto
            internal int Hits;
        }

        private static List<Beach> found;
        private static object foundFor;

        // Found once per map, and kept.
        internal static List<Beach> Known()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (found == null || !ReferenceEquals(foundFor, level)) { foundFor = level; Beaches(new List<Ship>()); }
            return found ?? new List<Beach>();
        }

        private static string Beaches(List<Ship> amphibs)
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            var lanes = level?.seaLanes;
            var roads = level?.roadNetwork;
            if (lanes == null || roads == null || lanes.roads == null)
                return "[amphib] beaches · no sea lanes or road network on this map";

            var beaches = new List<Beach>();
            int samples = 0, reached = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var lane in lanes.roads)
            {
                if (lane?.points == null) continue;
                float carried = SampleEvery;
                for (int i = 1; i < lane.points.Count; i++)
                {
                    Vector3 a = lane.points[i - 1].ToLocalPosition(), b = lane.points[i].ToLocalPosition();
                    float span = Vector3.Distance(a, b);
                    for (float along = SampleEvery - carried; along < span; along += SampleEvery)
                    {
                        samples++;
                        Vector3 sea = Vector3.Lerp(a, b, span > 0.01f ? along / span : 0f);
                        if (TryLanding(roads, sea, out Beach found)) { reached++; Merge(beaches, found); }
                    }
                    carried = (carried + span) % SampleEvery;
                }
            }

            found = beaches;
            var text = new StringBuilder("[amphib] beaches · " + samples + " sea-lane samples, " + reached +
                " reach a gentle shore, " + beaches.Count + " beach(es) · " + clock.ElapsedMilliseconds + " ms");
            beaches.Sort((x, y) => y.Hits.CompareTo(x.Hits));
            int shown = 0;
            foreach (Beach beach in beaches)
            {
                if (shown++ >= 12) { text.Append("\n    … ").Append(beaches.Count - 12).Append(" more"); break; }
                text.Append("\n    beach ").Append(shown).Append(" · ").Append(beach.Hits).Append(" sample(s)")
                    .Append(" · ").Append(beach.Height.ToString("0.0")).Append(" m up · slope ")
                    .Append(beach.Slope.ToString("0")).Append("°");
                foreach (Ship ship in amphibs)
                {
                    Vector3 offset = beach.Point - ship.transform.position;
                    offset.y = 0f;
                    float bearing = (Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg + 360f) % 360f;
                    text.Append(" · ").Append(ShipNames.Of(ship)).Append(' ')
                        .Append((offset.magnitude / 1000f).ToString("0.0")).Append(" km ")
                        .Append(bearing.ToString("000")).Append('°');
                }
            }
            return text.ToString();
        }

        // The craft's own reasoning, from a point on a sea lane: the nearest
        // road, then a line from the water to it, and where that line first
        // meets the ground is where it comes ashore.
        private static bool TryLanding(RoadPathfinding.RoadNetwork roads, Vector3 sea, out Beach beach)
        {
            beach = null;
            if (!roads.TryGetNearestPoint(sea.ToGlobalPosition(), out GlobalPosition roadPoint, out _)) return false;
            Vector3 road = roadPoint.ToLocalPosition();
            Vector3 flatSea = new Vector3(sea.x, Datum.LocalSeaY + 1f, sea.z);
            Vector3 flatRoad = new Vector3(road.x, Datum.LocalSeaY + 1f, road.z);
            if (Vector3.Distance(flatSea, flatRoad) > ReachFromSea) return false;
            // The road itself has to be on land for the craft to call this a landing.
            if (!Physics.Linecast(road + Vector3.up * 5000f, road - Vector3.up * 5000f, out RaycastHit ground, PhysicsLayers.StaticsMask)
                || ground.point.y <= Datum.LocalSeaY) return false;
            if (!Physics.Linecast(flatSea, flatRoad, out RaycastHit shore, PhysicsLayers.StaticsMask)) return false;

            // What the ground is like a little way in from the waterline,
            // which is what the craft has to climb onto. At the waterline
            // itself the line only ever finds ground a metre up.
            Vector3 inward = flatRoad - flatSea;
            inward.y = 0f;
            Vector3 probe = shore.point + inward.normalized * InlandProbe;
            if (!Physics.Linecast(probe + Vector3.up * 200f, probe - Vector3.up * 50f, out RaycastHit top, PhysicsLayers.StaticsMask))
                return false;
            float height = top.point.y - Datum.LocalSeaY;
            if (height > MaxHeight || top.normal.y < MinUpright) return false;
            beach = new Beach
            {
                Point = shore.point,
                Inland = top.point,
                Height = height,
                Slope = Mathf.Acos(Mathf.Clamp01(top.normal.y)) * Mathf.Rad2Deg,
                Hits = 1
            };
            return true;
        }

        private static void Merge(List<Beach> beaches, Beach found)
        {
            foreach (Beach beach in beaches)
            {
                if ((beach.Point - found.Point).sqrMagnitude > ClusterRadius * ClusterRadius) continue;
                beach.Hits++;
                return;
            }
            beaches.Add(found);
        }

        private static T Read<T>(FieldInfo field, object from)
        {
            object value = field?.GetValue(from);
            return value is T typed ? typed : default;
        }
    }
}

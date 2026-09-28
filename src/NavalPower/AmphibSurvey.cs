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
        private const float ClusterRadius = 600f;

        internal sealed class Beach
        {
            internal Vector3 Order;         // what to send a craft to: a road point, on land
            internal Vector3 Point;         // where its own approach line strikes the shore
            internal Vector3 Inland;        // kept equal to Order for older callers
            internal float Height;          // rise over the first 60 m past the waterline
            internal float Slope;           // steepest 4 m of the climb, in degrees
            internal string AtShore;        // what the line strikes at the waterline
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
                    .Append(" · climbs ").Append(beach.Height.ToString("0.0")).Append(" m in 60 m · steepest ")
                    .Append(beach.Slope.ToString("0")).Append("° · shore '").Append(beach.AtShore).Append("'");
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

        // From a sea-lane sample: the nearest road is the candidate order, and
        // what the craft would do with that order is what gets judged.
        private static bool TryLanding(RoadPathfinding.RoadNetwork roads, Vector3 sea, out Beach beach)
        {
            beach = null;
            if (!roads.TryGetNearestPoint(sea.ToGlobalPosition(), out GlobalPosition roadPoint, out _)) return false;
            Vector3 order = roadPoint.ToLocalPosition();
            Vector3 flat = new Vector3(order.x - sea.x, 0f, order.z - sea.z);
            if (flat.magnitude > ReachFromSea) return false;
            return Judge(order, out beach);
        }

        // Send a craft to this point and where does it actually go? Its own
        // SetDestination, step for step: a point on land means a landing; the
        // nearest sea lane to the point; the nearest road to that; and a line
        // a metre above the sea from the one to the other, which comes ashore
        // where it first strikes. The line checked has to be the line it
        // flies -- the first finder judged a line of its own, and the craft
        // came in on a different one, straight into a concrete wall.
        internal static bool Predict(Vector3 order, out Vector3 sea, out Vector3 destination, out RaycastHit shore)
        {
            sea = destination = order;
            shore = default;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level?.seaLanes == null || level.roadNetwork == null) return false;
            if (!Physics.Linecast(order + Vector3.up * 5000f, order - Vector3.up * 5000f, out RaycastHit ground, PhysicsLayers.StaticsMask)
                || ground.point.y <= Datum.LocalSeaY) return false;             // on water it just sails there
            if (!level.seaLanes.TryGetNearestPoint(order.ToGlobalPosition(), out GlobalPosition lane, out _)) return false;
            sea = lane.ToLocalPosition();
            if (level.roadNetwork.TryGetNearestPoint(lane, out GlobalPosition road, out _)) destination = road.ToLocalPosition();
            sea.y = destination.y = Datum.LocalSeaY + 1f;
            return Physics.Linecast(sea, destination, out shore, PhysicsLayers.StaticsMask);
        }

        private const float ProfileStep = 4f, ProfileIn = 60f, ProfileOut = 40f;
        private const float MaxClimb = 15f;           // degrees, over any 4 m of the way in
        private const float MaxRise = 12f;            // metres, over the first 60 m
        private const float MinShoreUpright = 0.7f;   // the waterline itself: steeper than ~45 degrees is a wall

        // The whole way in, not one spot: the ground every 4 m along the
        // craft's line, from 40 m out to 60 m in.
        internal static bool Judge(Vector3 order, out Beach beach)
        {
            beach = null;
            if (!Predict(order, out Vector3 sea, out Vector3 destination, out RaycastHit shore)) return false;
            if (shore.normal.y < MinShoreUpright) return false;                  // a sea wall, quay or cliff
            Vector3 way = destination - sea;
            way.y = 0f;
            way.Normalize();
            float steepest = 0f, previous = float.NaN, rise = 0f;
            for (float d = -ProfileOut; d <= ProfileIn; d += ProfileStep)
            {
                Vector3 at = shore.point + way * d;
                float height = Datum.LocalSeaY;
                if (Physics.Linecast(at + Vector3.up * 300f, at - Vector3.up * 100f, out RaycastHit hit, PhysicsLayers.StaticsMask))
                {
                    height = Mathf.Max(hit.point.y, Datum.LocalSeaY);
                    if (d >= 0f && d <= 20f && hit.normal.y < MinShoreUpright && hit.point.y > Datum.LocalSeaY) return false;
                }
                if (!float.IsNaN(previous) && d > -ProfileStep)
                    steepest = Mathf.Max(steepest, Mathf.Atan2(height - previous, ProfileStep) * Mathf.Rad2Deg);
                previous = height;
                if (d >= ProfileIn - 0.01f) rise = height - Datum.LocalSeaY;
            }
            if (steepest > MaxClimb || rise > MaxRise) return false;
            beach = new Beach
            {
                Order = order,
                Inland = order,
                Point = shore.point,
                Height = rise,
                Slope = steepest,
                AtShore = shore.collider != null ? shore.collider.name : "?",
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
                // The gentlest way in stands for the whole beach.
                if (found.Slope < beach.Slope)
                {
                    beach.Order = beach.Inland = found.Order; beach.Point = found.Point;
                    beach.Slope = found.Slope; beach.Height = found.Height; beach.AtShore = found.AtShore;
                }
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

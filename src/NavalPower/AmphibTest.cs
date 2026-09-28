using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    // Landing craft test (1.0.2 plan, phase 1b). From the Annex's own menu:
    // put a couple of the faction's vehicles into its hold for free, open the
    // well deck, launch one landing craft filled from the hold, send it to
    // the nearest beach the survey found, and log each stage -- launch,
    // transit, beaching, unloading, return, docking -- so we learn whether
    // the game's own landing craft AI does the job before building on it.
    internal static class AmphibTest
    {
        internal static bool Available(Ship ship, out UnitStorage hold, out UnitDefinition craft)
        {
            hold = null; craft = null;
            if (ship == null) return false;
            foreach (UnitStorage storage in ship.GetComponentsInChildren<UnitStorage>(true))
                foreach (UnitDefinition type in AmphibSurveyAccess.Deployable(storage))
                    if (type?.unitPrefab != null && type.unitPrefab.GetComponent<LandingCraftAI>() != null)
                    { hold = storage; craft = type; return true; }
            return false;
        }

        internal static void Start(Ship carrier)
        {
            if (!Available(carrier, out UnitStorage hold, out UnitDefinition craft))
            { CommandState.Say("This ship has no well deck"); return; }
            if (carrier.GetComponent<AmphibTestRun>() != null) { CommandState.Say("A landing craft test is already running"); return; }

            AmphibSurvey.Beach beach = null;
            float best = float.MaxValue;
            foreach (AmphibSurvey.Beach candidate in AmphibSurvey.Known())
            {
                float d = (candidate.Inland - carrier.transform.position).sqrMagnitude;
                if (d < best) { best = d; beach = candidate; }
            }
            if (beach == null) { CommandState.Say("No beach found on this map"); return; }

            var run = carrier.gameObject.AddComponent<AmphibTestRun>();
            run.Begin(carrier, hold, craft, beach);
            CommandState.Say("Landing craft test started · watch the log");
        }
    }

    // Reads the hold's private list of deployable types, shared with the survey.
    internal static class AmphibSurveyAccess
    {
        private static readonly FieldInfo DeployableTypes = AccessTools.Field(typeof(UnitStorage), "deployableTypes");
        private static readonly FieldInfo Doors = AccessTools.Field(typeof(UnitStorage), "doors");
        private static readonly FieldInfo LastDeployed = AccessTools.Field(typeof(UnitStorage), "lastDeployedUnit");
        private static readonly FieldInfo Rail = AccessTools.Field(typeof(UnitStorage), "deployRail");

        // What the game's own deploy does after spawning: the hold steers the
        // new unit out along its rail each physics step -- centred, squared
        // up, eased outward -- until it is clear. Without it the craft
        // slewed into the well deck's walls and was destroyed.
        internal static bool GuideOut(UnitStorage storage, Unit unit)
        {
            if (LastDeployed == null) return false;
            LastDeployed.SetValue(storage, unit);
            storage.enabled = true;
            return Rail?.GetValue(storage) is bool rail && rail;
        }

        internal static List<UnitDefinition> Deployable(UnitStorage storage) =>
            DeployableTypes?.GetValue(storage) as List<UnitDefinition> ?? new List<UnitDefinition>();

        internal static int DoorCount(UnitStorage storage) =>
            Doors?.GetValue(storage) is System.Array doors ? doors.Length : -1;
    }

    internal sealed class AmphibTestRun : MonoBehaviour
    {
        private static readonly FieldInfo Cushion = AccessTools.Field(typeof(LandingCraftAI), "airCushion");

        private Ship carrier, craft;
        private UnitStorage hold;
        private UnitDefinition craftType;
        private AmphibSurvey.Beach beach;

        internal void Begin(Ship carrier, UnitStorage hold, UnitDefinition craftType, AmphibSurvey.Beach beach)
        {
            this.carrier = carrier; this.hold = hold; this.craftType = craftType; this.beach = beach;
            StartCoroutine(Run());
        }

        private static void Log(string line) => Plugin.Log.LogInfo("[amphib-test] " + line);

        private string Where(Vector3 point)
        {
            Vector3 offset = point - carrier.transform.position;
            offset.y = 0f;
            float bearing = (Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg + 360f) % 360f;
            return (offset.magnitude / 1000f).ToString("0.0") + " km " + bearing.ToString("000") + "° from " + ShipNames.Of(carrier);
        }

        private IEnumerator Run()
        {
            Log("start · " + ShipNames.Of(carrier) + " · craft " + craftType.unitName + " · doors " +
                AmphibSurveyAccess.DoorCount(hold) + " · beach " + Where(beach.Inland) + " · inland " +
                beach.Height.ToString("0.0") + " m up, slope " + beach.Slope.ToString("0") + "°");

            // A couple of the faction's own convoy vehicles, lightest first.
            var loaded = new StringBuilder();
            var types = new List<UnitDefinition>();
            foreach (var group in carrier.NetworkHQ?.faction?.GetConvoyGroups() ?? new List<Faction.ConvoyGroup>())
                foreach (var unit in group.Constituents)
                    if (unit?.Type != null && !types.Contains(unit.Type)) types.Add(unit.Type);
            types.Sort((a, b) => a.mass.CompareTo(b.mass));
            for (int i = 0; i < types.Count && i < 2; i++)
            {
                hold.AddOrRemoveUnit(types[i], 2);
                loaded.Append(i > 0 ? ", " : "").Append("2 × ").Append(types[i].unitName)
                    .Append(" (").Append(types[i].mass.ToString("0")).Append(" kg)");
            }
            Log("into the hold · " + (loaded.Length > 0 ? loaded.ToString() : "nothing: no convoy vehicles for this faction"));

            // The well deck, the way the game opens it.
            float waited = 0f;
            while (!hold.DoorsOpen() && waited < 30f)
            {
                hold.OpenDoors();
                waited += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }
            Log(hold.DoorsOpen() ? "doors open after " + waited.ToString("0.0") + " s" : "doors never reported open; launching anyway");

            Transform door = hold.GetDoorTransform();
            Unit spawned = NetworkSceneSingleton<Spawner>.i.SpawnUnit(craftType, door.position, door.rotation,
                carrier.rb != null ? carrier.rb.GetPointVelocity(door.position) : Vector3.zero, carrier, null);
            craft = spawned as Ship;
            if (craft == null) { Log("spawn failed"); Destroy(this); yield break; }
            bool rail = AmphibSurveyAccess.GuideOut(hold, craft);
            UnitStorage cargo = craft.GetComponentInChildren<UnitStorage>(true);
            if (cargo != null) cargo.TryFillFromStorage(hold);
            Log("guided out along the deck rail: " + rail);
            Log("spawned " + ShipNames.Of(craft) + " · its hold: " + Contents(cargo) + " · left in the carrier: " + Contents(hold));
            craft.Launch();
            float launched = Time.timeSinceLevelLoad;

            // Keep the gate open while it clears the deck, as the game does,
            // and say where it is relative to the door every half second:
            // along the deck, off the centreline, and turned from it.
            while (Time.timeSinceLevelLoad - launched < 12f)
            {
                hold.OpenDoors();
                yield return new WaitForSeconds(0.5f);
                if (craft == null || craft.disabled)
                {
                    Log("destroyed " + (Time.timeSinceLevelLoad - launched).ToString("0.0") + " s after launch");
                    break;
                }
                Vector3 offset = craft.transform.position - door.position;
                float yaw = Vector3.SignedAngle(door.forward, craft.transform.forward, Vector3.up);
                Log("  clearing · " + Vector3.Dot(offset, door.forward).ToString("0") + " m along · " +
                    Vector3.Dot(offset, door.right).ToString("0.0") + " m off centre · " +
                    (offset.y).ToString("0.0") + " m up · turned " + yaw.ToString("0") + "° · " + craft.speed.ToString("0.0") + " m/s");
            }

            // Its own launch run is over; now the beach.
            int orders = 0;
            if (craft != null && !craft.disabled) { Order(); orders++; }

            LandingCraftAI ai = craft != null ? craft.GetComponent<LandingCraftAI>() : null;
            ShipAI.ShipAIState last = (ShipAI.ShipAIState)(-1);
            float nextReport = 0f, started = Time.timeSinceLevelLoad;
            bool unloaded = false;
            while (craft != null && !craft.disabled && Time.timeSinceLevelLoad - started < 900f)
            {
                ShipAI.ShipAIState state = ai != null ? ai.state : ShipAI.ShipAIState.holding;
                if (state == ShipAI.ShipAIState.unloading) unloaded = true;
                bool changed = state != last;
                if (changed || Time.timeSinceLevelLoad >= nextReport)
                {
                    nextReport = Time.timeSinceLevelLoad + 10f;
                    Log((changed ? "→ " : "  ") + state + " · " + Where(craft.transform.position) +
                        " · to beach " + (Vector3.Distance(craft.transform.position, beach.Inland) / 1000f).ToString("0.00") + " km" +
                        " · " + craft.speed.ToString("0.0") + " m/s · cushion " + CushionState(ai) + " · hold " + Contents(cargo));
                    last = state;
                }
                // Stopped short of the beach without landing: ask again, a few times.
                if (!unloaded && state == ShipAI.ShipAIState.holding && orders < 4 && Time.timeSinceLevelLoad - launched > 20f)
                {
                    Log("holding short of the beach · ordering again");
                    Order(); orders++;
                }
                yield return new WaitForSeconds(2f);
            }

            Log(craft == null || craft.disabled
                ? "craft gone" + (unloaded ? " after unloading" : " before unloading") + " · carrier hold now: " + Contents(hold)
                : "gave up watching after 15 min · state " + (ai != null ? ai.state.ToString() : "?"));
            Destroy(this);
        }

        private void Order()
        {
            craft.UnitCommand.SetDestination(beach.Inland.ToGlobalPosition(), true);
            Log("ordered to the beach · " + Where(beach.Inland));
        }

        private static string CushionState(LandingCraftAI ai)
        {
            if (ai == null || Cushion == null) return "?";
            var cushion = Cushion.GetValue(ai) as AirCushion;
            return cushion == null ? "none" : cushion.Landed() ? "landed" : "afloat";
        }

        private static string Contents(UnitStorage storage)
        {
            List<NuclearOption.SavedMission.UnitCount> list = storage?.GetStoredList();
            if (list == null || list.Count == 0) return "empty";
            var text = new StringBuilder();
            foreach (var entry in list)
            {
                if (text.Length > 0) text.Append(", ");
                string name = Encyclopedia.Lookup != null && Encyclopedia.Lookup.TryGetValue(entry.UnitType, out UnitDefinition d) ? d.unitName : entry.UnitType;
                text.Append(entry.Count).Append(" × ").Append(name);
            }
            return text.ToString();
        }
    }
}

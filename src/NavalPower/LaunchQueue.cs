using System.Collections.Generic;
using UnityEngine;

namespace NavalPower
{
    // Launches waiting for a hangar.
    //
    // Airbase.TrySpawnAircraft needs a hangar free at that moment and simply
    // refuses otherwise, and a carrier has one or two. So a launch is queued
    // here and fed to the hangars as they come free, in order -- which is what
    // lets a wing of four go up off a deck that can only build one at a time,
    // and a single launch wait its turn instead of being refused.
    //
    // Each aircraft is paid for as it actually launches, so a wing you cannot
    // afford stops short with what you could pay for, rather than failing
    // outright or charging for aircraft that never left the deck.
    internal static class LaunchQueue
    {
        internal const int MaxWing = 4;
        private const float GiveUpSeconds = 900f;

        internal sealed class Entry
        {
            internal Airbase Field;
            internal LoadoutPlan Plan;      // shared by the whole wing
            internal string Callsign;       // this aircraft's: "Viper 1-2"
            internal string Wing;           // the wing's: "Viper 1"; null for a single
            internal float QueuedAt;
        }

        private static readonly List<Entry> queue = new List<Entry>();
        private static float nextTick;

        // Queues one aircraft, or a wing of them, and tries the first at once.
        internal static string Enqueue(Airbase field, LoadoutPlan plan)
        {
            if (field == null || plan?.Definition == null) return "Choose an airframe first.";
            plan = plan.Snapshot();
            int count = Mathf.Clamp(plan.Count, 1, MaxWing);
            string callsign = string.IsNullOrEmpty(plan.Callsign) ? Callsigns.Suggest(plan.Definition) : plan.Callsign;
            string wing = count > 1 ? callsign : null;
            for (int i = 1; i <= count; i++)
                queue.Add(new Entry
                {
                    Field = field,
                    Plan = plan,
                    Callsign = count > 1 ? callsign + "-" + i : callsign,
                    Wing = wing,
                    QueuedAt = Time.unscaledTime
                });
            CarrierOps.Remember(plan);
            nextTick = 0f;
            Tick();
            int waiting = queue.FindAll(e => e.Field == field &&
                (wing != null ? e.Wing == wing : e.Callsign == callsign)).Count;
            string what = count > 1 ? callsign + " · " + count + " × " + plan.Definition.unitName : callsign + " · " + plan.Definition.unitName;
            return waiting == 0 ? "Launching " + what
                : waiting == count ? what + " · queued for a hangar"
                : what + " · first away, " + waiting + " waiting for a hangar";
        }

        internal static List<Entry> For(Airbase field) => queue.FindAll(e => e.Field == field);

        internal static IEnumerable<string> LabelsInUse()
        {
            foreach (Entry entry in queue) yield return entry.Callsign;
        }

        internal static int Cancel(Airbase field)
        {
            int removed = queue.RemoveAll(e => e.Field == field);
            if (removed > 0) Plugin.Log.LogInfo("[deck] " + Airfields.NameOf(field) + " · " + removed + " queued launch(es) cancelled");
            return removed;
        }

        // One launch per field per pass, in the order asked for. A field that
        // cannot host the airframe at all is not waited on.
        internal static void Tick()
        {
            if (queue.Count == 0 || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.5f;
            var tried = new HashSet<Airbase>();
            for (int i = 0; i < queue.Count; i++)
            {
                Entry entry = queue[i];
                if (tried.Contains(entry.Field)) continue;
                tried.Add(entry.Field);
                AircraftDefinition definition = entry.Plan.Definition;

                string drop = null;
                if (entry.Field == null || entry.Field.disabled) drop = "its deck or field is out of action";
                else if (!entry.Field.GetAvailableAircraft().Contains(definition)) drop = "no hangar there can host it any more";
                else if (Time.unscaledTime - entry.QueuedAt > GiveUpSeconds) drop = "no hangar came free";
                if (drop != null) { DropFrom(i, entry, drop); i--; continue; }

                if (!entry.Field.CanSpawnAircraft(definition)) continue;   // wait for a hangar
                if (CarrierOps.Launch(entry.Field, entry.Plan, entry.Callsign, entry.Wing, out string reason))
                {
                    queue.RemoveAt(i);
                    i--;
                    CommandState.Say(reason);
                }
                else { DropFrom(i, entry, reason); i--; }
            }
        }

        // This aircraft, and the rest of its wing still waiting: a wing that
        // cannot be completed goes with what got up.
        private static void DropFrom(int index, Entry entry, string why)
        {
            int removed = 1;
            if (entry.Wing == null) queue.RemoveAt(index);
            else removed = queue.RemoveAll(e => e.Wing == entry.Wing && e.Field == entry.Field);
            string line = (entry.Wing ?? entry.Callsign) + " · " + removed + " launch(es) cancelled · " + why;
            Plugin.Log.LogInfo("[deck] " + line);
            CommandState.Say(line);
        }
    }
}

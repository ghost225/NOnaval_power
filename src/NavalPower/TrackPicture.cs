using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace NavalPower
{
    public enum TrackSource { Unknown, Own, Datalink }

    public sealed class TrackOrigin
    {
        public TrackSource Source;
        public string Reporter;      // friendly unit feeding the datalink, when known
    }

    // Where each contact in the picture is coming from.
    //
    // A faction shares radar through FactionHQ, so a ship under EMCON keeps
    // seeing everything its consorts see. Separating own-sensor contacts from
    // datalink ones is what makes going silent legible: the picture does not
    // vanish, it stops being yours.
    public static class TrackPicture
    {
        private static readonly FieldInfo Detected = AccessTools.Field(typeof(TargetDetector), "detectedTargets");

        private static readonly Dictionary<Unit, TrackOrigin> origins = new Dictionary<Unit, TrackOrigin>();
        private static readonly HashSet<Unit> own = new HashSet<Unit>();
        private static Ship cached;
        private static float nextRefresh;

        public static TrackOrigin Origin(Ship ship, Unit contact)
        {
            Refresh(ship);
            return contact != null && origins.TryGetValue(contact, out TrackOrigin origin)
                ? origin : new TrackOrigin { Source = TrackSource.Unknown };
        }

        public static bool IsOwn(Ship ship, Unit contact)
        {
            Refresh(ship);
            return contact != null && own.Contains(contact);
        }

        public static int OwnCount(Ship ship) { Refresh(ship); return own.Count; }

        private static void Refresh(Ship ship)
        {
            if (ship == null) { origins.Clear(); own.Clear(); return; }
            // Walking every friendly unit's detectors is not cheap; twice a
            // second is far finer than the picture actually changes.
            if (ship == cached && Time.unscaledTime < nextRefresh) return;
            cached = ship;
            nextRefresh = Time.unscaledTime + 0.5f;

            origins.Clear();
            own.Clear();
            if (Detected == null) return;

            foreach (TargetDetector detector in Sensors.Detectors(ship))
                foreach (Unit seen in Contacts(detector))
                    if (seen != null) own.Add(seen);

            foreach (Unit seen in own)
                origins[seen] = new TrackOrigin { Source = TrackSource.Own };

            FactionHQ hq = ship.NetworkHQ;
            if (hq == null) return;

            // Anything the faction holds that we are not seeing ourselves is
            // datalink; attribute it to whichever consort actually sees it.
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.disabled || unit == ship) continue;
                if (unit.NetworkHQ != hq) continue;
                foreach (TargetDetector detector in unit.GetComponentsInChildren<TargetDetector>(true))
                {
                    if (detector == null || detector.GetAttachedUnit() != unit || !detector.activated) continue;
                    foreach (Unit seen in Contacts(detector))
                    {
                        if (seen == null || own.Contains(seen)) continue;
                        if (origins.TryGetValue(seen, out TrackOrigin existing) && existing.Reporter != null) continue;
                        origins[seen] = new TrackOrigin
                        {
                            Source = TrackSource.Datalink,
                            Reporter = unit.definition?.unitName ?? unit.name
                        };
                    }
                }
            }
        }

        private static IEnumerable<Unit> Contacts(TargetDetector detector)
        {
            if (detector == null || Detected == null) yield break;
            if (!(Detected.GetValue(detector) is IEnumerable list)) yield break;
            foreach (object item in list) if (item is Unit unit) yield return unit;
        }

        internal static string Describe(Ship ship, Unit contact)
        {
            TrackOrigin origin = Origin(ship, contact);
            switch (origin.Source)
            {
                case TrackSource.Own: return "Own sensors";
                case TrackSource.Datalink:
                    return origin.Reporter != null ? "Datalink · " + origin.Reporter : "Datalink";
                default: return "Reported track";
            }
        }
    }
}

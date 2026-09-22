using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    public sealed class SensorSnapshot
    {
        public int Id;
        public string Name;
        public bool IsEmitter, Operational, Active, Jammed;
        public float RangeMetres, VisualRangeMetres;
        public int DetectedCount;
    }

    // Sensor status and emission control.
    //
    // Radar derives from TargetDetector, so a hull's detectors are a mix of
    // emitters (radar) and passive sensors. EMCON only silences emitters --
    // switching off a passive sensor would not reduce the ship's signature,
    // it would just blind it.
    public static class Sensors
    {
        private static readonly FieldInfo Detected = AccessTools.Field(typeof(TargetDetector), "detectedTargets");

        internal static IEnumerable<TargetDetector> Detectors(Ship ship)
        {
            if (ship == null) yield break;
            foreach (TargetDetector detector in ship.GetComponentsInChildren<TargetDetector>(true))
                if (detector != null && detector.GetAttachedUnit() == ship) yield return detector;
        }

        public static SensorSnapshot[] GetSensors(Ship ship)
        {
            var rows = new List<SensorSnapshot>();
            if (ship == null || !CommandableShip.Is(ship)) return rows.ToArray();
            foreach (TargetDetector detector in Detectors(ship))
            {
                var radar = detector as Radar;
                bool operational = detector.IsOperational();
                rows.Add(new SensorSnapshot
                {
                    Id = detector.GetInstanceID(),
                    Name = Naming.Pretty(detector.name),
                    IsEmitter = radar != null,
                    Operational = operational,
                    Active = operational && detector.activated,
                    Jammed = radar != null && radar.IsJammed(),
                    RangeMetres = detector.GetRadarRange(),
                    VisualRangeMetres = detector.GetVisualRange(),
                    DetectedCount = Count(detector)
                });
            }
            return rows.ToArray();
        }

        private static int Count(TargetDetector detector)
        {
            if (Detected == null) return -1;
            return Detected.GetValue(detector) is ICollection collection ? collection.Count : -1;
        }

        public static bool SetEmitting(Ship ship, int sensorId, bool emitting, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            foreach (TargetDetector detector in Detectors(ship))
            {
                if (!(detector is Radar) || detector.GetInstanceID() != sensorId) continue;
                if (!detector.IsOperational()) { reason = "That sensor is not operational."; return false; }
                detector.activated = emitting;
                reason = Naming.Pretty(detector.name) + (emitting ? " radiating." : " silent.");
                return true;
            }
            reason = "No matching sensor.";
            return false;
        }

        public static bool SetAllEmitting(Ship ship, bool emitting, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            int changed = 0;
            foreach (TargetDetector detector in Detectors(ship))
            {
                if (!(detector is Radar) || !detector.IsOperational()) continue;
                detector.activated = emitting;
                changed++;
            }
            reason = changed == 0 ? "No operational emitters."
                : emitting ? "Emitters radiating (" + changed + ")."
                : "EMCON silent · all emitters shut down (" + changed + ").";
            return changed > 0;
        }

        // True when the ship carries operational emitters and none are radiating.
        public static bool IsSilent(Ship ship)
        {
            bool any = false;
            foreach (TargetDetector detector in Detectors(ship))
            {
                if (!(detector is Radar) || !detector.IsOperational()) continue;
                any = true;
                if (detector.activated) return false;
            }
            return any;
        }

        // Emitting radars only: what the ship is lighting up, for the map.
        internal static void CollectActiveRanges(Ship ship, List<float> into)
        {
            foreach (TargetDetector detector in Detectors(ship))
            {
                if (!(detector is Radar) || !detector.IsOperational() || !detector.activated) continue;
                float range = detector.GetRadarRange();
                if (range > 1f && !float.IsNaN(range) && !float.IsInfinity(range)) into.Add(range);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace NavalPower
{
    public enum EmitterClass { Surface, Airborne, Land }

    // A passive bearing measurement on a radar that is transmitting. Carries an
    // estimate and its error, never the emitter itself: hearing a radar does
    // not tell you what is carrying it or exactly where it is.
    public sealed class EsmContact
    {
        public uint Id;
        public EmitterClass Class;
        public string Type;
        public GlobalPosition Position;
        public float BearingDegrees, RangeMetres;
        public float RadialUncertaintyMetres, CrossRangeUncertaintyMetres;
        public float AgeSeconds;
        public bool Stale;
    }

    public static class Esm
    {
        private static readonly EsmContact[] Empty = new EsmContact[0];

        internal static void Configure(Ship ship)
        {
            if (ship != null && CommandableShip.Is(ship) && ship.GetComponent<EsmReceiver>() == null)
                ship.gameObject.AddComponent<EsmReceiver>();
        }

        public static EsmContact[] GetContacts(Ship ship)
        {
            if (ship == null || !CommandableShip.Is(ship)) return Empty;
            Configure(ship);
            var receiver = ship.GetComponent<EsmReceiver>();
            return receiver != null ? receiver.Copy() : Empty;
        }
    }

    internal static class EsmPolicy
    {
        internal const float MaximumRange = 200000f;
        internal const float ScanSeconds = 1f, StaleSeconds = 6f, ExpireSeconds = 120f;
        internal const int Budget = 96, Capacity = 32;
        internal const float RadialFraction = .20f, MinimumRadial = 2500f, AngularErrorDegrees = 2.5f;

        // An emitter is heard about twice as far as it can see. That asymmetry
        // is the whole argument for going silent.
        internal static float DetectionRange(float emitterRadarRange) =>
            !Finite(emitterRadarRange) || emitterRadarRange <= 0f ? 0f
                : Mathf.Min(MaximumRange, emitterRadarRange * 2f);

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // A stable per receiver/emitter bias, so repeated measurements of a
        // stationary emitter do not jitter around the truth and average out
        // into a perfect fix.
        internal static float Bias(uint receiverId, uint emitterId, uint channel)
        {
            unchecked
            {
                uint value = receiverId * 747796405u ^ emitterId * 2891336453u ^ channel * 277803737u;
                value ^= value >> 16; value *= 2246822519u; value ^= value >> 13;
                value *= 3266489917u; value ^= value >> 16;
                return ((value & 0x00ffffffu) / 16777215f * 2f - 1f) * .65f;
            }
        }

        internal static GlobalPosition Estimate(GlobalPosition origin, GlobalPosition emitter,
            float radialBias, float angularBias,
            out float radial, out float crossRange, out float bearing, out float range)
        {
            Vector3 offset = emitter - origin;
            offset.y = 0f;
            float distance = offset.magnitude;
            radial = Mathf.Max(MinimumRadial, distance * RadialFraction);
            crossRange = Mathf.Max(250f, distance * Mathf.Tan(AngularErrorDegrees * Mathf.Deg2Rad));
            float angle = Mathf.Atan2(offset.x, offset.z) + angularBias * AngularErrorDegrees * Mathf.Deg2Rad;
            bearing = (angle * Mathf.Rad2Deg + 360f) % 360f;
            range = Mathf.Max(1f, distance + radialBias * radial);
            return origin + new Vector3(Mathf.Sin(angle) * range, 0f, Mathf.Cos(angle) * range);
        }
    }

    internal sealed class EsmReceiver : MonoBehaviour
    {
        private sealed class Contact
        {
            internal Unit Emitter;
            internal Radar Radar;
            internal uint Id;
            internal EmitterClass Class;
            internal string Type;
            internal GlobalPosition Position;
            internal float Radial, CrossRange, Bearing, Range, LastHeard, MovementAllowance;
        }

        private readonly List<Contact> contacts = new List<Contact>(EsmPolicy.Capacity);
        private Ship ship;
        private FactionHQ faction;
        private int cursor;
        private float nextScan;

        private void Awake() { ship = GetComponent<Ship>(); }
        private void OnDestroy() { contacts.Clear(); }

        // The set is passive, so it keeps working with every emitter shut down.
        private Vector3 ReceiverPoint()
        {
            foreach (TargetDetector detector in Sensors.Detectors(ship))
            {
                Transform point = detector.GetScanPoint();
                if (point != null) return point.position;
            }
            return ship.transform.position;
        }

        private void Update()
        {
            if (ship == null || !ship.IsServer || !ship.LocalSim || !MissionManager.IsRunning) return;
            if (faction != ship.NetworkHQ) { contacts.Clear(); faction = ship.NetworkHQ; }
            float now = Time.timeSinceLevelLoad;
            if (now < nextScan) return;
            nextScan = now + EsmPolicy.ScanSeconds;
            if (faction == null) return;

            Vector3 receiver = ReceiverPoint();

            for (int i = contacts.Count - 1; i >= 0; i--)
            {
                Contact contact = contacts[i];
                if (CanHear(contact.Emitter, contact.Radar, receiver)) Refresh(contact, now);
                if (now - contact.LastHeard > EsmPolicy.ExpireSeconds) contacts.RemoveAt(i);
            }

            int count = UnitRegistry.allUnits.Count;
            for (int scanned = 0; scanned < Math.Min(count, EsmPolicy.Budget); scanned++)
            {
                if (cursor >= count) cursor = 0;
                Unit candidate = UnitRegistry.allUnits[cursor++];
                if (candidate == null || candidate == ship || candidate.disabled ||
                    candidate.NetworkHQ == null || candidate.NetworkHQ == faction) continue;

                bool known = false;
                for (int i = 0; i < contacts.Count; i++) if (contacts[i].Emitter == candidate) { known = true; break; }
                if (known) continue;

                var radar = candidate.radar as Radar;
                if (!CanHear(candidate, radar, receiver)) continue;

                if (contacts.Count >= EsmPolicy.Capacity)
                {
                    // Only ever displace a stale estimate; a live emitter keeps
                    // its identity rather than thrashing the list.
                    int oldest = -1;
                    float oldestTime = now - EsmPolicy.StaleSeconds;
                    for (int i = 0; i < contacts.Count; i++)
                        if (contacts[i].LastHeard < oldestTime) { oldest = i; oldestTime = contacts[i].LastHeard; }
                    if (oldest < 0) continue;
                    contacts.RemoveAt(oldest);
                }

                var added = new Contact { Emitter = candidate, Radar = radar, Id = Identity(candidate) };
                // Deliberately coarse: an emitter family, not a hull class.
                if (candidate is Ship) { added.Class = EmitterClass.Surface; added.Type = "Surface radar emitter"; added.MovementAllowance = 30f; }
                else if (candidate is Aircraft) { added.Class = EmitterClass.Airborne; added.Type = "Airborne radar emitter"; added.MovementAllowance = 450f; }
                else { added.Class = EmitterClass.Land; added.Type = "Land radar emitter"; added.MovementAllowance = 20f; }
                Refresh(added, now);
                contacts.Add(added);
            }
        }

        private bool CanHear(Unit emitter, Radar radar, Vector3 receiver)
        {
            if (emitter == null || emitter == ship || emitter.disabled || emitter.NetworkHQ == null ||
                emitter.NetworkHQ == faction || radar == null || !radar.activated || !radar.IsOperational() ||
                radar.GetAttachedUnit() != emitter || emitter.radar != radar) return false;
            Transform point = radar.GetScanPoint();
            if (point == null) return false;
            float distance = (point.position - receiver).magnitude;
            if (!EsmPolicy.Finite(distance) || distance < 1f || distance > EsmPolicy.DetectionRange(radar.GetRadarRange()))
                return false;
            // Terrain still blocks. No invented radar horizon, and no tracking
            // update is sent: an emitter heard passively is not a radar track.
            return !Physics.Linecast(receiver, point.position, PhysicsLayers.StaticsMask);
        }

        private void Refresh(Contact contact, float now)
        {
            contact.Position = EsmPolicy.Estimate(ship.GlobalPosition(), contact.Emitter.GlobalPosition(),
                EsmPolicy.Bias(ship.persistentID.Id, contact.Emitter.persistentID.Id, 1),
                EsmPolicy.Bias(ship.persistentID.Id, contact.Emitter.persistentID.Id, 2),
                out contact.Radial, out contact.CrossRange, out contact.Bearing, out contact.Range);
            contact.LastHeard = now;
        }

        private static uint Identity(Unit unit)
        {
            unchecked
            {
                uint value = unit.persistentID.Id * 2654435761u;
                value ^= value >> 15;
                return value % 8999u + 1000u;      // a readable four-digit track number
            }
        }

        internal EsmContact[] Copy()
        {
            if (ship == null || faction != ship.NetworkHQ) return new EsmContact[0];
            float now = Time.timeSinceLevelLoad;
            var result = new List<EsmContact>(contacts.Count);
            foreach (Contact contact in contacts)
            {
                float age = Mathf.Max(0f, now - contact.LastHeard);
                if (age > EsmPolicy.ExpireSeconds) continue;
                // Keep measuring underneath a live track, but show only one
                // symbol: an ESM estimate must not sit on top of a real track.
                if (contact.Emitter != null &&
                    faction.GetTrackingData(contact.Emitter.persistentID)?.Observed() == true) continue;
                // Uncertainty grows with how far the emitter could have moved.
                float growth = Mathf.Max(0f, age - EsmPolicy.StaleSeconds) * contact.MovementAllowance;
                result.Add(new EsmContact
                {
                    Id = contact.Id,
                    Class = contact.Class,
                    Type = contact.Type,
                    Position = contact.Position,
                    BearingDegrees = contact.Bearing,
                    RangeMetres = contact.Range,
                    AgeSeconds = age,
                    Stale = age > EsmPolicy.StaleSeconds,
                    RadialUncertaintyMetres = contact.Radial + growth,
                    CrossRangeUncertaintyMetres = contact.CrossRange + growth
                });
            }
            return result.ToArray();
        }
    }
}

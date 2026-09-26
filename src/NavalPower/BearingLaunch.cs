using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    // Missiles fired down a bearing, to find their own target.
    //
    // Anti-radiation missiles self-acquire: launched without a target they fly
    // on, and every few seconds home on any radar that has been painting them
    // inside their seeker cone. That is exactly the shot you want down an ESM
    // bearing, where there is no fix to target -- only a direction. The catch
    // is which way "on" is: the seeker starts along the launcher's axis, so a
    // round from a vertical cell would search straight up. So each round is
    // fired from its launcher with no target, and as its seeker starts its
    // search heading is set down the ordered bearing.
    //
    // Other seekers need a target (active radar flies blind without one,
    // cruise missiles detonate), so only anti-radiation weapons are offered.
    internal static class BearingLaunch
    {
        private sealed class Shot
        {
            internal Unit Owner;
            internal WeaponStation Station;
            internal float Bearing;
            internal int Remaining;
            internal float NextAt;
        }

        private static readonly List<Shot> queue = new List<Shot>();
        private static readonly List<(Unit owner, float bearing, float until)> seeds = new List<(Unit, float, float)>();
        private static readonly Dictionary<WeaponInfo, bool> selfAcquiring = new Dictionary<WeaponInfo, bool>();

        internal static bool SelfAcquiring(WeaponInfo info)
        {
            if (info == null) return false;
            if (selfAcquiring.TryGetValue(info, out bool known)) return known;
            bool arm = info.weaponPrefab != null && info.weaponPrefab.GetComponentInChildren<ARMSeeker>(true) != null;
            selfAcquiring[info] = arm;
            return arm;
        }

        internal static List<WeaponStation> StationsOn(Unit unit)
        {
            var result = new List<WeaponStation>();
            if (unit?.weaponStations == null) return result;
            foreach (WeaponStation station in unit.weaponStations)
                if (station?.WeaponInfo != null && station.Ammo > 0 && SelfAcquiring(station.WeaponInfo)) result.Add(station);
            return result;
        }

        internal static float BearingTo(Unit from, GlobalPosition to)
        {
            Vector3 offset = to - from.GlobalPosition();
            return (Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg + 360f) % 360f;
        }

        internal static bool Order(Unit owner, WeaponStation station, float bearing, int count, out string reason)
        {
            reason = null;
            if (owner == null || owner.disabled || station == null) { reason = "Nothing to fire."; return false; }
            if (!SelfAcquiring(station.WeaponInfo)) { reason = station.WeaponInfo?.weaponName + " cannot find its own target."; return false; }
            if (station.Ammo <= 0) { reason = station.WeaponInfo.weaponName + " is empty."; return false; }
            count = Mathf.Clamp(count, 1, station.Ammo);
            queue.Add(new Shot { Owner = owner, Station = station, Bearing = bearing, Remaining = count, NextAt = 0f });
            reason = ShipNames.Of(owner) + " · " + count + " × " + station.WeaponInfo.weaponName + " down " + bearing.ToString("000") + "°";
            Plugin.Log.LogInfo("[bearing] " + reason);
            Tick();
            return true;
        }

        // One round per launcher at a time, as its fire interval allows.
        internal static void Tick()
        {
            float now = Time.timeSinceLevelLoad;
            seeds.RemoveAll(s => s.owner == null || now > s.until);
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                Shot shot = queue[i];
                if (shot.Owner == null || shot.Owner.disabled || shot.Remaining <= 0 || shot.Station.Ammo <= 0) { queue.RemoveAt(i); continue; }
                if (now < shot.NextAt) continue;
                Weapon weapon = null;
                foreach (Weapon candidate in shot.Station.Weapons)
                    if (candidate != null && candidate.ammo > 0) { weapon = candidate; break; }
                if (weapon == null) { queue.RemoveAt(i); continue; }

                float radians = shot.Bearing * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                int before = weapon.ammo;
                seeds.Add((shot.Owner, shot.Bearing, now + 5f));
                Vector3 inherited = shot.Owner.rb != null ? shot.Owner.rb.velocity : Vector3.zero;
                weapon.Fire(shot.Owner, null, inherited, shot.Station, shot.Owner.GlobalPosition() + direction * 100000f);
                shot.NextAt = now + 1.2f;
                if (weapon.ammo < before) shot.Remaining--;
                else seeds.RemoveAt(seeds.Count - 1);       // not ready to fire yet; try again shortly
            }
        }

        // The seeker starting up asks whether its missile was one of ours.
        internal static bool TakeSeed(Unit owner, out float bearing)
        {
            bearing = 0f;
            for (int i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].owner != owner) continue;
                bearing = seeds[i].bearing;
                seeds.RemoveAt(i);
                return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ARMSeeker), nameof(ARMSeeker.Initialize))]
    internal static class BearingSeedPatch
    {
        private const string Name = "Bearing launch";
        private static readonly AccessTools.FieldRef<MissileSeeker, Missile> MissileOf =
            AccessTools.FieldRefAccess<MissileSeeker, Missile>("missile");
        private static readonly AccessTools.FieldRef<ARMSeeker, GlobalPosition> KnownOf =
            AccessTools.FieldRefAccess<ARMSeeker, GlobalPosition>("knownPos");

        private static void Postfix(ARMSeeker __instance, Unit target)
        {
            if (target != null || !Guard.Ok(Name)) return;
            try
            {
                Missile missile = MissileOf(__instance);
                if (missile == null || !BearingLaunch.TakeSeed(missile.owner, out float bearing)) return;
                float radians = bearing * Mathf.Deg2Rad;
                GlobalPosition search = missile.GlobalPosition() + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * 100000f;
                KnownOf(__instance) = search;
                missile.SetAimpoint(search, Vector3.zero);
            }
            catch (Exception ex) { Guard.Failed(Name, ex); }
        }
    }
}

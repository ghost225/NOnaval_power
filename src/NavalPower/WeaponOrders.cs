using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NavalPower
{
    public sealed class WeaponCommandInfo
    {
        public string Key, Name, Readiness;
        public int Ammo;
        public float MinRange, MaxRange;
        public bool IsGun, IsBeam, Continuous;
    }

    public static class WeaponOrders
    {
        // Weapon stations are grouped by their WeaponInfo asset, so "the 30mm
        // cannons" is one orderable entry regardless of how many mounts carry it.
        internal static string KeyOf(WeaponInfo info) => info == null ? null : info.name;

        internal static bool Supported(Weapon weapon) =>
            weapon is MissileLauncher || weapon is Gun || weapon is Laser;

        internal static bool Continuous(WeaponStation station) =>
            station.Weapons.Any(w => w is Gun || w is Laser);

        public static WeaponCommandInfo[] GetWeapons(Ship ship)
        {
            if (ship == null || !CommandableShip.Is(ship)) return new WeaponCommandInfo[0];
            return ship.weaponStations
                .Where(s => s.WeaponInfo != null && s.Weapons.Any(Supported))
                .GroupBy(s => s.WeaponInfo)
                .Select(g => new WeaponCommandInfo
                {
                    Key = KeyOf(g.Key),
                    Name = g.Key.weaponName,
                    Ammo = g.Sum(s => Math.Max(0, s.Ammo)),
                    IsGun = g.Any(s => s.Weapons.Any(w => w is Gun)),
                    IsBeam = g.Any(s => s.Weapons.Any(w => w is Laser)),
                    Continuous = g.Any(Continuous),
                    MinRange = g.Key.targetRequirements.minRange,
                    MaxRange = g.Key.targetRequirements.maxRange,
                    Readiness = g.All(s => s.Reloading) ? "Reloading"
                        : g.All(s => s.Ammo <= 0) && !g.Any(s => s.Weapons.Any(w => w is Laser)) ? "Empty"
                        : "Ready"
                })
                .ToArray();
        }

        internal static WeaponStation[] StationsFor(Ship ship, string key) =>
            ship.weaponStations.Where(s => KeyOf(s.WeaponInfo) == key && s.Weapons.Any(Supported)).ToArray();

        // The game scores every weapon against every target type natively:
        // RoleIdentity (antiSurface/antiAir/antiMissile/antiRadar) crossed with
        // the target's TypeIdentity. Zero means the weapon simply cannot do the
        // job -- a radar SAM against a truck -- and that holds for modded
        // weapons and modded hulls without a lookup table of our own.
        public static float Opportunity(WeaponInfo info, Unit target) =>
            info == null || target == null || target.definition == null
                ? 0f : target.definition.GetOpportunity(info.effectiveness);

        public static bool CanAttack(Ship ship, string key, Unit target, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            if (target == null || target == ship || target.disabled || target.definition == null || target.persistentID.NotValid)
            { reason = "Choose another live unit."; return false; }
            WeaponStation[] fitted = StationsFor(ship, key);
            if (fitted.Length == 0) { reason = "That weapon is not fitted."; return false; }
            if (ship.NetworkHQ == null ||
                (target.NetworkHQ != ship.NetworkHQ && !ship.NetworkHQ.TryGetKnownPosition(target, out _)))
            { reason = "No known position for that contact."; return false; }

            WeaponInfo info = fitted[0].WeaponInfo;
            if (Opportunity(info, target) <= 0.01f)
            { reason = info.weaponName + " cannot engage that target type."; return false; }

            // Range is advisory for an explicit order: the player may well be
            // ordering a shot they intend to close the distance for.
            float range = FastMath.Distance(ship.GlobalPosition(), target.GlobalPosition());
            TargetRequirements envelope = info.targetRequirements;
            reason = range > envelope.maxRange
                ? "Beyond " + info.weaponName + " range (" + (range / 1000f).ToString("0.0") + " km of " + (envelope.maxRange / 1000f).ToString("0.0") + " km)."
                : range < envelope.minRange
                ? "Inside " + info.weaponName + " minimum range."
                : null;
            return true;
        }

        public static bool ValidQuantity(int count) =>
            count == 1 || count == 2 || count == 4 || count == 8 || count == 16;

        public static bool Attack(Ship ship, string key, Unit target, int count, out string reason, bool append = false)
        {
            if (!CanAttack(ship, key, target, out reason)) return false;
            string advisory = reason;
            WeaponStation[] stations = StationsFor(ship, key);
            bool continuous = stations.Any(Continuous);
            if (continuous) count = 1;                          // Counts are a missile-only concept.
            else if (!ValidQuantity(count)) { reason = "Choose 1, 2, 4, 8 or 16."; return false; }
            if (!continuous && stations.Sum(s => Math.Max(0, s.Ammo)) < count)
            { reason = "Not enough loaded ammunition."; return false; }
            bool accepted = ShipWeapons.Ensure(ship).AddOrder(key, target, stations, count, continuous, append, out reason);
            if (accepted && advisory != null) reason += " · " + advisory;
            return accepted;
        }

        public static bool CeaseFire(Ship ship, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            ShipWeapons.Ensure(ship).CeaseAll();
            reason = "Cease fire. Airborne weapons continue their own flight.";
            return true;
        }

        public static string GetStatus(Ship ship)
        {
            var state = ship != null ? ship.GetComponent<ShipWeapons>() : null;
            return state != null ? state.Status : "Automatic engagement";
        }
    }

    internal sealed class ShipWeapons : MonoBehaviour
    {
        internal sealed class Order
        {
            internal string Key;
            internal Unit Target;
            internal WeaponStation[] Stations;
            internal WeaponStation Station;
            internal Weapon Selected;
            internal Turret Turret;
            internal int Remaining, Requested;
            internal bool Continuous;
            internal float Created, NextShot, NextMountCheck;
        }

        // Set while an explicit order pulls the trigger, so the engagement
        // policy can tell our own shots from the ship's automatic ones.
        [System.ThreadStatic] internal static bool Firing;

        private readonly List<Order> orders = new List<Order>(8);
        private Ship ship;
        private string lastStatus = "Automatic engagement";

        internal string Status => orders.Count == 0 ? lastStatus
            : (orders[0].Continuous ? "Continuous" : orders[0].Remaining + "/" + orders[0].Requested + " remaining")
              + " · " + orders.Count + " order(s) · " + lastStatus;

        // True when an explicit order currently owns this mount, so the
        // engagement policy leaves it alone.
        internal static bool Holds(Ship ship, Turret turret)
        {
            var state = ship != null ? ship.GetComponent<ShipWeapons>() : null;
            if (state == null || turret == null) return false;
            foreach (Order order in state.orders) if (order.Turret == turret) return true;
            return false;
        }

        internal static void CollectTargets(Ship ship, List<Unit> into)
        {
            var state = ship != null ? ship.GetComponent<ShipWeapons>() : null;
            if (state == null) return;
            foreach (Order order in state.orders) if (order.Target != null) into.Add(order.Target);
        }

        internal static ShipWeapons Ensure(Ship ship)
        {
            var state = ship.GetComponent<ShipWeapons>();
            if (state == null)
            {
                state = ship.gameObject.AddComponent<ShipWeapons>();
                state.ship = ship;
            }
            return state;
        }

        internal bool AddOrder(string key, Unit target, WeaponStation[] stations, int count,
            bool continuous, bool append, out string reason)
        {
            if (orders.Count >= 16) { reason = "The order queue is full."; return false; }
            // Replacing cancels unlaunched work for this weapon only; anything
            // already in the air keeps its own seeker and objective.
            if (!append)
                for (int i = orders.Count - 1; i >= 0; i--)
                    if (orders[i].Key == key) { Release(orders[i]); orders.RemoveAt(i); }

            orders.Add(new Order
            {
                Key = key, Target = target, Stations = stations,
                Remaining = count, Requested = count, Continuous = continuous,
                Created = Time.timeSinceLevelLoad
            });
            reason = lastStatus = (append ? "Queued " : "Ordered ")
                + (continuous ? "continuous " : count + " × ") + stations[0].WeaponInfo.weaponName
                + " against " + target.definition.unitName;
            Plugin.Log.LogInfo("[order] " + ship.definition?.unitName + ": " + reason);
            return true;
        }

        internal void CeaseAll()
        {
            foreach (Order order in orders) Release(order);
            orders.Clear();
            lastStatus = "Cease fire";
        }

        private void FixedUpdate()
        {
            if (ship == null || ship.disabled || !ship.IsServer || !ship.LocalSim) { CeaseAll(); return; }
            for (int i = 0; i < orders.Count; i++)
            {
                Order order = orders[i];
                bool expired = order.Remaining <= 0
                    || order.Target == null || order.Target.disabled
                    || (!order.Continuous && Time.timeSinceLevelLoad - order.Created > 180f);
                if (expired)
                {
                    Release(order);
                    lastStatus = order.Remaining <= 0 ? "Ordered salvo complete" : "Order ended";
                    orders.RemoveAt(i--);
                    continue;
                }
                // One live order per weapon group; the rest of the queue waits.
                if (orders.Take(i).Any(o => o.Key == order.Key)) continue;
                Execute(order);
            }
        }

        private void Execute(Order order)
        {
            float now = Time.timeSinceLevelLoad;
            if (now < order.NextShot) return;

            // Re-assess the mount periodically: a turret that could bear when the
            // order was issued may not after the ship or the target has turned.
            if (order.Selected != null && now >= order.NextMountCheck)
            {
                order.NextMountCheck = now + .2f;
                if (!NativeBindings.CanServe(order.Turret, ship, order.Target)) Release(order);
            }

            if (order.Selected == null || !Usable(order.Selected))
            {
                if (!AcquireMount(order, now)) { lastStatus = "Waiting for a mount that can bear"; order.NextShot = now + .1f; }
                return;
            }

            // A trainable mount must be pointing at the target first. A fixed
            // launcher never assigns onTarget at all -- Turret.AimTurret returns
            // a range test for those -- so gating on it would block forever.
            if (order.Turret != null && !NativeBindings.FiresWithoutAiming(order.Turret) && !order.Turret.IsOnTarget())
            { NativeBindings.StopTrigger(order.Selected); lastStatus = "Mount turning onto target"; return; }
            if (order.Selected.Safety)
            { NativeBindings.StopTrigger(order.Selected); lastStatus = "Waiting for mount readiness"; return; }
            if (order.Selected is MissileLauncher launcher &&
                now - NativeBindings.LastFired(launcher) < NativeBindings.FireInterval(launcher)) return;

            int before = order.Selected.ammo;
            bool previous = Firing;
            Firing = true;
            try
            {
                order.Selected.Fire(ship, order.Target, ship.rb != null ? ship.rb.velocity : Vector3.zero,
                    order.Station, default(GlobalPosition));
            }
            finally { Firing = previous; }

            if (order.Continuous)
            {
                lastStatus = "Engaging " + order.Target.definition.unitName + " continuously";
                return;
            }
            if (order.Selected.ammo < before)
            {
                order.Remaining--;
                lastStatus = order.Remaining + " of " + order.Requested + " away";
            }
            order.NextShot = now + Time.fixedDeltaTime;
        }

        private bool AcquireMount(Order order, float now)
        {
            Release(order);
            foreach (WeaponStation station in order.Stations)
            {
                if (station.Reloading) continue;
                Weapon candidate = station.Weapons.FirstOrDefault(w =>
                    WeaponOrders.Supported(w) && Usable(w) && (w.ammo > 0 || w is Laser) &&
                    NativeBindings.CanServe(w.GetComponentInParent<Turret>(), ship, order.Target));
                if (candidate == null) continue;

                order.Selected = candidate;
                order.Station = station;
                order.Turret = candidate.GetComponentInParent<Turret>();
                if (order.Turret != null)
                {
                    // Clear whatever the native controller had picked, then take
                    // the mount manually. This is the contested step: if ShipAI
                    // keeps re-choosing, the turret will visibly oscillate.
                    if (NativeBindings.TurretChooseTarget != null && order.Turret.GetTarget() != null)
                        NativeBindings.TurretChooseTarget.Invoke(order.Turret, new object[] { true });
                    order.Turret.SetTarget(order.Target.persistentID, station.Number);
                    order.Turret.SetManual(true);
                }
                candidate.SetTarget(order.Target);
                order.NextShot = now + Time.fixedDeltaTime;
                order.NextMountCheck = now + .2f;
                Plugin.Log.LogInfo("[mount] " + order.Key + " -> station " + station.Number +
                    " turret=" + (order.Turret != null ? order.Turret.name : "none") +
                    " fixed=" + NativeBindings.FiresWithoutAiming(order.Turret));
                return true;
            }
            return false;
        }

        private void Release(Order order)
        {
            if (order.Selected != null) NativeBindings.StopTrigger(order.Selected);
            // Hand the mount back to the engagement policy, not to its own
            // devices. Releasing it outright unlocked the turret until the
            // policy swept again a quarter-second later, which was long enough
            // for a mount to ripple off shots the rules forbade -- firing one
            // manual round under weapons hold effectively freed the weapon.
            if (order.Turret != null) EngagementPolicy.HandBack(ship, order.Turret);
            order.Selected = null;
            order.Station = null;
            order.Turret = null;
        }

        private static bool Usable(Weapon weapon) =>
            weapon != null && weapon.gameObject.activeInHierarchy;
    }
}

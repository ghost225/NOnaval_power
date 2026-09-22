using System.Linq;
using System.Text;
using UnityEngine;

namespace NavalPower
{
    // Step 3 rig. Deliberately keybind-driven rather than a UI: the open
    // question is whether a manual order takes a mount cleanly away from
    // ShipAI, and that needs orders and logs, not menus.
    //
    //   F6  report the followed ship's state
    //   F7  order the first ready weapon at the nearest hostile
    //   F8  cease fire
    //   F9  waypoint 5 km off the bow
    //   Home / End  all stop / ahead flank
    internal sealed class TestHarness : MonoBehaviour
    {
        private void Update()
        {
            if (!MissionManager.IsRunning) return;
            if (Input.GetKeyDown(KeyCode.F6)) Report();
            else if (Input.GetKeyDown(KeyCode.F7)) OrderNearest();
            else if (Input.GetKeyDown(KeyCode.F8)) Cease();
            else if (Input.GetKeyDown(KeyCode.F9)) WaypointAhead();
            else if (Input.GetKeyDown(KeyCode.Home)) Speed(0f);
            else if (Input.GetKeyDown(KeyCode.End)) Speed(CommandableShip.MaximumSpeedKnots(Plugin.CommandedShip()));
        }

        private static Ship Require()
        {
            Ship ship = Plugin.CommandedShip();
            if (ship == null) { Plugin.Log.LogWarning("[naval] Follow a commandable ship first."); return null; }
            if (!CommandableShip.CanCommand(ship, out string reason))
            { Plugin.Log.LogWarning("[naval] " + reason); return null; }
            return ship;
        }

        private static void Report()
        {
            Ship ship = Plugin.CommandedShip();
            if (ship == null) { Plugin.Log.LogInfo("[naval] no commandable ship followed"); return; }
            var text = new StringBuilder("[naval] " + ship.definition?.unitName + " [" + ship.definition?.jsonKey + "]");
            text.Append("\n  ai=").Append(ship.GetComponent<ShipAI>()?.GetType().Name ?? "none");
            text.Append("  commandable=").Append(CommandableShip.CanCommand(ship, out string why) ? "yes" : "no · " + why);

            NavigationSnapshot nav = NavigationOrders.GetSnapshot(ship);
            if (nav != null)
                text.Append("\n  speed actual=").Append(nav.ActualSpeedKnots.ToString("0.0"))
                    .Append(" kt ordered=").Append(nav.OrderedSpeedKnots.ToString("0.0"))
                    .Append(" kt (max ").Append(nav.MaximumSpeedKnots.ToString("0.0"))
                    .Append(")  route=").Append(nav.Waypoints.Length)
                    .Append("  ").Append(nav.Status);

            text.Append("\n  throttle=").Append(ship.GetInputs()?.throttle.ToString("0.00") ?? "?");
            text.Append("\n  weapons: ").Append(WeaponOrders.GetStatus(ship));
            foreach (WeaponCommandInfo weapon in WeaponOrders.GetWeapons(ship))
            {
                WeaponInfo info = WeaponOrders.StationsFor(ship, weapon.Key).FirstOrDefault()?.WeaponInfo;
                text.Append("\n    ").Append(weapon.Name).Append(" · ").Append(weapon.Readiness)
                    .Append(" · ammo ").Append(weapon.Ammo)
                    .Append(weapon.Continuous ? " · continuous" : "")
                    .Append(" · range ").Append(weapon.MinRange.ToString("0")).Append("-").Append(weapon.MaxRange.ToString("0"));
                if (info != null)
                {
                    RoleIdentity role = info.effectiveness;
                    text.Append(" · vs surface/air/missile/radar ")
                        .Append(role.antiSurface.ToString("0.0")).Append("/")
                        .Append(role.antiAir.ToString("0.0")).Append("/")
                        .Append(role.antiMissile.ToString("0.0")).Append("/")
                        .Append(role.antiRadar.ToString("0.0"));
                }
            }

            foreach (Turret turret in ship.GetComponentsInChildren<Turret>(true))
                text.Append("\n    turret ").Append(turret.name)
                    .Append(" onTarget=").Append(turret.IsOnTarget())
                    .Append(" target=").Append(turret.GetTarget()?.definition?.unitName ?? "none");

            Plugin.Log.LogInfo(text.ToString());
        }

        private static void OrderNearest()
        {
            Ship ship = Require();
            if (ship == null) return;

            // Pick the best weapon/target pairing rather than the first of each:
            // score every ready weapon against every hostile by native
            // opportunity, and require the contact to be inside the envelope.
            WeaponCommandInfo[] ready = WeaponOrders.GetWeapons(ship).Where(w => w.Readiness == "Ready").ToArray();
            if (ready.Length == 0) { Plugin.Log.LogWarning("[naval] no ready weapon"); return; }

            WeaponCommandInfo bestWeapon = null;
            Unit bestTarget = null;
            float bestScore = 0f, bestRange = 0f;

            foreach (WeaponCommandInfo weapon in ready)
            {
                WeaponInfo info = WeaponOrders.StationsFor(ship, weapon.Key).FirstOrDefault()?.WeaponInfo;
                if (info == null) continue;
                foreach (Unit unit in UnitRegistry.allUnits)
                {
                    if (unit == null || unit == ship || unit.disabled) continue;
                    if (unit.NetworkHQ == null || unit.NetworkHQ == ship.NetworkHQ) continue;
                    float opportunity = WeaponOrders.Opportunity(info, unit);
                    if (opportunity <= 0.01f) continue;
                    float range = FastMath.Distance(ship.GlobalPosition(), unit.GlobalPosition());
                    if (range > weapon.MaxRange || range < weapon.MinRange) continue;
                    // Prefer capability, break ties by closing range.
                    float score = opportunity * 1000f - range / 1000f;
                    if (score > bestScore)
                    { bestScore = score; bestWeapon = weapon; bestTarget = unit; bestRange = range; }
                }
            }

            if (bestWeapon == null)
            {
                Plugin.Log.LogWarning("[naval] no hostile inside any ready weapon's envelope; nearest options:");
                foreach (WeaponCommandInfo weapon in ready)
                    Plugin.Log.LogWarning("    " + weapon.Name + " envelope " +
                        weapon.MinRange.ToString("0") + "-" + weapon.MaxRange.ToString("0") + " m");
                return;
            }

            Plugin.Log.LogInfo("[naval] ordering " + bestWeapon.Name + " at " + bestTarget.definition?.unitName +
                " (" + (bestRange / 1000f).ToString("0.0") + " km, opportunity " +
                WeaponOrders.Opportunity(WeaponOrders.StationsFor(ship, bestWeapon.Key)[0].WeaponInfo, bestTarget).ToString("0.00") + ")");
            WeaponOrders.Attack(ship, bestWeapon.Key, bestTarget, 1, out string reason);
            Plugin.Log.LogInfo("[naval] " + reason);
        }

        private static void Cease()
        {
            Ship ship = Require();
            if (ship == null) return;
            WeaponOrders.CeaseFire(ship, out string reason);
            Plugin.Log.LogInfo("[naval] " + reason);
        }

        private static void WaypointAhead()
        {
            Ship ship = Require();
            if (ship == null) return;
            Vector3 bow = ship.transform.forward; bow.y = 0f; bow.Normalize();
            NavigationOrders.ReplaceWaypoint(ship, ship.GlobalPosition() + bow * 5000f, out string reason);
            Plugin.Log.LogInfo("[naval] " + reason);
        }

        private static void Speed(float knots)
        {
            Ship ship = Require();
            if (ship == null) return;
            NavigationOrders.SetOrderedSpeedKnots(ship, knots, out string reason);
            Plugin.Log.LogInfo("[naval] " + reason);
        }
    }
}

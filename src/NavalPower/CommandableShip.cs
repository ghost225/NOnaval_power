using NuclearOption.Networking;
using UnityEngine;

namespace NavalPower
{
    // The capability gate. Naval Power commands any ship the native game drives
    // with a ShipAI and a UnitCommand; there is no hull whitelist. ShipAI
    // subclasses (AssaultCarrierAI, LandingCraftAI, third-party ship AI) match
    // here polymorphically, which is why orders are issued through UnitCommand
    // rather than through a patch on a specific Steer body.
    internal static class CommandableShip
    {
        internal static bool Is(Unit unit) =>
            unit is Ship ship && ship.GetComponent<ShipAI>() != null && ship.UnitCommand != null;

        internal static bool CanCommand(Unit unit, out string reason)
        {
            reason = null;
            var ship = unit as Ship;
            if (ship == null) { reason = "Only ships can be commanded."; return false; }
            if (ship.GetComponent<ShipAI>() == null || ship.UnitCommand == null)
            { reason = "This hull has no native navigation controller."; return false; }
            if (!MissionManager.IsRunning || ship.disabled || !ship.gameObject.activeInHierarchy)
            { reason = "This ship is not available in a running mission."; return false; }
            if (!GameManager.GetLocalPlayer<Player>(out var player) || player == null)
            { reason = "A local player is required."; return false; }
            if (!HasPermission(ship, player))
            { reason = player.HQ != null ? "You can command only your own faction's ships." : "Command authority is required."; return false; }
            // Orders mutate simulation state, so they belong to whoever owns it.
            if (!ship.IsServer || !ship.LocalSim)
            { reason = "Ship commands require the mission host."; return false; }
            return true;
        }

        internal static bool HasPermission(Ship ship, Player player)
        {
            if (ship == null || player == null) return false;
            return player.HQ != null ? player.HQ == ship.NetworkHQ : player.HasAuthority;
        }

        // Ship definitions record top speed in km/h; every order in this mod is
        // in knots, matching the native naval UI.
        internal const float MetresPerSecondPerKnot = 1852f / 3600f;

        internal static float MaximumSpeedKnots(Ship ship)
        {
            float top = (ship != null ? ship.definition as ShipDefinition : null)?.shipInfo?.topSpeed ?? 0f;
            return Finite(top) && top > 0f ? top / 1.852f : 30f;
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(GlobalPosition p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
    }
}

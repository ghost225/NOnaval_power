using NuclearOption.Networking;
using System.Collections.Generic;
using UnityEngine;

namespace NavalPower
{
    public sealed class NavigationSnapshot
    {
        public float ActualSpeedKnots, OrderedSpeedKnots, MinimumSpeedKnots, MaximumSpeedKnots;
        public bool HasSpeedOrder;
        public GlobalPosition[] Waypoints;
        public string Status;
    }

    // Route and speed orders. Every leg is handed to native pathfinding through
    // UnitCommand.SetDestination, so this works unchanged on any ShipAI subclass
    // -- including ones that override Steer, where a Harmony patch on the base
    // method would never run.
    public static class NavigationOrders
    {
        public const int MaximumWaypoints = 64;

        private static ShipRoute State(Ship ship, out string reason)
        {
            if (!CommandableShip.CanCommand(ship, out reason)) return null;
            var state = ship.GetComponent<ShipRoute>();
            if (state == null)
            {
                state = ship.gameObject.AddComponent<ShipRoute>();
                state.Initialize(ship);
            }
            GameManager.GetLocalPlayer<Player>(out var player);
            state.Issuer = player;
            return state;
        }

        public static bool ReplaceWaypoint(Ship ship, GlobalPosition waypoint, out string reason)
        {
            if (!CommandableShip.Finite(waypoint)) { reason = "That waypoint is invalid."; return false; }
            var state = State(ship, out reason);
            if (state == null) return false;
            state.Replace(waypoint);
            reason = "Course set.";
            return true;
        }

        public static bool AppendWaypoint(Ship ship, GlobalPosition waypoint, out string reason)
        {
            if (!CommandableShip.Finite(waypoint)) { reason = "That waypoint is invalid."; return false; }
            var state = State(ship, out reason);
            if (state == null) return false;
            if (state.WaypointCount >= MaximumWaypoints) { reason = "The route is full."; return false; }
            state.Append(waypoint);
            reason = "Waypoint appended.";
            return true;
        }

        public static bool ClearWaypoints(Ship ship, out string reason)
        {
            var state = State(ship, out reason);
            if (state == null) return false;
            state.ClearRoute();
            reason = "Route cleared.";
            return true;
        }

        public static bool SetOrderedSpeedKnots(Ship ship, float knots, out string reason)
        {
            if (!CommandableShip.Finite(knots)) { reason = "That speed is invalid."; return false; }
            var state = State(ship, out reason);
            if (state == null) return false;
            float maximum = CommandableShip.MaximumSpeedKnots(ship);
            state.OrderSpeed(Mathf.Clamp(knots, -maximum / 3f, maximum));
            reason = "Ordered " + state.OrderedSpeedKnots.ToString("0.0") + " kt.";
            return true;
        }

        public static bool ReleaseSpeed(Ship ship, out string reason)
        {
            var state = State(ship, out reason);
            if (state == null) return false;
            state.ReleaseSpeed();
            reason = "Speed released to the native controller.";
            return true;
        }

        public static NavigationSnapshot GetSnapshot(Ship ship)
        {
            if (ship == null || !CommandableShip.Is(ship)) return null;
            var state = ship.GetComponent<ShipRoute>();
            float actual = ship.rb != null
                ? Vector3.Dot(ship.rb.velocity, ship.transform.forward) / CommandableShip.MetresPerSecondPerKnot
                : 0f;
            float maximum = CommandableShip.MaximumSpeedKnots(ship);
            return new NavigationSnapshot
            {
                ActualSpeedKnots = actual,
                OrderedSpeedKnots = state != null && state.HasSpeedOrder ? state.OrderedSpeedKnots : actual,
                MinimumSpeedKnots = -maximum / 3f,
                MaximumSpeedKnots = maximum,
                HasSpeedOrder = state != null && state.HasSpeedOrder,
                Waypoints = state != null ? state.CopyWaypoints() : new GlobalPosition[0],
                Status = state != null ? state.Status : "Autonomous navigation"
            };
        }
    }

    internal sealed class ShipRoute : MonoBehaviour
    {
        private readonly List<GlobalPosition> route = new List<GlobalPosition>();
        private Ship ship;
        private ShipAI ai;
        private bool ownsRoute, ownsHeading, sendingOwnOrder, hasSentLeg;
        private GlobalPosition lastSent;
        private Vector3 orderedHeading;
        private GlobalPosition headingDestination;
        private float speedIntegral, lastGovernorUpdate, nextAuthorityCheck;

        internal Player Issuer;
        internal bool HasSpeedOrder { get; private set; }
        internal float OrderedSpeedKnots { get; private set; }
        internal int WaypointCount => route.Count;
        internal bool OwnsNavigation => ownsRoute || ownsHeading;

        internal string Status =>
            ownsRoute && route.Count == 0 ? "Holding position" :
            HasSpeedOrder && Mathf.Abs(OrderedSpeedKnots) < .01f ? "All stop" :
            route.Count > 0 ? "Following ordered route (" + route.Count + " leg(s))" :
            ownsHeading ? "Following ordered course" : "Autonomous navigation";

        internal void Initialize(Ship owner)
        {
            ship = owner;
            ai = ship.GetComponent<ShipAI>();
            if (ship.UnitCommand != null) ship.UnitCommand.ProcessSetDestination += OnNativeDestination;
        }

        private void OnDestroy()
        {
            if (ship != null && ship.UnitCommand != null) ship.UnitCommand.ProcessSetDestination -= OnNativeDestination;
        }

        internal GlobalPosition[] CopyWaypoints() => route.ToArray();

        // A destination we did not send is a newer player order and supersedes
        // the queued route. Two things are not that, and must not clear it:
        // our own leg re-entering synchronously, and anything that re-issues
        // the ship's current destination -- opening and closing the map does
        // exactly that, and treating it as a new order silently ate the route.
        private void OnNativeDestination(ref UnitCommand.Command command)
        {
            if (sendingOwnOrder) return;
            if (hasSentLeg && Same(command.position, lastSent)) return;
            route.Clear();
            ownsRoute = false;
            ownsHeading = false;
            if (command.player != null && CommandableShip.HasPermission(ship, command.player))
            {
                Issuer = command.player;
                ownsRoute = true;
                route.Add(command.position);
            }
        }

        private void Send(GlobalPosition destination)
        {
            lastSent = destination;
            hasSentLeg = true;
            sendingOwnOrder = true;
            try { ship.UnitCommand.SetDestination(destination, true); }
            finally { sendingOwnOrder = false; }
        }

        // One metre is far below any meaningful order difference and well above
        // the round-trip noise of a re-issued position.
        private static bool Same(GlobalPosition a, GlobalPosition b) =>
            Mathf.Abs(a.x - b.x) < 1f && Mathf.Abs(a.y - b.y) < 1f && Mathf.Abs(a.z - b.z) < 1f;

        internal void Replace(GlobalPosition waypoint)
        {
            route.Clear();
            route.Add(waypoint);
            ownsRoute = true;
            ownsHeading = false;
            Send(waypoint);
        }

        internal void Append(GlobalPosition waypoint)
        {
            bool first = route.Count == 0;
            route.Add(waypoint);
            ownsRoute = true;
            ownsHeading = false;
            if (first) Send(waypoint);
        }

        internal void ClearRoute()
        {
            route.Clear();
            ownsRoute = false;
            ownsHeading = false;
            hasSentLeg = false;
            // Hand steering back rather than freezing the ship in place.
            if (ai != null) ai.ArriveAtCommandedDestination();
        }

        internal void OrderSpeed(float knots)
        {
            if (!HasSpeedOrder || Mathf.Abs(knots - OrderedSpeedKnots) > .01f) speedIntegral = 0f;
            OrderedSpeedKnots = knots;
            HasSpeedOrder = true;
            // A telegraph order with no plotted route still needs somewhere to
            // go; give native pathfinding a rolling point off the current bow.
            if (Mathf.Abs(knots) > .01f && route.Count == 0 && !ownsHeading)
            {
                ownsRoute = false;
                ownsHeading = true;
                orderedHeading = ship.transform.forward;
                orderedHeading.y = 0f;
                orderedHeading.Normalize();
                ContinueHeading();
            }
        }

        internal void ReleaseSpeed()
        {
            HasSpeedOrder = false;
            speedIntegral = 0f;
        }

        private void ContinueHeading()
        {
            headingDestination = ship.GlobalPosition() + orderedHeading * 10000f;
            Send(headingDestination);
        }

        private void Update()
        {
            if (ship == null || ship.disabled || !ship.IsServer || !ship.LocalSim) return;

            float now = Time.timeSinceLevelLoad;
            if (now >= nextAuthorityCheck)
            {
                nextAuthorityCheck = now + .5f;
                if (!MissionManager.IsRunning || !CommandableShip.HasPermission(ship, Issuer))
                {
                    if (OwnsNavigation || HasSpeedOrder)
                    {
                        route.Clear();
                        HasSpeedOrder = false;
                        ownsRoute = false;
                        ownsHeading = false;
                        if (ai != null) ai.ArriveAtCommandedDestination();
                    }
                    return;
                }
            }

            if (ownsHeading && FastMath.Distance(ship.GlobalPosition(), headingDestination) < 2000f)
                ContinueHeading();

            // Advance the route ourselves. Native arrival starts a multi-minute
            // hold; issuing the next leg first keeps the ship moving between
            // waypoints without patching Steer.
            if (ownsRoute && route.Count > 0 &&
                FastMath.Distance(ship.GlobalPosition(), route[0]) < ship.maxRadius + 100f)
            {
                route.RemoveAt(0);
                if (route.Count > 0) Send(route[0]);
                else if (ai != null) ai.ArriveAtCommandedDestination();
            }
        }

        // ShipAI.Steer runs inside Update and rewrites throttle every 0.2 s.
        // Writing in LateUpdate makes the governor the last author before the
        // next physics step, without patching a virtual that subclasses override.
        private void LateUpdate()
        {
            if (!HasSpeedOrder || ship == null || ship.disabled || ship.rb == null) return;
            if (!ship.IsServer || !ship.LocalSim) return;
            if (ownsRoute && route.Count == 0) return;

            ShipInputs inputs = ship.GetInputs();
            if (inputs == null) return;

            float forward = Vector3.Dot(ship.rb.velocity, ship.transform.forward);
            float desired = OrderedSpeedKnots * CommandableShip.MetresPerSecondPerKnot;
            float maximum = CommandableShip.MaximumSpeedKnots(ship) * CommandableShip.MetresPerSecondPerKnot;
            float fraction = desired / Mathf.Max(1f, maximum);
            float error = desired - forward;
            // Astern orders need full authority; ahead orders must not exceed
            // whatever the native controller is already asking for in a turn.
            float upper = desired > 0f ? Mathf.Clamp(inputs.throttle, -1f, 1f) : 1f;
            float raw = fraction * Mathf.Abs(fraction) + error * .18f + speedIntegral;

            float elapsed = Mathf.Clamp(Time.timeSinceLevelLoad - lastGovernorUpdate, 0f, .25f);
            lastGovernorUpdate = Time.timeSinceLevelLoad;
            // Trim steady-state error without winding up while native avoidance,
            // a turn, or the propulsion limit is what is actually binding.
            if ((raw > -1f && raw < upper) || (raw >= upper && error < 0f) || (raw <= -1f && error > 0f))
                speedIntegral = Mathf.Clamp(speedIntegral + error * elapsed * .04f, -1f, 1f);

            inputs.throttle = Mathf.Clamp(raw, -1f, upper);
        }
    }
}

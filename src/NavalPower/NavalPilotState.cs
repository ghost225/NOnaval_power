using UnityEngine;

namespace NavalPower
{
    // A pilot state that flies what it is told and nothing else.
    //
    // The native combat state hunts: it picks its own target and closes, which
    // is why an unmanaged aircraft flies straight at the enemy and dies. This
    // drives aircraft.autopilot directly instead, so a flight holds a route, an
    // orbit or a station until ordered otherwise -- and can sit there radiating
    // as an offboard sensor while the ship that launched it stays silent.
    internal sealed class NavalPilotState : PilotBaseState
    {
        private Flight flight;
        private float orbitPhase;
        private float nextReport;

        internal static void Install(Pilot pilot, Flight flight)
        {
            if (pilot == null || flight == null || pilot.aircraft == null) return;
            var state = new NavalPilotState { flight = flight, stateDisplayName = "Naval Power" };
            state.Initialize(pilot);
            pilot.SwitchStateNew(state);
            Plugin.Log.LogInfo("[flight] " + flight.Name + " under command · " + flight.Describe());
        }

        public override void EnterState(Pilot pilot)
        {
            this.pilot = pilot;
            FindNearestAirbase();
        }

        public override void LeaveState() { }

        public override void UpdateState(Pilot pilot) { }

        public override void FixedUpdateState(Pilot pilot)
        {
            if (flight == null || aircraft == null || aircraft.disabled) return;

            // Fuel outranks every order: an aircraft that runs dry on station is
            // worse than one that broke off early.
            if (flight.Mode != FlightMode.ReturnToBase && !fuelChecker.HasEnoughFuel())
            {
                FlightOrders.ReturnToBase(flight);
                Plugin.Log.LogInfo("[flight] " + flight.Name + " returning · fuel");
            }

            Report();

            switch (flight.Mode)
            {
                case FlightMode.Route: FlyRoute(); break;
                case FlightMode.Orbit: FlyOrbit(flight.OrbitCentre); break;
                case FlightMode.Station: FlyStation(); break;
                // Both hand the aircraft to the native combat pilot; the
                // difference is that a strike has a designated target pinned
                // onto it. Missing this case left nothing driving the aircraft.
                case FlightMode.Strike:
                case FlightMode.Engage: HandBackToCombat(pilot); break;
                case FlightMode.ReturnToBase: HandBackToLanding(pilot); break;
            }
        }

        // Periodic trace: if a flight wanders, this says whether it was given
        // the wrong destination or simply refused to fly to the right one.
        private void Report()
        {
            if (Time.timeSinceLevelLoad < nextReport) return;
            nextReport = Time.timeSinceLevelLoad + 5f;
            Vector3 offset = destination - aircraft.GlobalPosition();
            float bearing = (Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg + 360f) % 360f;
            offset.y = 0f;
            float verticalError = (destination - aircraft.GlobalPosition()).y;
            Plugin.Log.LogInfo("[flight] " + flight.Name + " · " + flight.Describe() +
                " · dest bearing " + bearing.ToString("000") + "° range " + (offset.magnitude / 1000f).ToString("0.0") +
                " km · alt " + aircraft.radarAlt.ToString("0") + " ordered " + flight.Altitude.ToString("0") +
                " (dest dy " + verticalError.ToString("0") + ")" +
                " · state " + (aircraft.autopilot != null ? aircraft.autopilot.GetType().Name : "none"));
        }

        private void FlyRoute()
        {
            if (flight.Route.Count == 0) { FlyOrbit(aircraft.GlobalPosition()); return; }
            GlobalPosition leg = flight.Route[0];
            // Arrival is horizontal only: altitude is held separately, so a leg
            // must not stay "unreached" because the aircraft is high over it.
            if (Horizontal(aircraft.GlobalPosition(), leg) < 600f)
            {
                flight.Route.RemoveAt(0);
                if (flight.Route.Count == 0)
                {
                    // Hold where the route ended rather than flying on forever.
                    flight.OrbitCentre = leg;
                    flight.Mode = FlightMode.Orbit;
                    return;
                }
                leg = flight.Route[0];
            }
            Steer(leg);
        }

        // Fly the tangent, not a point on the rim.
        //
        // Chasing a point 50 degrees around the circle put the destination two
        // and a half kilometres away at a wide angle, which is the regime where
        // AutopilotPlane starts adding pull-up and never settles. Steering along
        // the tangent with a long look-ahead keeps the destination far off and
        // nearly dead ahead, which is the cruise case the autopilot handles well.
        private const float LookAhead = 6000f;

        private void FlyOrbit(GlobalPosition centre)
        {
            Vector3 offset = aircraft.GlobalPosition() - centre;
            offset.y = 0f;
            float radius = Mathf.Max(flight.OrbitRadius, 400f);
            float distance = offset.magnitude;
            Vector3 outward = distance > 1f ? offset / distance : Flat(aircraft.transform.forward);

            // Transit first. Blending a tangent with an inward correction is
            // right on the circle and useless off it: the correction saturates
            // once well outside, leaving a 45-degree drift that takes an age to
            // close. Well outside the area, just go there.
            if (distance > radius * 1.5f)
            {
                Steer(centre + outward * radius);
                return;
            }

            // On station: fly the circle. Consistent left-hand circuit, the way
            // a holding pattern is flown.
            Vector3 tangent = new Vector3(-outward.z, 0f, outward.x);
            float error = Mathf.Clamp((distance - radius) / Mathf.Max(radius, 1f), -1f, 1f);
            Vector3 heading = (tangent - outward * error).normalized;
            // Never look further ahead than the circle itself, or a small area
            // gets a look-ahead that points clean outside it.
            float lookAhead = Mathf.Min(LookAhead, radius * 1.5f);
            Steer(aircraft.GlobalPosition() + heading * lookAhead);
        }

        private static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.001f ? value.normalized : Vector3.forward;
        }

        private void FlyStation()
        {
            if (flight.Parent == null || flight.Parent.disabled) { FlyOrbit(aircraft.GlobalPosition()); return; }
            // The anchor moves with the ship, so the flight keeps company
            // instead of orbiting the spot the ship used to be.
            Vector3 forward = flight.Parent.transform.forward; forward.y = 0f; forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            GlobalPosition anchor = flight.Parent.GlobalPosition()
                + right * flight.StationOffset.x + forward * flight.StationOffset.z;
            FlyOrbit(anchor);
        }

        // The two AutoAim overloads are implemented by different autopilots:
        // AutopilotPlane overrides the nine-argument one, AutopilotHelo and
        // AutopilotTiltwing the five-argument one. Everything else falls through
        // to an empty virtual on the base class, which issues no control inputs
        // at all -- a fixed-wing given the helicopter call simply coasts on
        // stale inputs until it stalls and goes in. Dispatch on the real type.
        private void Steer(GlobalPosition target)
        {
            Autopilot autopilot = aircraft.autopilot;
            if (autopilot == null) return;

            float aboveGround = Mathf.Max(flight.Altitude, MinimumClearance);
            bool followTerrain = aboveGround < 400f;
            GlobalPosition point = AtAltitude(target, aboveGround);
            destination = point;

            if (autopilot is AutopilotPlane)
            {
                // Mirrors the native combat state's call. altitudeHold is only
                // consulted for terrain following; otherwise the destination's
                // own height carries the altitude. Bank is held well short of
                // the 180 degrees the combat state allows, since this is
                // transit rather than evasion.
                autopilot.AutoAim(point, aimVelocity: true, ignoreCollisions: false, runwayAlign: false,
                    effort: 1f, bankAllowed: 70f, followTerrain: followTerrain,
                    altitudeHold: aboveGround, targetVelocity: Vector3.zero);
            }
            else
            {
                autopilot.AutoAim(point, 0f, Vector3.zero, Vector3.zero, followTerrain);
            }
        }

        // Only these autopilots actually implement an AutoAim; anything else
        // would be flown by a method with an empty body.
        internal static bool CanBeFlown(Aircraft aircraft) =>
            aircraft != null && (aircraft.autopilot is AutopilotPlane
                || aircraft.autopilot is AutopilotHelo
                || aircraft.autopilot is AutopilotTiltwing);

        // Ground clearance at the destination, the way Autopilot.TerrainWaypoint
        // does it: sample terrain, fall back to sea level, then add the ordered
        // height above it.
        //
        // GlobalPosition is a large-world coordinate and is NOT interchangeable
        // with transform.position -- building a Vector3 from its components and
        // calling ToGlobalPosition converts a second time, displacing every
        // destination by the floating-origin offset. Convert through a delta
        // from the aircraft, and return the point raised in its own space.
        private GlobalPosition AtAltitude(GlobalPosition point, float aboveGround)
        {
            Vector3 world = aircraft.transform.position + (point - aircraft.GlobalPosition());
            float ground = Datum.LocalSeaY;
            if (Physics.Linecast(new Vector3(world.x, ground + 5000f, world.z),
                                 new Vector3(world.x, ground - 5000f, world.z),
                                 out RaycastHit hit,
                                 (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ExclusionZonesMask))
                ground = Mathf.Max(hit.point.y, Datum.LocalSeaY);
            float wanted = ground + Mathf.Max(aboveGround, MinimumClearance);
            return point + Vector3.up * (wanted - world.y);
        }

        // Never command a flight lower than this above the ground, whatever is
        // selected: the autopilot needs room to arrest a descent.
        private const float MinimumClearance = 55f;

        private void HandBackToCombat(Pilot pilot)
        {
            // No combat state to hand to: keep flying it ourselves rather than
            // leaving the aircraft with nobody at the controls.
            if (pilot.AICombatState == null) { FlyOrbit(flight.OrbitCentre); return; }
            Plugin.Log.LogInfo("[flight] " + flight.Name + " · handing to the combat pilot · " + flight.Describe());
            pilot.SwitchStateNew(pilot.AICombatState);
        }

        private void HandBackToLanding(Pilot pilot)
        {
            PilotBaseState landing = pilot.AIHeloLandingState != null && IsRotary(pilot)
                ? (PilotBaseState)pilot.AIHeloLandingState : pilot.AILandingState;
            if (landing == null) { FlyOrbit(aircraft.GlobalPosition()); return; }
            Plugin.Log.LogInfo("[flight] " + flight.Name + " recovering");
            pilot.SwitchStateNew(landing);
        }

        private static bool IsRotary(Pilot pilot) => pilot.AIHeloCombatState != null;

        private static float Horizontal(GlobalPosition a, GlobalPosition b)
        {
            Vector3 offset = a - b;
            offset.y = 0f;
            return offset.magnitude;
        }
    }
}

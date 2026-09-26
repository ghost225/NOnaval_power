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
            controlInputs = aircraft.GetInputs();
            parameters = aircraft.GetAircraftParameters();
            FindNearestAirbase();

            // The same handover the native combat state performs. Without it a
            // helicopter arrives from its takeoff state with auto-hover still
            // engaged, and the controls filter then fights the autopilot's
            // collective all the way into the ground.
            aircraft.SetFlightAssistToDefault();
            aircraft.SetGear(deployed: false);
            ControlsFilter filter = aircraft.GetControlsFilter();
            if (filter != null) filter.SetAutoHover(enabled: false);
        }

        private AircraftParameters parameters;

        // The native combat and landing states never touch the throttle -- an
        // AI jet cruises flat out -- so whatever formation keeping left it at
        // would be inherited. Leave it where they expect it.
        public override void LeaveState()
        {
            if (controlInputs != null && aircraft != null && aircraft.autopilot is AutopilotPlane) controlInputs.throttle = 1f;
        }

        // A lead with wingmen in formation holds a little power back, so they
        // have speed in hand to close up with.
        private const float LeadThrottle = 0.85f;

        public override void UpdateState(Pilot pilot) { }

        public override void FixedUpdateState(Pilot pilot)
        {
            if (flight == null || aircraft == null || aircraft.disabled) return;

            // Fuel outranks every order: an aircraft that runs dry on station is
            // worse than one that broke off early.
            if (flight.Mode != FlightMode.ReturnToBase && !fuelChecker.HasEnoughFuel())
            {
                FlightOrders.ReturnToBase(flight);
                Plugin.Log.LogInfo("[flight] " + flight.Name + " returning · fuel at " +
                    (aircraft.GetFuelLevel() * 100f).ToString("0") + "%, below the pilot's 20% minimum");
                CommandState.Say(flight.Name + " · low fuel, returning");
            }

            Report();

            if (aircraft.autopilot is AutopilotPlane && flight.Mode != FlightMode.Formation)
                controlInputs.throttle = Time.timeSinceLevelLoad < flight.ThrottleCutUntil ? 0f
                    : Wings.HasFollowers(flight) ? LeadThrottle : 1f;

            switch (flight.Mode)
            {
                case FlightMode.Formation: FlyFormation(); break;
                case FlightMode.Route: FlyRoute(); break;
                case FlightMode.Orbit: FlyOrbit(flight.OrbitCentre); break;
                case FlightMode.Station: FlyStation(); break;
                case FlightMode.Egress: FlyEgress(); break;
                case FlightMode.Jam: FlyJamming(); break;
                // Both hand the aircraft to the native combat pilot; the
                // difference is that a strike has a designated target pinned
                // onto it. Missing this case left nothing driving the aircraft.
                case FlightMode.Strike:
                    if (flight.RunInDone || !(aircraft.autopilot is AutopilotPlane)) HandBackToCombat(pilot);
                    else FlyRunIn(pilot);
                    break;
                case FlightMode.Engage: HandBackToCombat(pilot); break;
                case FlightMode.ReturnToBase: HandBackToLanding(pilot); break;
                // A mode with no arm here writes no control inputs at all, and
                // the aircraft simply falls out of the sky -- which is how both
                // Strike and Cargo were first found. Holding is always wrong
                // for the order, but it is never fatal, and it says so.
                default: FlyOrbit(aircraft.GlobalPosition()); WarnUnflown(); break;
            }
        }

        private FlightMode warnedMode = (FlightMode)(-1);

        private void WarnUnflown()
        {
            if (warnedMode == flight.Mode) return;
            warnedMode = flight.Mode;
            Plugin.Log.LogWarning("[flight] " + flight.Name + " · nothing flies " + flight.Mode +
                " from this state; holding overhead instead");
        }

        // Periodic trace: if a flight wanders, this says whether it was given
        // the wrong destination or simply refused to fly to the right one.
        private void Report()
        {
            if (!Settings.FlightTrace.Value || Time.timeSinceLevelLoad < nextReport) return;
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

        // About seventeen degrees. Steeper than this and a rotary aircraft
        // pitches up hard enough to lose control rather than climb.
        private const float MaxClimbGradient = 0.3f;

        // How far ahead a rotary aircraft is ever asked to steer. Beyond this
        // the tilt PID saturates and the aircraft wallows rather than flies.
        private const float RotaryLead = 1500f;

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

        // Out low and away. Height is the thing a departing aircraft can trade
        // for survival, so the egress leg ignores the flight's ordered altitude
        // and runs at the egress height instead.
        private void FlyEgress()
        {
            float ordered = flight.Altitude;
            flight.Altitude = Mathf.Min(ordered, Settings.EgressAltitude.Value);
            Steer(flight.EgressPoint);
            flight.Altitude = ordered;
        }

        // Hold off the emitter and keep the pod on it. The pod switches itself
        // off in LateUpdate unless Fire is called again, so jamming has to be
        // asserted every frame rather than commanded once.
        private void FlyJamming()
        {
            Unit target = flight.Target;
            if (target == null || target.disabled) { FlyOrbit(aircraft.GlobalPosition()); return; }

            float standoff = Mathf.Max(Settings.JammingStandoff.Value, 1000f);
            float radius = flight.OrbitRadius;
            flight.OrbitRadius = standoff;
            FlyOrbit(target.GlobalPosition());
            flight.OrbitRadius = radius;

            WeaponStation station = FlightOrders.JammerOn(aircraft);
            if (station == null) return;
            foreach (Weapon weapon in station.Weapons)
            {
                if (!(weapon is JammingPod pod)) continue;
                pod.SetTarget(target);
                pod.Fire(aircraft, target, aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero,
                    station, default(GlobalPosition));
            }
        }

        private void FlyStation()
        {
            if (flight.Home == null || flight.Home.disabled) { FlyOrbit(aircraft.GlobalPosition()); return; }
            // A land field does not move: work an area over it.
            if (flight.Parent == null) { FlyOrbit(flight.HomePosition); return; }
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
        // A wingman's slot, in the lead's frame, flown with a throttle loop on
        // the along-track gap. The jet autopilot does not match a target's
        // speed -- its velocity input only biases the climb -- so speed is held
        // with power; the helicopter autopilot does take a velocity to match.
        private void FlyFormation()
        {
            Flight lead = Wings.LeadOf(flight);
            Aircraft leader = lead?.Aircraft;
            if (lead == flight || leader == null || leader.disabled)
            {
                // No one to fly on: hold here until given something to do.
                flight.OrbitCentre = aircraft.GlobalPosition();
                flight.Mode = FlightMode.Orbit;
                return;
            }

            Vector3 velocity = leader.rb != null ? leader.rb.velocity : leader.transform.forward * 100f;
            Vector3 forward = new Vector3(velocity.x, 0f, velocity.z);
            if (forward.sqrMagnitude < 25f) forward = new Vector3(leader.transform.forward.x, 0f, leader.transform.forward.z);
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);

            bool rotary = !(aircraft.autopilot is AutopilotPlane);
            Vector3 offset = Wings.SlotOffset(flight, rotary);
            GlobalPosition slot = leader.GlobalPosition() + right * offset.x + forward * offset.z;
            flight.Altitude = lead.Altitude;

            Vector3 gap = slot - aircraft.GlobalPosition();
            gap.y = 0f;
            float along = Vector3.Dot(gap, forward);          // positive: behind the slot
            float distance = gap.magnitude;

            if (rotary)
            {
                Steer(slot, velocity);
                return;
            }

            // Aim down the lead's track from the slot rather than at the slot
            // itself, so the aircraft settles alongside instead of overshooting
            // a point and circling back to it; further out, lead it more.
            GlobalPosition aim = slot + forward * Mathf.Clamp(distance * 0.5f + 1500f, 1500f, 6000f);
            float speedGap = Vector3.Dot(velocity, forward) - Vector3.Dot(aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero, forward);
            float power = distance > 4000f && along > 0f ? 1f
                : Mathf.Clamp(LeadThrottle + along * 0.0006f + speedGap * 0.02f, 0.35f, 1f);
            controlInputs.throttle = power;
            Steer(aim, velocity);
        }

        private void Steer(GlobalPosition target, Vector3 velocity = default)
        {
            Autopilot autopilot = aircraft.autopilot;
            if (autopilot == null) return;

            float aboveGround = Mathf.Max(flight.Altitude, MinimumClearance);

            if (autopilot is AutopilotPlane)
            {
                // Fixed wing: the destination's own height carries the altitude,
                // and altitudeHold is consulted only for terrain following.
                // Bank is held well short of the 180 degrees the combat state
                // allows, since this is transit rather than evasion.
                bool followTerrain = aboveGround < 400f;
                GlobalPosition point = AtAltitude(target, aboveGround);
                destination = point;
                autopilot.AutoAim(point, aimVelocity: true, ignoreCollisions: false, runwayAlign: false,
                    effort: 1f, bankAllowed: 70f, followTerrain: followTerrain,
                    altitudeHold: aboveGround, targetVelocity: velocity);
                return;
            }

            // Rotary and tiltwing read altitudeHold as the height to hold above
            // the ground: it is fed into TerrainWaypoint and compared against
            // radarAlt. The native helo state passes minimumRadarAlt plus a
            // desiredHeight it slews by tens of metres a second -- it never
            // steps it.
            //
            // That gradualness is load-bearing. TerrainWaypoint places the
            // waypoint only max(speed, 100) * 6 metres ahead, so at low speed
            // that is 600 m; asking for 600 m of height there is a 45-degree
            // climb, and the attitude controller answers by standing the
            // aircraft on its tail until it departs. Ask for no more height
            // than the aircraft can reach at a sane gradient from where it is,
            // and let it walk up to the ordered altitude over successive frames.
            float floor = parameters != null ? parameters.minimumRadarAlt : 0f;
            float lookAhead = Mathf.Max(aircraft.speed, 100f) * 6f;
            float wanted = floor + aboveGround;
            float reachable = aircraft.radarAlt + lookAhead * MaxClimbGradient;
            float commanded = Mathf.Clamp(Mathf.Min(wanted, reachable), floor, floor + 1000f);

            // Bounded steering point.
            //
            // AutopilotHelo feeds the raw horizontal offset to the destination
            // straight into a tilt PID. The native state hands it a target a
            // few kilometres away at most; a task area tens of kilometres off
            // saturates that PID at maximum tilt and the aircraft oscillates
            // instead of flying. Aim at a point a bounded distance along the
            // bearing instead -- it moves with the aircraft, so the course is
            // unchanged, but the error the PID sees stays in its working range.
            GlobalPosition here = aircraft.GlobalPosition();
            Vector3 bearing = target - here;
            bearing.y = 0f;
            float span = bearing.magnitude;
            GlobalPosition aim = span > RotaryLead
                ? here + bearing / span * RotaryLead
                : target;

            destination = aim;
            autopilot.AutoAim(aim, commanded, Vector3.zero, velocity, followTerrain: true);
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
        private static float MinimumClearance => Settings.MinimumClearance.Value;

        // Set up the attack: down to a height the weapon can be released from,
        // toward the target, and hand over once inside release range.
        //
        // Too high to line up in the range left, or simply too close, it first
        // opens out -- toward friendly lines, the side its home is on, rather
        // than on past the target -- to a set-up point far enough off to
        // descend and turn in, and runs in from there. The set-up is a stage
        // that completes, so it cannot flip between opening out and turning in.
        private void FlyRunIn(Pilot pilot)
        {
            Unit target = flight.Target;
            FactionHQ hq = aircraft.NetworkHQ;
            WeaponStation station = FlightOrders.NamedStation(aircraft, flight.PreferredWeapon) ??
                (target != null ? FlightOrders.BestStationFor(aircraft, target) : null);
            if (target == null || target.disabled || hq == null || !hq.TryGetKnownPosition(target, out GlobalPosition known) ||
                !FlightOrders.RunInFor(station?.WeaponInfo, out float height, out float release))
            {
                CompleteRunIn(pilot, "no run-in needed");
                return;
            }

            GlobalPosition here = aircraft.GlobalPosition();
            float range = Horizontal(known, here);
            bool low = aircraft.radarAlt <= height + Mathf.Max(250f, height * 0.25f);
            if (Time.timeSinceLevelLoad - flight.RunInStarted > 240f) { CompleteRunIn(pilot, "run-in timed out"); return; }

            if (!flight.SettingUp && (range < release * 0.5f || (!low && range < release)))
            {
                flight.SettingUp = true;
                Plugin.Log.LogInfo("[flight] " + flight.Name + " · opening out to set up the run");
            }

            GlobalPosition aim = known;
            if (flight.SettingUp)
            {
                Vector3 friendly = flight.HomePosition - known;
                friendly.y = 0f;
                if (friendly.sqrMagnitude < 1f) friendly = here - known;
                friendly.y = 0f;
                if (friendly.sqrMagnitude < 1f) friendly = -aircraft.transform.forward;
                GlobalPosition setUp = known + friendly.normalized * release * 1.3f;
                if (Horizontal(setUp, here) < 1500f || (low && range >= release * 1.15f))
                {
                    flight.SettingUp = false;
                    Plugin.Log.LogInfo("[flight] " + flight.Name + " · turning in for the run");
                }
                else aim = setUp;
            }
            else if (low && range < release) { CompleteRunIn(pilot, "in position"); return; }

            float ordered = flight.Altitude;
            flight.Altitude = height;
            Steer(aim);
            flight.Altitude = ordered;
        }

        private void CompleteRunIn(Pilot pilot, string why)
        {
            flight.RunInDone = true;
            flight.StrikeStarted = Time.timeSinceLevelLoad;     // patience runs from the attack, not the transit
            Plugin.Log.LogInfo("[flight] " + flight.Name + " · run-in complete · " + why + " · alt " +
                aircraft.radarAlt.ToString("0") + " m");
            HandBackToCombat(pilot);
        }

        private void HandBackToCombat(Pilot pilot)
        {
            // No combat state to hand to: keep flying it ourselves rather than
            // leaving the aircraft with nobody at the controls.
            PilotBaseState combat = FlightOrders.CombatStateFor(pilot);
            if (combat == null) { FlyOrbit(flight.OrbitCentre); return; }
            Plugin.Log.LogInfo("[flight] " + flight.Name + " · handing to the combat pilot · " + flight.Describe());
            pilot.SwitchStateNew(combat);
        }

        private void HandBackToLanding(Pilot pilot)
        {
            PilotBaseState landing = FlightOrders.IsRotary(pilot) && pilot.AIHeloLandingState != null
                ? (PilotBaseState)pilot.AIHeloLandingState : pilot.AILandingState;
            if (landing == null) { FlyOrbit(aircraft.GlobalPosition()); return; }
            Plugin.Log.LogInfo("[flight] " + flight.Name + " recovering");
            pilot.SwitchStateNew(landing);
        }

        private static float Horizontal(GlobalPosition a, GlobalPosition b)
        {
            Vector3 offset = a - b;
            offset.y = 0f;
            return offset.magnitude;
        }
    }
}

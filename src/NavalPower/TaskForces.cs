using System.Collections.Generic;
using UnityEngine;

namespace NavalPower
{
    internal enum Formation { Screen, Column, Abreast, Box }

    // Ships that sail as one: escorts keeping station on a guide.
    //
    // The game has no ship formations, so this is built on our own navigation:
    // each escort is given a waypoint a little ahead of its station and a speed
    // to hold, and both are revised as the guide moves. Orders to the guide
    // move the whole force; an order given straight to an escort detaches it
    // until it is told to rejoin.
    //
    // Stations are a bearing and range from the guide, turning with the guide's
    // course (or fixed to north, if asked). The guide itself is followed
    // through a smoothed copy of its position and course, so a small wobble
    // does not swing the screen, and under fire the smoothing all but stops --
    // a guide jinking to dodge a missile does not drag its escorts through the
    // same manoeuvre.
    internal sealed class TaskForce
    {
        internal string Name;
        internal Ship Guide;
        internal readonly List<Escort> Escorts = new List<Escort>();
        internal Formation Formation = Formation.Screen;
        internal float Spacing = 1f;             // multiplier on the size-based gap
        internal bool FixedNorth;

        // The smoothed guide the stations hang off.
        internal GlobalPosition Centre;
        internal Vector3 Course = Vector3.forward;
        internal float LastSmooth = -1f;
        internal bool UnderFire;

        internal IEnumerable<Ship> Ships()
        {
            if (Guide != null) yield return Guide;
            foreach (Escort escort in Escorts) if (escort.Ship != null) yield return escort.Ship;
        }

        internal int Count => (Guide != null ? 1 : 0) + Escorts.Count;
    }

    internal sealed class Escort
    {
        internal Ship Ship;
        internal float Bearing;                  // degrees off the guide's course (or true, fixed to north)
        internal float Range;                    // metres
        internal bool ThreatArc;                 // a screen station, centred on the threat bearing
        internal bool Detached;
        internal GlobalPosition LastAim;
        internal float NextIssue;
        internal float LastSpeed = -999f;
        internal float OffStation;
        internal GlobalPosition Station;
        internal bool GivingWay;
    }

    internal static class TaskForces
    {
        private static readonly List<TaskForce> forces = new List<TaskForce>();
        private static readonly string[] Names =
        {
            "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliet"
        };

        // Set while the force itself is steering a ship, so its own orders are
        // not mistaken for the player's and detach the escort.
        private static bool issuing;
        private static float guideOrderedAt = -99f;
        private static TaskForce guideOrderedForce;
        private static float nextTick;

        internal static IReadOnlyList<TaskForce> All => forces;

        internal static TaskForce Of(Ship ship)
        {
            if (ship == null) return null;
            foreach (TaskForce force in forces)
            {
                if (force.Guide == ship) return force;
                foreach (Escort escort in force.Escorts) if (escort.Ship == ship) return force;
            }
            return null;
        }

        internal static Escort EscortOf(Ship ship)
        {
            TaskForce force = Of(ship);
            if (force == null) return null;
            foreach (Escort escort in force.Escorts) if (escort.Ship == ship) return escort;
            return null;
        }

        // ---- forming and changing --------------------------------------------

        internal static TaskForce Create(Ship guide)
        {
            if (guide == null || Of(guide) != null) return Of(guide);
            var used = new HashSet<string>();
            foreach (TaskForce force in forces) used.Add(force.Name);
            string name = "Task Force";
            foreach (string candidate in Names) if (!used.Contains(candidate)) { name = candidate; break; }
            var created = new TaskForce { Name = name, Guide = guide };
            forces.Add(created);
            Plugin.Log.LogInfo("[tf] " + name + " formed on " + ShipNames.Of(guide));
            return created;
        }

        internal static bool Add(TaskForce force, Ship ship, out string reason)
        {
            reason = null;
            if (force == null || ship == null) return false;
            if (!CommandableShip.CanCommand(ship, out reason)) return false;
            TaskForce existing = Of(ship);
            if (existing == force) { reason = ShipNames.Of(ship) + " is already in " + force.Name + "."; return false; }
            if (existing != null) Remove(ship);
            force.Escorts.Add(new Escort { Ship = ship });
            Layout(force);
            Plugin.Log.LogInfo("[tf] " + ShipNames.Of(ship) + " joins " + force.Name);
            return true;
        }

        internal static void Remove(Ship ship)
        {
            TaskForce force = Of(ship);
            if (force == null) return;
            if (force.Guide == ship)
            {
                Release(ship);
                force.Guide = null;
                PromoteGuide(force);
            }
            else
            {
                force.Escorts.RemoveAll(e => e.Ship == ship);
                Release(ship);
            }
            if (force.Count < 2) Disband(force);
            else Layout(force);
        }

        internal static void Disband(TaskForce force)
        {
            if (force == null) return;
            foreach (Ship ship in new List<Ship>(force.Ships())) Release(ship);
            force.Escorts.Clear();
            forces.Remove(force);
            Plugin.Log.LogInfo("[tf] " + force.Name + " disbanded");
        }

        internal static void MakeGuide(Ship ship)
        {
            TaskForce force = Of(ship);
            if (force == null || force.Guide == ship) return;
            Ship old = force.Guide;
            force.Escorts.RemoveAll(e => e.Ship == ship);
            Release(ship);
            force.Guide = ship;
            if (old != null) force.Escorts.Add(new Escort { Ship = old });
            force.LastSmooth = -1f;
            Layout(force);
            Plugin.Log.LogInfo("[tf] " + force.Name + " · " + ShipNames.Of(ship) + " is now the guide");
        }

        internal static void SetFormation(TaskForce force, Formation formation)
        {
            if (force == null) return;
            force.Formation = formation;
            Layout(force);
        }

        internal static void SetSpacing(TaskForce force, float spacing)
        {
            if (force == null) return;
            force.Spacing = Mathf.Clamp(spacing, 0.5f, 4f);
            Layout(force);
        }

        internal static void Rejoin(Ship ship)
        {
            Escort escort = EscortOf(ship);
            if (escort == null) return;
            escort.Detached = false;
            escort.NextIssue = 0f;
            escort.LastSpeed = -999f;
        }

        // A navigation order from anyone but the force itself. To an escort
        // it detaches it -- unless it came with an order to the guide a moment
        // before, close by, which is the whole force moving together. To the
        // guide it is the force's order, and noted as such.
        internal static void NoteOrder(Ship ship)
        {
            if (issuing || ship == null) return;
            TaskForce force = Of(ship);
            if (force == null) return;
            if (force.Guide == ship)
            {
                guideOrderedAt = Time.timeSinceLevelLoad;
                guideOrderedForce = force;
                return;
            }
            Escort escort = EscortOf(ship);
            if (escort == null || escort.Detached) return;
            bool together = guideOrderedForce == force && Time.timeSinceLevelLoad - guideOrderedAt < 2f &&
                force.Guide != null && FastMath.Distance(ship.GlobalPosition(), force.Guide.GlobalPosition()) < 3000f;
            if (together) return;
            escort.Detached = true;
            Plugin.Log.LogInfo("[tf] " + ShipNames.Of(ship) + " detached from " + force.Name);
            CommandState.Say(ShipNames.Of(ship) + " · detached from " + force.Name + " · Return to formation to rejoin");
        }

        // Handing a ship's navigation back: its route and speed order cleared,
        // its speed cap lifted.
        private static void Release(Ship ship)
        {
            if (ship == null) return;
            var route = ship.GetComponent<ShipRoute>();
            if (route != null) route.SpeedCapKnots = float.PositiveInfinity;
        }

        private static void PromoteGuide(TaskForce force)
        {
            Escort next = null;
            foreach (Escort escort in force.Escorts)
                if (escort.Ship != null && !escort.Ship.disabled && !escort.Detached) { next = escort; break; }
            if (next == null)
                foreach (Escort escort in force.Escorts)
                    if (escort.Ship != null && !escort.Ship.disabled) { next = escort; break; }
            if (next == null) return;
            force.Escorts.Remove(next);
            force.Guide = next.Ship;
            force.LastSmooth = -1f;
            Plugin.Log.LogInfo("[tf] " + force.Name + " · " + ShipNames.Of(next.Ship) + " takes the guide");
            CommandState.Say(force.Name + " · " + ShipNames.Of(next.Ship) + " takes the guide");
        }

        // ---- stations ------------------------------------------------------------

        private enum Role { Main, Ring, Screen }

        // By hull: carriers, assault ships and anything unarmed or big are the
        // main body; destroyers and frigates ring it; corvettes and patrol craft
        // screen ahead.
        private static Role RoleOf(Ship ship)
        {
            string type = (ship.definition?.unitName ?? "").ToLowerInvariant();
            if (type.Contains("carrier") || type.Contains("assault") || type.Contains("landing") ||
                type.Contains("cargo") || type.Contains("tanker")) return Role.Main;
            if (type.Contains("corvette") || type.Contains("patrol") || type.Contains("boat")) return Role.Screen;
            if (type.Contains("destroyer") || type.Contains("frigate") || type.Contains("cruiser")) return Role.Ring;
            RoleIdentity role = ship.definition != null ? ship.definition.roleIdentity : default;
            if (role.antiAir + role.antiSurface + role.antiMissile + role.antiRadar <= 0.01f) return Role.Main;
            return ship.maxRadius > 90f ? Role.Main : Role.Ring;
        }

        // The basic gap: scaled to the guide's size, never tight.
        internal static float Gap(TaskForce force)
        {
            float radius = force.Guide != null ? force.Guide.maxRadius : 60f;
            return Mathf.Max(3f * radius, 600f) * force.Spacing;
        }

        // Lays out every escort's station for the force's formation.
        internal static void Layout(TaskForce force)
        {
            float gap = Gap(force);
            var main = new List<Escort>();
            var ring = new List<Escort>();
            var screen = new List<Escort>();
            foreach (Escort escort in force.Escorts)
            {
                escort.ThreatArc = false;
                if (force.Formation != Formation.Screen) continue;
                switch (RoleOf(escort.Ship))
                {
                    case Role.Main: main.Add(escort); break;
                    case Role.Screen: screen.Add(escort); break;
                    default: ring.Add(escort); break;
                }
            }

            switch (force.Formation)
            {
                case Formation.Column:
                    for (int i = 0; i < force.Escorts.Count; i++)
                        Place(force.Escorts[i], 180f, gap * 1.4f * (i + 1));
                    return;
                case Formation.Abreast:
                    for (int i = 0; i < force.Escorts.Count; i++)
                        Place(force.Escorts[i], i % 2 == 0 ? 90f : 270f, gap * 1.4f * (i / 2 + 1));
                    return;
                case Formation.Box:
                    float[] corners = { 45f, 315f, 135f, 225f };
                    for (int i = 0; i < force.Escorts.Count; i++)
                        Place(force.Escorts[i], corners[i % 4], gap * 1.8f * (i / 4 + 1));
                    return;
            }

            // Screen: the main body astern, a ring round the guide, pickets ahead.
            for (int i = 0; i < main.Count; i++)
                Place(main[i], 180f + (i % 2 == 0 ? 1 : -1) * 15f * ((i + 1) / 2), gap * 1.5f * (i / 2 + 1));
            float[] ringBearings = { 45f, 315f, 135f, 225f, 90f, 270f, 0f, 180f };
            for (int i = 0; i < ring.Count; i++)
                Place(ring[i], ringBearings[i % ringBearings.Length], gap * 2f * (1f + 0.6f * (i / ringBearings.Length)));
            float arc = Mathf.Max(10f * (force.Guide != null ? force.Guide.maxRadius : 60f), 2000f) * force.Spacing;
            for (int i = 0; i < screen.Count; i++)
            {
                int rank = i / 5, slot = i % 5;
                float offset = (slot - 2) * 17.5f + (rank % 2 == 1 ? 8.75f : 0f);
                Place(screen[i], offset, arc + 800f * rank);
                screen[i].ThreatArc = true;
            }
        }

        private static void Place(Escort escort, float bearing, float range)
        {
            escort.Bearing = (bearing % 360f + 360f) % 360f;
            escort.Range = range;
            escort.NextIssue = 0f;
        }

        // ---- every tick --------------------------------------------------------

        internal static void Tick()
        {
            if (forces.Count == 0 || Time.timeSinceLevelLoad < nextTick) return;
            float dt = nextTick <= 0f ? 0.5f : Time.timeSinceLevelLoad - (nextTick - 0.5f);
            nextTick = Time.timeSinceLevelLoad + 0.5f;

            foreach (TaskForce force in new List<TaskForce>(forces))
            {
                force.Escorts.RemoveAll(e => e.Ship == null || e.Ship.disabled);
                if (force.Guide == null || force.Guide.disabled) { force.Guide = null; PromoteGuide(force); }
                if (force.Guide == null || force.Count < 2) { Disband(force); continue; }
                Smooth(force, dt);
                float worst = 0f;
                foreach (Escort escort in force.Escorts)
                {
                    if (escort.Detached) continue;
                    Keep(force, escort);
                    worst = Mathf.Max(worst, escort.OffStation);
                }
                Pace(force, worst);
            }
        }

        // Time-based, not per frame: a couple of seconds to follow the guide's
        // position and eight for its course; under fire, a minute and more.
        private static void Smooth(TaskForce force, float dt)
        {
            Ship guide = force.Guide;
            GlobalPosition here = guide.GlobalPosition();
            Vector3 velocity = guide.rb != null ? guide.rb.velocity : Vector3.zero;
            Vector3 course = new Vector3(velocity.x, 0f, velocity.z);
            if (course.sqrMagnitude < 1f) course = new Vector3(guide.transform.forward.x, 0f, guide.transform.forward.z);
            course.Normalize();

            force.UnderFire = false;
            foreach (Unit unit in UnitRegistry.allUnits)
                if (unit is Missile missile && !missile.disabled && missile.targetID == guide.persistentID) { force.UnderFire = true; break; }

            if (force.LastSmooth < 0f)
            {
                force.Centre = here;
                force.Course = course;
                force.LastSmooth = Time.timeSinceLevelLoad;
                return;
            }
            force.LastSmooth = Time.timeSinceLevelLoad;
            float positionTau = force.UnderFire ? 60f : 2f;
            float courseTau = force.UnderFire ? 90f : 8f;
            float kp = 1f - Mathf.Exp(-dt / positionTau), kc = 1f - Mathf.Exp(-dt / courseTau);
            // The smoothed centre also moves with the guide's velocity, so it
            // does not lag a steadily steaming guide.
            force.Centre = force.Centre + velocity * dt;
            force.Centre = force.Centre + (here - force.Centre) * kp;
            force.Course = Vector3.Slerp(force.Course, course, kc).normalized;
        }

        private static float ThreatBearing(TaskForce force)
        {
            FactionHQ hq = force.Guide.NetworkHQ;
            float course = Mathf.Atan2(force.Course.x, force.Course.z) * Mathf.Rad2Deg;
            if (hq == null) return course;
            float best = 60000f;
            float bearing = course;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Ship ship) || ship.disabled || ship.NetworkHQ == null || ship.NetworkHQ == hq) continue;
                if (!hq.TryGetKnownPosition(ship, out GlobalPosition known)) continue;
                Vector3 offset = known - force.Centre;
                offset.y = 0f;
                float range = offset.magnitude;
                if (range >= best) continue;
                best = range;
                bearing = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            }
            return bearing;
        }

        internal static GlobalPosition StationOf(TaskForce force, Escort escort)
        {
            float reference = escort.ThreatArc ? ThreatBearing(force)
                : force.FixedNorth ? 0f : Mathf.Atan2(force.Course.x, force.Course.z) * Mathf.Rad2Deg;
            float radians = (reference + escort.Bearing) * Mathf.Deg2Rad;
            return force.Centre + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * escort.Range;
        }

        private static void Keep(TaskForce force, Escort escort)
        {
            Ship ship = escort.Ship;
            Ship guide = force.Guide;
            GlobalPosition station = StationOf(force, escort);
            escort.Station = station;
            GlobalPosition here = ship.GlobalPosition();
            Vector3 gap = station - here;
            gap.y = 0f;
            escort.OffStation = gap.magnitude;
            float along = Vector3.Dot(gap, force.Course);   // positive: behind its station

            // Speed: the guide's, plus the along-track gap; flat out when well
            // adrift, never so slow it loses steerage.
            float guideKnots = guide.rb != null
                ? Vector3.Dot(guide.rb.velocity, force.Course) / CommandableShip.MetresPerSecondPerKnot : 0f;
            float maximum = CommandableShip.MaximumSpeedKnots(ship);
            float knots = escort.OffStation > 3000f && along > 0f ? maximum
                : Mathf.Clamp(guideKnots + along * 0.004f, 3f, maximum);

            // Aim well ahead of the station along the course -- never at it, or
            // the native arrival hold stops the ship there.
            float lead = Mathf.Max(6f * ship.maxRadius, 600f);
            GlobalPosition aim = station + force.Course * lead;
            if (escort.OffStation > lead * 2f) aim = station + force.Course * (lead * 0.5f);

            escort.GivingWay = GiveWay(force, escort, ref aim, ref knots);

            issuing = true;
            try
            {
                float radius = Mathf.Max(2f * ship.maxRadius, 200f);
                if ((escort.NextIssue <= 0f || FastMath.Distance(aim, escort.LastAim) > radius) &&
                    Time.timeSinceLevelLoad >= escort.NextIssue)
                {
                    if (NavigationOrders.ReplaceWaypoint(ship, aim, out _))
                    {
                        escort.LastAim = aim;
                        escort.NextIssue = Time.timeSinceLevelLoad + 3f;
                    }
                }
                if (Mathf.Abs(knots - escort.LastSpeed) > 0.5f &&
                    NavigationOrders.SetOrderedSpeedKnots(ship, knots, out _))
                    escort.LastSpeed = knots;
            }
            finally { issuing = false; }
        }

        // Closest point of approach against every ship nearby: if two will
        // pass inside a safe distance in the next minute and a half, the escort
        // gives way -- eases off and steers to starboard of the other's track.
        private static bool GiveWay(TaskForce force, Escort escort, ref GlobalPosition aim, ref float knots)
        {
            Ship ship = escort.Ship;
            if (ship.rb == null) return false;
            Vector3 myVelocity = ship.rb.velocity;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Ship other) || other == ship || other.disabled || other.rb == null) continue;
                Vector3 offset = other.GlobalPosition() - ship.GlobalPosition();
                offset.y = 0f;
                if (offset.sqrMagnitude > 3000f * 3000f) continue;
                Vector3 relative = other.rb.velocity - myVelocity;
                relative.y = 0f;
                float speed = relative.sqrMagnitude;
                if (speed < 0.01f) continue;
                float t = -Vector3.Dot(offset, relative) / speed;
                if (t < 0f || t > 90f) continue;
                float miss = (offset + relative * t).magnitude;
                float safe = (ship.maxRadius + other.maxRadius) * 1.5f + 100f;
                if (miss >= safe) continue;
                Vector3 forward = myVelocity.sqrMagnitude > 1f ? myVelocity.normalized : ship.transform.forward;
                forward.y = 0f;
                Vector3 starboard = new Vector3(forward.z, 0f, -forward.x).normalized;
                aim = ship.GlobalPosition() + (forward + starboard).normalized * 1500f;
                knots = Mathf.Max(3f, knots * 0.5f);
                escort.NextIssue = 0f;              // steer away now, not in three seconds
                return true;
            }
            return false;
        }

        // The guide slows for escorts well off station -- but not while it is
        // being shot at, when getting clear matters more than keeping station.
        private static void Pace(TaskForce force, float worst)
        {
            var route = force.Guide.GetComponent<ShipRoute>();
            if (route == null) return;
            float threshold = Mathf.Max(6f * force.Guide.maxRadius, 1200f);
            if (force.UnderFire || worst <= threshold) { route.SpeedCapKnots = float.PositiveInfinity; return; }
            float maximum = CommandableShip.MaximumSpeedKnots(force.Guide);
            float fraction = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01((worst - threshold) / 3000f));
            route.SpeedCapKnots = maximum * fraction;
        }

        internal static string Describe(TaskForce force, Escort escort)
        {
            if (escort.Detached) return "detached";
            if (escort.GivingWay) return "giving way";
            float threshold = Mathf.Max(3f * escort.Ship.maxRadius, 400f);
            return escort.OffStation <= threshold ? "on station"
                : "closing · " + UnitConverter.DistanceReading(escort.OffStation);
        }
    }
}

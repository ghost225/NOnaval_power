using System.Collections.Generic;
using UnityEngine;

namespace NavalPower
{
    internal enum Formation { Screen, Column, Abreast, Box, Custom }

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
        // The guide's track, newest last: a column follows it, so its ships
        // turn in succession where the guide turned.
        internal readonly List<GlobalPosition> Wake = new List<GlobalPosition>();

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
            if (!forces.Contains(force)) { reason = "That task force no longer exists."; return false; }
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
            // A force lives as long as it has a guide: formed first and filled
            // afterwards, or down to its last ship, it is still there to add to.
            if (force.Guide == null) Disband(force);
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

        // A station dragged in the formation editor: the formation becomes a
        // custom one, and the escort sails for it straight away.
        internal static void MoveStation(TaskForce force, Escort escort, float bearing, float metres)
        {
            if (force == null || escort == null) return;
            force.Formation = Formation.Custom;
            escort.ThreatArc = false;
            escort.Bearing = bearing;
            escort.Range = metres;
            escort.NextIssue = 0f;
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
            return Mathf.Max(2f * radius, 400f) * force.Spacing;
        }

        // Lays out every escort's station for the force's formation.
        internal static void Layout(TaskForce force)
        {
            if (force.Formation == Formation.Custom) return;     // stations as dragged
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

            var slots = new List<Slot>();
            switch (force.Formation)
            {
                case Formation.Column:
                    for (int i = 0; i < force.Escorts.Count; i++) slots.Add(new Slot(180f, gap * 1.4f * (i + 1)));
                    Assign(force, force.Escorts, slots);
                    return;
                case Formation.Abreast:
                    for (int i = 0; i < force.Escorts.Count; i++)
                        slots.Add(new Slot(i % 2 == 0 ? 90f : 270f, gap * 1.4f * (i / 2 + 1)));
                    Assign(force, force.Escorts, slots);
                    return;
                case Formation.Box:
                    float[] corners = { 45f, 315f, 135f, 225f };
                    for (int i = 0; i < force.Escorts.Count; i++)
                        slots.Add(new Slot(corners[i % 4], gap * 1.8f * (i / 4 + 1)));
                    Assign(force, force.Escorts, slots);
                    return;
            }

            // Screen: the main body astern, a ring round the guide, pickets
            // ahead -- each role's stations shared out within that role.
            for (int i = 0; i < main.Count; i++)
                slots.Add(new Slot(180f + (i % 2 == 0 ? 1 : -1) * 15f * ((i + 1) / 2), gap * 1.6f * (i / 2 + 1)));
            Assign(force, main, slots);
            slots = new List<Slot>();
            float[] ringBearings = { 45f, 315f, 135f, 225f, 90f, 270f, 0f, 180f };
            for (int i = 0; i < ring.Count; i++)
                slots.Add(new Slot(ringBearings[i % ringBearings.Length], gap * 2.4f * (1f + 0.5f * (i / ringBearings.Length))));
            Assign(force, ring, slots);
            slots = new List<Slot>();
            float arc = Mathf.Max(5f * (force.Guide != null ? force.Guide.maxRadius : 60f), 1500f) * force.Spacing;
            for (int i = 0; i < screen.Count; i++)
            {
                int rank = i / 5, slot = i % 5;
                float offset = (slot - 2) * 17.5f + (rank % 2 == 1 ? 8.75f : 0f);
                slots.Add(new Slot(offset, arc + 600f * rank, threatArc: true));
            }
            Assign(force, screen, slots);
            Report(force, main, ring, screen);
        }

        // What every escort was given, and why: a station that looks wrong in
        // game can be read here against the guide's size and the escort's role.
        private static void Report(TaskForce force, List<Escort> main, List<Escort> ring, List<Escort> screen)
        {
            var line = new System.Text.StringBuilder("[tf] " + force.Name + " · " + force.Formation + " · guide " +
                ShipNames.Of(force.Guide) + " radius " + (force.Guide != null ? force.Guide.maxRadius.ToString("0") : "?") +
                " m · gap " + Gap(force).ToString("0") + " m");
            foreach (Escort escort in force.Escorts)
            {
                string role = main.Contains(escort) ? "main" : ring.Contains(escort) ? "ring" : screen.Contains(escort) ? "picket" : force.Formation.ToString().ToLowerInvariant();
                line.Append("\n    ").Append(ShipNames.Of(escort.Ship)).Append(" [").Append(ShipNames.TypeOf(escort.Ship))
                    .Append("] ").Append(role).Append(" · ").Append(escort.Bearing.ToString("000")).Append("° ")
                    .Append(escort.Range.ToString("0")).Append(" m");
            }
            Plugin.Log.LogInfo(line.ToString());
        }

        private readonly struct Slot
        {
            internal readonly float Bearing, Range;
            internal readonly bool ThreatArc;
            internal Slot(float bearing, float range, bool threatArc = false) { Bearing = bearing; Range = range; ThreatArc = threatArc; }
        }

        // Shares a formation's stations out so the ships travel least in all:
        // each to the one that suits the whole group, not the next in the list.
        // A ship sent past another to the far corner is never the shortest
        // total, so this also keeps their paths from crossing. Exhaustive for a
        // handful of ships, greedy nearest-first beyond that.
        private static void Assign(TaskForce force, List<Escort> group, List<Slot> slots)
        {
            int n = Mathf.Min(group.Count, slots.Count);
            if (n == 0) return;
            var cost = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    Vector3 offset = SlotPosition(force, slots[j]) - group[i].Ship.GlobalPosition();
                    offset.y = 0f;
                    cost[i, j] = offset.sqrMagnitude;
                }

            int[] best = new int[n];
            if (n <= 7)
            {
                int[] order = new int[n];
                for (int i = 0; i < n; i++) order[i] = i;
                float bestCost = float.MaxValue;
                Permute(order, 0, cost, ref bestCost, best);
            }
            else
            {
                var takenShip = new bool[n];
                var takenSlot = new bool[n];
                for (int round = 0; round < n; round++)
                {
                    int bi = -1, bj = -1;
                    float low = float.MaxValue;
                    for (int i = 0; i < n; i++)
                        if (!takenShip[i])
                            for (int j = 0; j < n; j++)
                                if (!takenSlot[j] && cost[i, j] < low) { low = cost[i, j]; bi = i; bj = j; }
                    takenShip[bi] = takenSlot[bj] = true;
                    best[bi] = bj;
                }
            }
            for (int i = 0; i < n; i++)
            {
                Slot slot = slots[best[i]];
                Place(group[i], slot.Bearing, slot.Range);
                group[i].ThreatArc = slot.ThreatArc;
            }
        }

        private static void Permute(int[] order, int k, float[,] cost, ref float bestCost, int[] best)
        {
            int n = order.Length;
            if (k == n)
            {
                float total = 0f;
                for (int i = 0; i < n; i++) total += cost[i, order[i]];
                if (total < bestCost) { bestCost = total; System.Array.Copy(order, best, n); }
                return;
            }
            for (int i = k; i < n; i++)
            {
                (order[k], order[i]) = (order[i], order[k]);
                Permute(order, k + 1, cost, ref bestCost, best);
                (order[k], order[i]) = (order[i], order[k]);
            }
        }

        // Where a station is now, in the world -- the smoothed guide once there
        // is one, the guide itself when the force has only just formed.
        private static GlobalPosition SlotPosition(TaskForce force, Slot slot)
        {
            GlobalPosition centre = force.LastSmooth >= 0f ? force.Centre : force.Guide.GlobalPosition();
            Vector3 course = force.LastSmooth >= 0f ? force.Course
                : new Vector3(force.Guide.transform.forward.x, 0f, force.Guide.transform.forward.z).normalized;
            float reference = slot.ThreatArc ? ThreatBearing(force)
                : force.FixedNorth ? 0f : Mathf.Atan2(course.x, course.z) * Mathf.Rad2Deg;
            float radians = (reference + slot.Bearing) * Mathf.Deg2Rad;
            return centre + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * slot.Range;
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
                if (force.Guide == null) { Disband(force); continue; }
                if (force.Escorts.Count == 0) continue;
                Smooth(force, dt);
                RecordWake(force);
                float worst = 0f;
                foreach (Escort escort in force.Escorts)
                {
                    if (escort.Detached) continue;
                    Keep(force, escort);
                    worst = Mathf.Max(worst, escort.OffStation);
                }
                Pace(force, worst);
                Trace(force);
            }
        }

        private static float nextTrace;

        private static void Trace(TaskForce force)
        {
            if (Time.timeSinceLevelLoad < nextTrace) return;
            nextTrace = Time.timeSinceLevelLoad + 10f;
            foreach (Escort escort in force.Escorts)
            {
                if (escort.Ship == null) continue;
                Plugin.Log.LogInfo("[tf] " + force.Name + " · " + ShipNames.Of(escort.Ship) + " · " + Describe(force, escort) +
                    " · station " + escort.Range.ToString("0") + " m at " + escort.Bearing.ToString("000") + "° · off by " +
                    escort.OffStation.ToString("0") + " m · aim " +
                    FastMath.Distance(escort.LastAim, escort.Ship.GlobalPosition()).ToString("0") + " m ahead · ordered " +
                    escort.LastSpeed.ToString("0.0") + " kt" + (force.UnderFire ? " · guide under fire" : ""));
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
            bool smoothed = force.LastSmooth >= 0f;
            GlobalPosition centre = smoothed ? force.Centre : force.Guide.GlobalPosition();
            Vector3 heading = smoothed ? force.Course : force.Guide.transform.forward;
            float course = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            if (hq == null) return course;
            float best = 60000f;
            float bearing = course;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Ship ship) || ship.disabled || ship.NetworkHQ == null || ship.NetworkHQ == hq) continue;
                if (!hq.TryGetKnownPosition(ship, out GlobalPosition known)) continue;
                Vector3 offset = known - centre;
                offset.y = 0f;
                float range = offset.magnitude;
                if (range >= best) continue;
                best = range;
                bearing = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            }
            return bearing;
        }

        private static void RecordWake(TaskForce force)
        {
            GlobalPosition here = force.Guide.GlobalPosition();
            if (force.Wake.Count == 0 || FastMath.Distance(here, force.Wake[force.Wake.Count - 1]) > 40f) force.Wake.Add(here);
            if (force.Wake.Count > 500) force.Wake.RemoveRange(0, force.Wake.Count - 500);
        }

        private static bool FollowsWake(TaskForce force) => force.Formation == Formation.Column && !force.FixedNorth;

        // The point on the guide's track a given distance astern of it, and the
        // way the track runs there. False while the track is not yet that long.
        internal static bool WakePoint(TaskForce force, float behind, out GlobalPosition point, out Vector3 direction)
        {
            point = force.Guide.GlobalPosition();
            direction = force.Course;
            if (behind <= 0f) return true;
            GlobalPosition newer = point;
            float left = behind;
            for (int i = force.Wake.Count - 1; i >= 0; i--)
            {
                GlobalPosition older = force.Wake[i];
                Vector3 segment = newer - older;
                segment.y = 0f;
                float length = segment.magnitude;
                if (length < 0.01f) { newer = older; continue; }
                if (length >= left)
                {
                    point = newer - segment / length * left;
                    direction = segment / length;
                    return true;
                }
                left -= length;
                newer = older;
            }
            return false;
        }

        internal static GlobalPosition StationOf(TaskForce force, Escort escort)
        {
            if (FollowsWake(force) && WakePoint(force, escort.Range, out GlobalPosition trail, out _)) return trail;
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
            // In a column following the wake, "ahead" is along the track at the
            // station, not the guide's present course.
            Vector3 course = force.Course;
            bool wake = FollowsWake(force) && WakePoint(force, escort.Range, out _, out course);
            if (!wake) course = force.Course;
            escort.Station = station;
            GlobalPosition here = ship.GlobalPosition();
            Vector3 gap = station - here;
            gap.y = 0f;
            escort.OffStation = gap.magnitude;
            float along = Vector3.Dot(gap, course);   // positive: behind its station

            // Speed: the guide's, plus the along-track gap; flat out when well
            // adrift, never so slow it loses steerage.
            float guideKnots = guide.rb != null
                ? Vector3.Dot(guide.rb.velocity, force.Course) / CommandableShip.MetresPerSecondPerKnot : 0f;
            float maximum = CommandableShip.MaximumSpeedKnots(ship);
            // Ahead of its station it slows and lets the station come to it --
            // down to a crawl if well ahead -- rather than turning back.
            float knots = escort.OffStation > 3000f && along > 0f ? maximum
                : Mathf.Clamp(guideKnots + along * 0.004f, 1.5f, maximum);

            // Aim well ahead of the station along the course -- never at it, or
            // the native arrival hold stops the ship there.
            float lead = Mathf.Max(6f * ship.maxRadius, 600f);
            GlobalPosition aim = station + course * lead;
            if (escort.OffStation > lead * 2f) aim = station + course * (lead * 0.5f);
            // A column steers for a point further up the guide's own track.
            if (wake && escort.OffStation <= lead * 2f && WakePoint(force, Mathf.Max(escort.Range - lead, 0f), out GlobalPosition up, out _))
                aim = up;
            // Never aim behind the ship. With its station astern -- after the
            // guide turned, or when it simply started ahead -- turning round to
            // meet it only means turning round again to keep up. Hold the
            // formation's course instead, edging across onto the station's
            // line, and let the speed loop slow it until the station arrives.
            Vector3 toAim = aim - here;
            toAim.y = 0f;
            if (along < lead * 0.5f || Vector3.Dot(toAim, course) < lead * 0.5f)
            {
                Vector3 across = gap - course * along;          // the sideways part of the gap
                across = Vector3.ClampMagnitude(across, lead);
                aim = here + course * lead + across;
            }

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

        // The fastest the force can go and still keep station: nine tenths of
        // its slowest ship's top speed, so every escort has speed in hand.
        internal static float FormationSpeed(TaskForce force, out Ship slowest)
        {
            slowest = force.Guide;
            float lowest = force.Guide != null ? CommandableShip.MaximumSpeedKnots(force.Guide) : 0f;
            foreach (Escort escort in force.Escorts)
            {
                if (escort.Ship == null || escort.Detached) continue;
                float top = CommandableShip.MaximumSpeedKnots(escort.Ship);
                if (top < lowest) { lowest = top; slowest = escort.Ship; }
            }
            return force.Escorts.Count > 0 ? lowest * 0.9f : lowest;
        }

        // The force's speed is the guide's, capped at the formation speed; the
        // order itself is set on the guide.
        internal static void SetSpeed(TaskForce force, float fraction)
        {
            if (force?.Guide == null) return;
            float knots = FormationSpeed(force, out _) * Mathf.Clamp01(fraction);
            issuing = true;
            try { NavigationOrders.SetOrderedSpeedKnots(force.Guide, knots, out _); }
            finally { issuing = false; }
            guideOrderedAt = Time.timeSinceLevelLoad;
            guideOrderedForce = force;
        }

        // The guide never outruns its slowest escort, and slows further for
        // escorts well off station -- except under fire, when getting clear
        // matters more than keeping station.
        private static void Pace(TaskForce force, float worst)
        {
            var route = force.Guide.GetComponent<ShipRoute>();
            if (route == null) return;
            float cap = FormationSpeed(force, out _);
            float threshold = Mathf.Max(6f * force.Guide.maxRadius, 1200f);
            if (!force.UnderFire && worst > threshold)
                cap *= Mathf.Lerp(1f, 0.4f, Mathf.Clamp01((worst - threshold) / 3000f));
            route.SpeedCapKnots = force.UnderFire && force.Escorts.Count == 0 ? float.PositiveInfinity : cap;
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

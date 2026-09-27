using System.Collections.Generic;
using UnityEngine;

namespace NavalPower
{
    internal enum TakeoffVerdict { Unknown, Ok, Marginal, TooHeavy, OverMax }

    internal sealed class TakeoffEstimate
    {
        internal float Gross, MaxWeight, ThrustToWeight;
        internal float Roll, Runway;            // metres; Roll 0 when thrust alone lifts it
        internal bool SkiJump, Deck, Vtol;
        internal TakeoffVerdict Verdict;
        internal string Line;
    }

    // Will it get off this deck with this load?
    //
    // The game's own loadout screen shows gross weight against the airframe's
    // maximum and a thrust-to-weight ratio, and nothing about where it is
    // taking off from. That matters most at sea: the AI's takeoff is a rolling
    // run down whatever runway the field has -- a VTOL airframe with its
    // nozzles part-way down, but still a run -- and a carrier's is a short
    // deck. Too heavy to reach flying speed by the end of it, the aircraft goes
    // into the water. A load that flies off a land runway may not fly off a
    // ship.
    //
    // The estimate: gross weight from the empty airframe, the fuel and the
    // stores; the airframe's own takeoff distance scaled by weight squared
    // (flying speed goes with the root of weight, acceleration against it);
    // shortened by a ski jump, and for a VTOL airframe by the share of its
    // weight its thrust carries. Thrust above weight lifts it off anywhere.
    // It is an estimate, and it says so; the numbers are logged at launch so
    // they can be checked against what actually happens.
    internal static class TakeoffCheck
    {
        private const float Gravity = 9.81f;

        internal static TakeoffEstimate Estimate(LoadoutPlan plan, Airbase field)
        {
            var result = new TakeoffEstimate();
            AircraftDefinition definition = plan?.Definition;
            if (definition == null || definition.aircraftInfo == null || definition.aircraftParameters == null) return result;
            var prefab = definition.unitPrefab != null ? definition.unitPrefab.GetComponent<Aircraft>() : null;
            if (prefab == null) return result;

            result.MaxWeight = definition.aircraftInfo.maxWeight;
            // A tank's fuel is added to its part's mass when it is filled, so a
            // drop tank's mount lists only the empty tank: its fuel goes on too.
            result.Gross = definition.aircraftInfo.emptyWeight +
                (Fuel(prefab) + ExternalFuel(plan)) * Mathf.Clamp01(plan.Fuel) + Stores(prefab, plan);
            float thrust = MaxThrust(prefab);
            result.ThrustToWeight = thrust > 0f && result.Gross > 0f ? thrust / (result.Gross * Gravity) : 0f;
            result.Vtol = definition.aircraftParameters.verticalLanding;
            result.Deck = field != null && field.AttachedAirbase;
            if (field != null && field.runways != null)
                foreach (Airbase.Runway runway in field.runways)
                    if (runway != null && runway.Takeoff && runway.Length > result.Runway)
                    {
                        result.Runway = runway.Length;
                        result.SkiJump = runway.SkiJump;
                    }

            string weight = UnitConverter.WeightReading(result.Gross) + " of " + UnitConverter.WeightReading(result.MaxWeight) + " max";
            string twr = result.ThrustToWeight > 0f ? "  ·  T/W " + result.ThrustToWeight.ToString("0.00") : "";
            if (result.MaxWeight > 0f && result.Gross > result.MaxWeight)
            {
                result.Verdict = TakeoffVerdict.OverMax;
                result.Line = "OVER MAXIMUM WEIGHT  ·  " + weight + twr;
                return result;
            }

            if (result.Vtol && result.ThrustToWeight >= 1.02f)
            {
                result.Verdict = TakeoffVerdict.Ok;
                result.Line = "Takeoff OK  ·  thrust exceeds weight  ·  " + weight + twr;
                return result;
            }

            float reference = definition.aircraftParameters.takeoffDistance;
            if (reference <= 0f || result.Runway <= 0f || result.MaxWeight <= 0f)
            {
                result.Verdict = TakeoffVerdict.Unknown;
                result.Line = weight + twr;
                return result;
            }
            float share = result.Gross / result.MaxWeight;
            float roll = reference * share * share;
            if (result.SkiJump) roll *= 0.7f;
            if (result.Vtol && result.ThrustToWeight > 0f) roll *= Mathf.Max(0.15f, 1f - result.ThrustToWeight);
            result.Roll = roll;

            string where = result.Deck ? "deck" : "runway";
            string run = "run ~" + roll.ToString("0") + " m of " + result.Runway.ToString("0") + " m " + where;
            if (roll <= result.Runway * 0.9f)
            {
                result.Verdict = TakeoffVerdict.Ok;
                result.Line = "Takeoff OK  ·  " + run + "  ·  " + weight + twr;
            }
            else if (roll <= result.Runway * 1.15f)
            {
                result.Verdict = TakeoffVerdict.Marginal;
                result.Line = "MARGINAL  ·  " + run + "  ·  less fuel or fewer stores";
            }
            else
            {
                result.Verdict = TakeoffVerdict.TooHeavy;
                result.Line = "TOO HEAVY FOR THIS " + where.ToUpperInvariant() + "  ·  " + run +
                    (result.Deck ? "  ·  likely to go in the water" : "");
            }
            return result;
        }

        internal static Color ColourOf(TakeoffVerdict verdict) =>
            verdict == TakeoffVerdict.Ok ? Theme.Good
            : verdict == TakeoffVerdict.Marginal ? Theme.Warn
            : verdict == TakeoffVerdict.Unknown ? Theme.TextMuted : Theme.Bad;

        internal static string Trace(TakeoffEstimate e) =>
            "takeoff estimate · gross " + e.Gross.ToString("0") + " kg / max " + e.MaxWeight.ToString("0") +
            " · T/W " + e.ThrustToWeight.ToString("0.00") + (e.Vtol ? " (VTOL)" : "") +
            " · run " + e.Roll.ToString("0") + " m vs " + e.Runway.ToString("0") + " m" +
            (e.SkiJump ? " ski jump" : "") + (e.Deck ? " deck" : "") + " · " + e.Verdict;

        // Fuel aboard against internal capacity. The game's own fuel level is
        // everything aboard over everything's capacity, drop tanks included,
        // so full is 100% with or without them. Internal capacity comes from
        // the airframe's prefab, whose tanks are all internal; external tanks
        // -- vanilla or a mod's, as long as they register as fuel tanks -- are
        // in the aircraft's fuel on board and so push the reading above 100%.
        private static readonly Dictionary<AircraftDefinition, float> internalCapacity =
            new Dictionary<AircraftDefinition, float>();

        internal static float InternalCapacity(AircraftDefinition definition)
        {
            if (definition == null) return 0f;
            if (internalCapacity.TryGetValue(definition, out float known)) return known;
            var prefab = definition.unitPrefab != null ? definition.unitPrefab.GetComponent<Aircraft>() : null;
            float capacity = prefab != null ? Fuel(prefab) : 0f;
            internalCapacity[definition] = capacity;
            return capacity;
        }

        // Fuel the plan's external tanks hold when full, on every hardpoint of
        // their station.
        internal static float ExternalFuel(LoadoutPlan plan)
        {
            if (plan?.Definition == null) return 0f;
            var prefab = plan.Definition.unitPrefab != null ? plan.Definition.unitPrefab.GetComponent<Aircraft>() : null;
            HardpointSet[] sets = prefab != null && prefab.weaponManager != null ? prefab.weaponManager.hardpointSets : null;
            float capacity = 0f;
            foreach (LoadoutStation station in plan.Stations)
            {
                if (station.Selected?.prefab == null) continue;
                float tank = 0f;
                foreach (FuelTank fuel in station.Selected.prefab.GetComponentsInChildren<FuelTank>(true)) tank += fuel.GetCapacity();
                if (tank <= 0f) continue;
                int points = sets != null && station.Index >= 0 && station.Index < sets.Length && sets[station.Index]?.hardpoints != null
                    ? Mathf.Max(1, sets[station.Index].hardpoints.Count) : 1;
                capacity += tank * points;
            }
            return capacity;
        }

        internal static float FuelPercent(Aircraft aircraft)
        {
            float capacity = InternalCapacity(aircraft.definition);
            if (capacity <= 0f) return Mathf.Clamp01(aircraft.GetFuelLevel()) * 100f;
            return aircraft.GetFuelQuantity() / capacity * 100f;
        }

        private static float Fuel(Aircraft prefab)
        {
            float capacity = 0f;
            foreach (FuelTank tank in prefab.GetComponentsInChildren<FuelTank>(true)) capacity += tank.GetCapacity();
            return capacity;
        }

        // Each station's mount, loaded, on every hardpoint of the set.
        private static float Stores(Aircraft prefab, LoadoutPlan plan)
        {
            HardpointSet[] sets = prefab.weaponManager != null ? prefab.weaponManager.hardpointSets : null;
            if (sets == null) return 0f;
            float mass = 0f;
            foreach (LoadoutStation station in plan.Stations)
            {
                if (station.Selected == null || station.Index < 0 || station.Index >= sets.Length) continue;
                int points = sets[station.Index]?.hardpoints != null ? Mathf.Max(1, sets[station.Index].hardpoints.Count) : 1;
                mass += Mathf.Max(station.Selected.mass, station.Selected.emptyMass) * points;
            }
            return mass;
        }

        private static float MaxThrust(Aircraft prefab)
        {
            var ducted = prefab.GetComponent<DuctedThrustSystem>();
            if (ducted != null) return ducted.GetMaxThrust();
            float thrust = 0f;
            foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is IThrustSource source) thrust += source.GetMaxThrust();
            return thrust;
        }
    }
}

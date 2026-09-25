using System.Collections.Generic;

namespace NavalPower
{
    // Every flight gets a callsign, so a list of six Vortexes is six things you
    // can tell apart and say out loud.
    //
    // A callsign is a word for the airframe type and a number: the first type
    // launched this session gets the first word, the next type the next, and
    // the number is the lowest one not already flying or waiting on a deck.
    // Numbers are reused once a flight is gone, the way a squadron reuses them.
    internal static class Callsigns
    {
        private static readonly string[] Words =
        {
            "Viper", "Raven", "Cobra", "Hawk", "Falcon", "Talon", "Spectre", "Lancer",
            "Reaper", "Ghost", "Jester", "Havoc", "Mako", "Nomad", "Shadow", "Titan"
        };

        private static readonly Dictionary<AircraftDefinition, string> wordFor =
            new Dictionary<AircraftDefinition, string>();

        internal static string Suggest(AircraftDefinition definition)
        {
            string word = WordFor(definition);
            var taken = new HashSet<string>();
            foreach (string label in FlightOrders.LabelsInUse()) taken.Add(label);
            for (int n = 1; n < 100; n++)
            {
                string candidate = word + " " + n;
                if (!taken.Contains(candidate)) return candidate;
            }
            return word;
        }

        private static string WordFor(AircraftDefinition definition)
        {
            if (definition == null) return "Flight";
            if (wordFor.TryGetValue(definition, out string word)) return word;
            word = Words[wordFor.Count % Words.Length];
            wordFor[definition] = word;
            return word;
        }

        // The game's own name for the aircraft, written the way it writes a
        // player's: callsign first, airframe in brackets. The hover card, the
        // kill feed and the target lists all read this, so a flight is named
        // wherever the game names anything.
        internal static void Apply(Aircraft aircraft, string label)
        {
            if (aircraft == null || string.IsNullOrEmpty(label)) return;
            string name = label + " [" + (aircraft.definition?.unitName ?? aircraft.name) + "]";
            aircraft.NetworkunitName = name;
            if (UnitRegistry.TryGetPersistentUnit(aircraft.persistentID, out PersistentUnit persistent) && persistent != null)
                persistent.unitName = name;
        }
    }
}

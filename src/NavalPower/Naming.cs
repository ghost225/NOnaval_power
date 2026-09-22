using System.Collections.Generic;
using System.Text;

namespace NavalPower
{
    // Turns component names into something legible on any hull. Ships are
    // authored with names like "CIWS_FL", "SAM_Radar1" or "Hull_CRAft", and a
    // per-ship lookup table cannot cover modded hulls, so this works from the
    // naming conventions instead.
    internal static class Naming
    {
        // Tokens that carry no meaning for a reader.
        private static readonly HashSet<string> Noise = new HashSet<string>
        {
            "lod0", "lod1", "lod2", "lod", "mesh", "geo", "geometry", "collider", "collision",
            "info", "prefab", "clone", "obj", "go", "root", "new", "dummy", "placeholder"
        };

        // Expansions that read better spelled out, and acronyms to leave alone.
        private static readonly Dictionary<string, string> Words = new Dictionary<string, string>
        {
            { "ciws", "CIWS" }, { "sam", "SAM" }, { "aam", "AAM" }, { "agm", "AGM" }, { "ram", "RAM" },
            { "vls", "VLS" }, { "pd", "Point Defence" }, { "irst", "IRST" }, { "eo", "Electro-Optical" },
            { "esm", "ESM" }, { "ecm", "ECM" }, { "rwr", "RWR" }, { "ir", "Infrared" }, { "fc", "Fire Control" },
            { "nav", "Navigation" }, { "srch", "Search" }, { "trk", "Tracking" }, { "gun", "Gun" },
            { "mg", "Machine Gun" }, { "aa", "Anti-Air" }, { "asw", "Anti-Submarine" },
            { "fwd", "Forward" }, { "aft", "Aft" }, { "stbd", "Starboard" }, { "prt", "Port" },
            { "hull", "Hull" }, { "gen", "Generator" }, { "eng", "Engine" }, { "rad", "Radar" },
            { "mag", "Magazine" }, { "amm", "Ammunition" }, { "htr", "Hangar" }
        };

        internal static string Pretty(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Unnamed";
            string name = raw.Replace("(Clone)", " ");

            var output = new List<string>();
            foreach (string piece in Split(name))
            {
                string lower = piece.ToLowerInvariant();
                if (Noise.Contains(lower)) continue;
                string position = Position(lower);
                if (position != null) { output.Add(position); continue; }
                if (Words.TryGetValue(lower, out string word)) { output.Add(word); continue; }
                output.Add(Capitalise(piece));
            }
            if (output.Count == 0) return Capitalise(raw);

            // Positions read better leading: "Forward Port CIWS", not "CIWS Forward Port".
            var leading = new List<string>();
            var rest = new List<string>();
            foreach (string word in output)
                (IsPosition(word) ? leading : rest).Add(word);
            leading.AddRange(rest);
            return string.Join(" ", leading.ToArray());
        }

        private static bool IsPosition(string word) =>
            word == "Forward" || word == "Aft" || word == "Port" || word == "Starboard" ||
            word == "Upper" || word == "Lower" || word == "Centre";

        // Hulls commonly encode position as a short code. Two letters are
        // fore/aft then port/starboard ("FL"); a lone letter is fore/aft, since
        // a single "R" means rear far more often than right in these names.
        private static string Position(string token)
        {
            switch (token)
            {
                case "f": case "fore": case "front": case "forward": return "Forward";
                case "r": case "rear": case "a": case "aft": return "Aft";
                case "l": case "port": return "Port";
                case "s": case "starboard": return "Starboard";
                case "c": case "centre": case "center": case "mid": case "midship": return "Centre";
                case "u": case "upper": case "top": return "Upper";
                case "d": case "lower": case "bottom": return "Lower";
                case "fl": return "Forward Port";
                case "fr": return "Forward Starboard";
                case "rl": case "al": return "Aft Port";
                case "rr": case "ar": return "Aft Starboard";
                case "cf": return "Forward Centre";
                case "cr": return "Aft Centre";
                default: return null;
            }
        }

        // Split on separators, camelCase boundaries, and letter/digit changes.
        private static IEnumerable<string> Split(string name)
        {
            var piece = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_' || c == '-' || c == '.' || c == ' ')
                {
                    if (piece.Length > 0) { yield return piece.ToString(); piece.Clear(); }
                    continue;
                }
                if (piece.Length > 0)
                {
                    char previous = piece[piece.Length - 1];
                    bool boundary =
                        (char.IsUpper(c) && char.IsLower(previous)) ||
                        (char.IsDigit(c) != char.IsDigit(previous)) ||
                        // "SAMRadar" -> "SAM" + "Radar"
                        (char.IsUpper(c) && char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1]));
                    if (boundary) { yield return piece.ToString(); piece.Clear(); }
                }
                piece.Append(c);
            }
            if (piece.Length > 0) yield return piece.ToString();
        }

        private static string Capitalise(string word)
        {
            if (string.IsNullOrEmpty(word)) return word;
            // Leave existing acronyms and numbers as authored.
            if (word.Length <= 4 && word.ToUpperInvariant() == word) return word;
            return char.ToUpperInvariant(word[0]) + word.Substring(1);
        }
    }
}

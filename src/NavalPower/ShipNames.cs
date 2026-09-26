using System.Collections.Generic;
using UnityEngine;

namespace NavalPower
{
    // Every ship gets a name, the way every flight gets a callsign: a fleet of
    // four "Destroyer"s is four things you cannot tell apart or talk about.
    //
    // Names come from a registry per side -- Anglo and European for Boscali;
    // for Primeva, Greek and Near-Eastern myth after the faction's own
    // vehicles -- behind a service prefix, BMDF or PALN.
    // Unarmed hulls are merchants and sail as MV. A ship's pick is keyed on the
    // mission and the ship's own unique name, so reloading the same mission
    // names it the same again, and a rename is remembered for that mission.
    //
    // Only a ship still wearing its type as its name is named: one a mission
    // or another mod has already named keeps that.
    internal static class ShipNames
    {
        private static readonly string[] Boscali =
        {
            "Valiant", "Intrepid", "Dauntless", "Vigilant", "Sovereign", "Albion", "Ardent", "Audacious",
            "Brilliant", "Courageous", "Defiant", "Endeavour", "Formidable", "Glorious", "Illustrious",
            "Invincible", "Implacable", "Indomitable", "Tenacious", "Unyielding", "Steadfast", "Relentless",
            "Nelson", "Drake", "Rodney", "Ruyter", "Tromp", "Colbert", "Suffren", "Jervis", "Hawke",
            "Aquitaine", "Lorraine", "Bretagne", "Normandie", "Flandre", "Holstein", "Bremen", "Lubeck",
            "Zeeland", "Utrecht", "Cornwall", "Kent", "Northumberland", "Somerset", "Argyll", "Monmouth",
            "Iron Duke", "Warspite", "Agincourt", "Trafalgar", "Camperdown", "Quiberon", "Lepanto",
            "Minerva", "Juno", "Arethusa", "Galatea", "Penelope", "Sirius", "Orion", "Achates", "Onslow"
        };

        // After the faction's own vehicles -- Ifrit, Ibis, Alkyon, Hyperion,
        // Medusa: Greek myth and nature in Greek spelling (Alkyon, not
        // Halcyon), blended with Arabic and Egyptian myth, leaning to creatures
        // and birds. Titans and gods, beasts, birds, and the weather.
        private static readonly string[] Primeva =
        {
            "Kronos", "Koios", "Krios", "Iapetos", "Theia", "Themis", "Tethys", "Okeanos", "Helios",
            "Selene", "Eos", "Astraios", "Pallas", "Perses", "Atlas", "Prometheus", "Nyx", "Erebos",
            "Typhon", "Ladon", "Skylla", "Charybdis", "Talos", "Gorgon", "Harpyia", "Kerberos", "Kentauros",
            "Pegasos", "Triton", "Nereus", "Proteus", "Marid", "Anqa", "Rukh", "Simurgh", "Buraq", "Jinn",
            "Aetos", "Kyknos", "Pelargos", "Glaux", "Hierax", "Korax", "Saqr", "Hudhud", "Bennu", "Shahin",
            "Barq", "Raad", "Asifa", "Shihab", "Najm", "Suhail", "Thurayya", "Qamar", "Hilal", "Zephyros",
            "Boreas", "Notos", "Euros", "Aigis", "Keraunos", "Astrape", "Thyella"
        };

        private static readonly string[] BoscaliMerchant =
        {
            "Northern Star", "Westmark", "Channel Pride", "Baltic Venture", "Northumbria", "Rhine Spirit",
            "Solent Trader", "Iberian Dawn", "Hanover Star", "Albion Trader", "Flanders Grace", "Mersey",
            "Clyde Venture", "Hansa Pride", "Atlantic Reach", "Dover Light"
        };

        private static readonly string[] PrimevaMerchant =
        {
            "Aigaion", "Nour", "Kalypso", "Thalassa", "Galini", "Al-Fajr", "Zahra", "Amphitrite", "Ionia",
            "Nefeli", "Yasmin", "Ourania", "Layla", "Kymothoe", "Marjan", "Halcyone"
        };

        // Names the game or a mod already gives a vehicle or class: a ship
        // called Ifrit beside an Ifrit fighter is just confusing.
        private static readonly HashSet<string> Reserved = new HashSet<string>
        {
            "Ifrit", "Ibis", "Alkyon", "Hyperion", "Medusa", "Annex", "Dynamo", "Shard", "Argus", "Chicane",
            "Darkreach", "Compass", "Revoker", "Vortex", "Tarantula", "Cricket", "Anvil", "Resolute",
            "Chimera", "Horus", "Boltstrike", "Linebreaker", "Spearhead"
        };

        private sealed class Entry
        {
            internal string Prefix;
            internal string Name;
            internal string Pool;
        }

        private static readonly Dictionary<Ship, Entry> named = new Dictionary<Ship, Entry>();
        private static readonly HashSet<Ship> foreign = new HashSet<Ship>();
        private static float nextSweep;

        // "BMDF Valiant"; a ship not (yet) named reads as its type.
        internal static string Of(Ship ship)
        {
            if (ship == null) return "";
            return named.TryGetValue(ship, out Entry entry) ? Full(entry) : (ship.definition?.unitName ?? ship.name);
        }

        // Any unit's display name: a named ship's, otherwise its type.
        internal static string Of(Unit unit) =>
            unit is Ship ship ? Of(ship) : unit != null ? (unit.definition?.unitName ?? unit.name) : "";

        internal static string TypeOf(Unit unit) => unit?.definition?.unitName ?? "";

        internal static bool IsNamed(Ship ship) => ship != null && named.ContainsKey(ship);

        private static string Full(Entry entry) =>
            string.IsNullOrEmpty(entry.Prefix) ? entry.Name : entry.Prefix + " " + entry.Name;

        internal static void Tick()
        {
            if (Time.unscaledTime < nextSweep) return;
            nextSweep = Time.unscaledTime + 1f;

            // A mission reload leaves the old ships behind as destroyed objects.
            var gone = new List<Ship>();
            foreach (Ship ship in named.Keys) if (ship == null) gone.Add(ship);
            foreach (Ship ship in gone) named.Remove(ship);
            foreign.RemoveWhere(ship => ship == null);

            // In a stable order, so first picks do not depend on spawn order.
            var fresh = new List<Ship>();
            foreach (Unit unit in UnitRegistry.allUnits)
                if (unit is Ship ship && !ship.disabled && !named.ContainsKey(ship) && !foreign.Contains(ship)) fresh.Add(ship);
            fresh.Sort((a, b) => string.CompareOrdinal(a.UniqueName ?? "", b.UniqueName ?? ""));
            foreach (Ship ship in fresh) Name(ship);
        }

        private static void Name(Ship ship)
        {
            if (ship.definition == null) return;
            if (!string.IsNullOrEmpty(ship.unitName) && ship.unitName != ship.definition.unitName)
            {
                foreign.Add(ship);                  // someone else named it
                return;
            }
            bool merchant = IsMerchant(ship);
            string faction = ship.NetworkHQ != null && ship.NetworkHQ.faction != null ? ship.NetworkHQ.faction.factionName : "";
            var entry = new Entry { Prefix = PrefixFor(faction, merchant) };
            string[] pool = PoolFor(faction, merchant, out entry.Pool);
            entry.Name = Saved(ship) ?? Pick(ship, pool, entry.Pool);
            named[ship] = entry;
            Apply(ship, entry);
        }

        // Nothing aboard scores against anything: a merchant.
        private static bool IsMerchant(Ship ship)
        {
            RoleIdentity role = ship.definition.roleIdentity;
            return role.antiAir + role.antiSurface + role.antiMissile + role.antiRadar <= 0.01f;
        }

        private static string PrefixFor(string faction, bool merchant)
        {
            if (merchant) return "MV";
            if (faction == FactionHelper.Boscali) return "BMDF";
            if (faction == FactionHelper.Primeva) return "PALN";
            if (string.IsNullOrEmpty(faction) || faction == "Neutral") return "";
            return faction.Substring(0, Mathf.Min(3, faction.Length)).ToUpperInvariant() + "N";
        }

        private static string[] PoolFor(string faction, bool merchant, out string key)
        {
            bool primeva = faction == FactionHelper.Primeva;
            key = (primeva ? "primeva" : "boscali") + (merchant ? "-mv" : "");
            if (merchant) return primeva ? PrimevaMerchant : BoscaliMerchant;
            return primeva ? Primeva : Boscali;
        }

        // Stable: the same mission and the same ship start the search at the
        // same place, and step past names already afloat.
        private static string Pick(Ship ship, string[] pool, string poolKey)
        {
            var taken = new HashSet<string>();
            foreach (Entry entry in named.Values) if (entry.Pool == poolKey) taken.Add(entry.Name);
            int start = (int)(Hash(MissionName() + "/" + (ship.UniqueName ?? ship.name)) % (uint)pool.Length);
            for (int i = 0; i < pool.Length; i++)
            {
                string candidate = pool[(start + i) % pool.Length];
                if (!taken.Contains(candidate) && !Reserved.Contains(candidate)) return candidate;
            }
            // More ships than names: the second of the name.
            for (int n = 2; ; n++)
            {
                string candidate = pool[start] + " " + Roman(n);
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        internal static bool Rename(Ship ship, string name, out string reason)
        {
            reason = null;
            name = (name ?? "").Trim();
            if (ship == null || name.Length == 0) return false;
            if (!named.TryGetValue(ship, out Entry entry)) { reason = "This ship keeps the name it was given."; return false; }
            // Typing the prefix as well is taken as meaning just the name.
            if (!string.IsNullOrEmpty(entry.Prefix) && name.StartsWith(entry.Prefix + " "))
                name = name.Substring(entry.Prefix.Length + 1).Trim();
            if (name.Length == 0) return false;
            entry.Name = name;
            Apply(ship, entry);
            try { PlayerPrefs.SetString(Key(ship), name); PlayerPrefs.Save(); }
            catch (System.Exception ex) { Plugin.Log.LogWarning("[ships] could not remember the name: " + ex.Message); }
            return true;
        }

        // The game's own name for it, as the hover card and kill feed read it.
        private static void Apply(Ship ship, Entry entry)
        {
            string label = Full(entry) + " [" + ship.definition.unitName + "]";
            ship.NetworkunitName = label;
            if (UnitRegistry.TryGetPersistentUnit(ship.persistentID, out PersistentUnit persistent) && persistent != null)
                persistent.unitName = label;
        }

        private static string Saved(Ship ship)
        {
            try
            {
                string saved = PlayerPrefs.GetString(Key(ship), "");
                return saved.Length > 0 ? saved : null;
            }
            catch { return null; }
        }

        private static string Key(Ship ship) => "NavalPower.ship." + MissionName() + "." + (ship.UniqueName ?? ship.name);

        private static string MissionName() => MissionManager.CurrentMission?.Name ?? "mission";

        // FNV-1a: string.GetHashCode is not promised to be the same run to run.
        private static uint Hash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text) { hash ^= c; hash *= 16777619; }
            return hash;
        }

        private static string Roman(int n)
        {
            string[] numerals = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return n < numerals.Length ? numerals[n] : n.ToString();
        }
    }
}

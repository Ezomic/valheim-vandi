using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// Which boss owns which biome, and what a boss is called in a global key.
    ///
    /// A boss is named by its defeat key - defeated_eikthyr, defeated_gdking - rather than by
    /// its prefab. The key is what the game writes when the boss dies, what Utangard gates on
    /// and what Vaettir's jib reads, so naming the same thing the same way is what keeps three
    /// mods from telling one player three different stories about the same evening.
    ///
    /// Parsed once and rebuilt when the config changes, because the map is a string somebody
    /// can edit while the game is running.
    /// </summary>
    internal static class Bosses
    {
        /// <summary>biome -> the boss keys whose kills raise that biome's stars.</summary>
        private static Dictionary<Heightmap.Biome, List<string>> _byBiome;

        private static string _parsedFrom;

        /// <summary>
        /// The boss keys that own this biome, or null when nothing does.
        ///
        /// A list rather than one key because the map is written boss:biome and a biome could
        /// be given two owners by an edit. Nothing in the default does that; the code reads
        /// whatever is there rather than assuming the default is what is in the file.
        /// </summary>
        internal static List<string> For(Heightmap.Biome biome)
        {
            Parse();

            List<string> keys;
            return _byBiome.TryGetValue(biome, out keys) ? keys : null;
        }

        /// <summary>
        /// Whether this defeat key belongs to a boss this mod is about.
        ///
        /// A creature carrying a defeat key is not a boss: a Bat writes KilledBat, a Troll
        /// KilledTroll, a Surtling killed_surtling, and Hildir's three have m_boss false. The
        /// only test that means anything here is the one the config already answers - is it a
        /// key somebody has mapped to a biome - so that is the test.
        /// </summary>
        internal static bool IsBossKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            Parse();

            foreach (KeyValuePair<Heightmap.Biome, List<string>> pair in _byBiome)
                if (pair.Value.Contains(key)) return true;

            return false;
        }

        /// <summary>The defeat key a creature writes when it dies, or null for anything else.</summary>
        internal static string KeyOf(Character character)
        {
            if (character == null) return null;

            string key = character.m_defeatSetGlobalKey;

            return string.IsNullOrEmpty(key) ? null : key.ToLowerInvariant();
        }

        /// <summary>The defeat key on a prefab, for an altar deciding what it is about to spawn.</summary>
        internal static string KeyOf(GameObject prefab)
        {
            if (prefab == null) return null;

            Character character = prefab.GetComponent<Character>();

            return character == null ? null : KeyOf(character);
        }

        /// <summary>
        /// Every biome a boss can own, in the order the game walks them. The `vandi` console
        /// command and the compendium page both list bosses by it, so the two read the same way.
        /// </summary>
        internal static readonly Heightmap.Biome[] Order =
        {
            Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp,
            Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Mistlands,
            Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean,
        };

        /// <summary>One boss Vandi counts, and every biome it owns, in progression order.</summary>
        internal sealed class Boss
        {
            internal string Key;
            internal List<Heightmap.Biome> Biomes = new List<Heightmap.Biome>();
        }

        /// <summary>
        /// The bosses BossBiomes names, each once, in the order of the first biome it owns. A boss
        /// that owns two, as Bonemass and Fader do by default, is one entry with two biomes, so
        /// the page lists the fight once and says where its kills count.
        /// </summary>
        internal static List<Boss> Roster()
        {
            Parse();

            List<Boss> roster = new List<Boss>();
            Dictionary<string, Boss> byKey = new Dictionary<string, Boss>();

            List<Heightmap.Biome> biomes = new List<Heightmap.Biome>(Order);
            foreach (Heightmap.Biome biome in _byBiome.Keys)
                if (!biomes.Contains(biome)) biomes.Add(biome);

            foreach (Heightmap.Biome biome in biomes)
            {
                List<string> keys;
                if (!_byBiome.TryGetValue(biome, out keys)) continue;

                foreach (string key in keys)
                {
                    Boss boss;
                    if (!byKey.TryGetValue(key, out boss))
                    {
                        boss = new Boss { Key = key };
                        byKey[key] = boss;
                        roster.Add(boss);
                    }

                    boss.Biomes.Add(biome);
                }
            }

            return roster;
        }

        private static ZNetScene _namesFor;
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>();

        /// <summary>
        /// What the player's own game calls the boss behind a defeat key: "The Elder" for
        /// defeated_gdking. Read off the creature prefab that sets the key, preferring one the game
        /// marks m_boss, since a bat sets a key too. The key itself when nothing in this world
        /// sets it, so a mistyped BossBiomes entry shows as exactly what was typed.
        /// </summary>
        internal static string NameOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";

            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return key;

            if (!ReferenceEquals(scene, _namesFor))
            {
                _namesFor = scene;
                Names.Clear();

                Dictionary<string, Character> chosen = new Dictionary<string, Character>();
                foreach (GameObject prefab in scene.m_prefabs)
                {
                    if (prefab == null) continue;

                    Character character;
                    if (!prefab.TryGetComponent(out character)) continue;

                    string defeat = KeyOf(character);
                    if (defeat == null) continue;

                    Character held;
                    if (!chosen.TryGetValue(defeat, out held) || (character.m_boss && !held.m_boss))
                        chosen[defeat] = character;
                }

                foreach (KeyValuePair<string, Character> pair in chosen)
                {
                    string token = pair.Value.m_name;
                    string text = string.IsNullOrEmpty(token) || Localization.instance == null
                        ? null
                        : Localization.instance.Localize(token);

                    if (!string.IsNullOrEmpty(text)) Names[pair.Key] = text;
                }
            }

            string name;
            return Names.TryGetValue(key, out name) ? name : key;
        }

        private static void Parse()
        {
            string spec = VandiConfig.BossBiomes.Value ?? "";
            if (_byBiome != null && spec == _parsedFrom) return;

            _parsedFrom = spec;
            _byBiome = new Dictionary<Heightmap.Biome, List<string>>();

            foreach (string entry in spec.Split(','))
            {
                string[] parts = entry.Split(':');
                if (parts.Length != 2) continue;

                string key = parts[0].Trim().ToLowerInvariant();
                string biomeName = parts[1].Trim();
                if (key.Length == 0 || biomeName.Length == 0) continue;

                Heightmap.Biome biome;
                try
                {
                    // Enum.Parse rather than a table of names: the biome names in the config
                    // are the game's own, so a biome added by an update needs no code here.
                    biome = (Heightmap.Biome)Enum.Parse(typeof(Heightmap.Biome), biomeName, true);
                }
                catch (Exception)
                {
                    VandiPlugin.LogOnce("BossBiomes names a biome this game does not have: '"
                        + biomeName + "'. That pair is skipped; the rest of the line still "
                        + "counts.");
                    continue;
                }

                List<string> keys;
                if (!_byBiome.TryGetValue(biome, out keys))
                {
                    keys = new List<string>();
                    _byBiome[biome] = keys;
                }

                if (!keys.Contains(key)) keys.Add(key);
            }
        }
    }
}

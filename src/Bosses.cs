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

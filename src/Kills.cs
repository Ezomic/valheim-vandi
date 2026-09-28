using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// How many times each player has killed each boss, kept in the world's global keys.
    ///
    /// Global keys rather than anything on the character, and the reason is the rule this mod
    /// is built on: credit goes to the player who made the offering even when they are dead,
    /// offline, or on the other side of the map when the boss falls. The machine that records
    /// the kill is whichever client owned the boss, so the record has to live somewhere that
    /// machine can write and every other machine can read. That is the world, and in Valheim
    /// the world's own note paper is the global key list.
    ///
    /// The layout is Utangard's, deliberately: <c>vandi_p_&lt;playerid&gt;_&lt;bosskey&gt;</c>
    /// with the count as the value, one key per player per boss, rewritten in place rather
    /// than accumulating. A key that does not parse as a member of GlobalKeys is a
    /// NonServerOption, so none of this can reach m_startingGlobalKeys or disturb the world's
    /// own rates.
    ///
    /// Written only on change, and the check is load-bearing rather than tidy: the server's
    /// RPC_SetGlobalKey ends in SendGlobalKeys(Everybody), so accepting one key rebroadcasts
    /// the world's whole key list to everybody connected. Capped at five kills' worth of
    /// boost, a player's count moves at most five times per boss, ever.
    /// </summary>
    internal static class Kills
    {
        private const string Prefix = "vandi_p_";

        /// <summary>How many times this player has killed that boss, as the world remembers.</summary>
        internal static int Count(long playerId, string bossKey)
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null || string.IsNullOrEmpty(bossKey)) return 0;

            string value;
            if (!zone.GetGlobalKey(Key(playerId, bossKey), out value)) return 0;

            int count;

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count)
                ? (count < 0 ? 0 : count)
                : 0;
        }

        /// <summary>The local player's own count, or zero before there is a local player.</summary>
        internal static int LocalCount(string bossKey)
        {
            Player player = Player.m_localPlayer;

            return player == null ? 0 : Count(player.GetPlayerID(), bossKey);
        }

        /// <summary>
        /// Records one more kill for that player, and answers what the count became.
        ///
        /// Anybody may write a global key - RPC_SetGlobalKey has no permission check - so this
        /// is not a privilege, it is a publication. It is called on one machine only, the one
        /// that owned the boss when it died, because a boss with a death animation reaches
        /// OnDeath on every client that is animating it. Ownership is read by Summon.Dying
        /// before OnDeath runs, since the owner's ZDO is gone by the time Summon.Died does.
        /// </summary>
        internal static int Add(long playerId, string bossKey)
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null || string.IsNullOrEmpty(bossKey)) return 0;

            int next = Count(playerId, bossKey) + 1;

            zone.SetGlobalKey(Key(playerId, bossKey) + " " + next.ToString(CultureInfo.InvariantCulture));

            return next;
        }

        /// <summary>
        /// Lowercased, because the game lowercases a key on its way in and a read that did not
        /// would answer for a key it had just written itself.
        /// </summary>
        private static string Key(long playerId, string bossKey)
        {
            return (Prefix + playerId + "_" + bossKey).ToLowerInvariant();
        }

        /// <summary>
        /// `vandi` in the console: the boss kills this world has credited to you, one line per boss
        /// Vandi counts, then the star roll where you stand and how much of it is Vandi's.
        ///
        /// Written for paired-kill-credit-killer.txt in Utangard's scenarios (LHM-36), which has to
        /// say the killer's record went up by exactly the kills it made and not by one more. The
        /// record is a global key, and a scenario can compare a key's value but not read it, while
        /// a character that has played the world already has some number there. So the only honest
        /// check is before and after, and that needs the number printed.
        ///
        /// isCheat false: it reads the world's keys, which every client already holds, and the
        /// star roll every spawn already asks for, and changes nothing.
        /// </summary>
        [HarmonyPatch]
        internal static class Readout
        {
            /// <summary>Every biome BossBiomes could name, walked in progression order.</summary>
            private static readonly Heightmap.Biome[] Biomes =
            {
                Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp,
                Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Mistlands,
                Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean,
            };

            /// <summary>
            /// Process-wide: Terminal's command table is a private static nothing clears, so a
            /// second registration would be a duplicate that outlives the world.
            /// </summary>
            private static bool _registered;

            [HarmonyPostfix]
            [HarmonyPatch(typeof(Terminal), "InitTerminal")]
            private static void Register()
            {
                if (_registered) return;
                _registered = true;

                new Terminal.ConsoleCommand("vandi",
                    "the boss kills this world has credited to you, per boss Vandi counts, and the star roll where you stand",
                    new Terminal.ConsoleEvent(OnCommand), isCheat: false);
            }

            private static void OnCommand(Terminal.ConsoleEventArgs args)
            {
                Terminal term = args.Context;
                if (term == null) return;

                Player player = Player.m_localPlayer;
                if (player == null || ZoneSystem.instance == null)
                {
                    term.AddString("vandi: no character in a world yet");
                    return;
                }

                long id = player.GetPlayerID();

                term.AddString("vandi: " + player.GetPlayerName() + ", id "
                    + id.ToString(CultureInfo.InvariantCulture)
                    + ", enabled=" + (VandiConfig.Enabled.Value ? "yes" : "no")
                    + "   (kills of a boss you summoned at an altar, as this world has recorded them)");

                // A boss may own two biomes, as Bonemass and Fader do by default, and is listed once.
                HashSet<string> shown = new HashSet<string>();

                foreach (Heightmap.Biome biome in Biomes)
                {
                    List<string> keys = Bosses.For(biome);
                    if (keys == null) continue;

                    foreach (string key in keys)
                    {
                        if (!shown.Add(key)) continue;

                        term.AddString("vandi " + key + "="
                            + Count(id, key).ToString(CultureInfo.InvariantCulture));
                    }
                }

                // Last, so the kill lines Utangard's scenario reads keep the place they had.
                term.AddString(StarLine(player));
            }

            /// <summary>
            /// The star roll where you stand, and how much of it is Vandi's.
            ///
            /// Written for vandi-stars-per-biome on 2026-09-28, for the same reason as the kill
            /// lines. That scenario stated the roll outright, 10 with no kills and 15 with one,
            /// which holds only where the ground does not multiply it, and in 1.0 an alt-biome
            /// sector can. Robbin's suite run left him four kilometres out in a Meadows stretch
            /// where the game's own roll was 20, and every reading came out ten high while the mod
            /// added exactly what it should. So the scenario reads this line first and checks how
            /// it moves.
            ///
            /// The total is SpawnSystem.GetLevelUpChance itself, with this mod's postfix on it, so
            /// it is the number a spawn here would roll against. Vandi's part is Stars.Share, the
            /// method the postfix adds, and the sector factor is printed because it is the part
            /// of "the game's" number nobody expects.
            /// </summary>
            private static string StarLine(Player player)
            {
                WorldGenerator world = WorldGenerator.instance;
                if (world == null) return "vandi starchance: no world generator yet";

                Vector3 here = player.transform.position;
                float total = SpawnSystem.GetLevelUpChance(here);
                float share = Stars.Share(here);
                float sector = world.GetBiomeSector(here).GetLevelUpChanceMultiplier();

                return "vandi starchance=" + Number(total) + " here (" + world.GetBiome(here)
                    + ", sector x" + Number(sector) + "): the game's " + Number(total - share)
                    + ", Vandi's +" + Number(share);
            }

            private static string Number(float value)
            {
                return value.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }
    }
}

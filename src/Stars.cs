using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// The star roll, raised in the biomes whose bosses the local player has killed.
    ///
    /// One patch on one method, because the game already funnels every star roll through it:
    /// SpawnSystem rolls each creature it places against
    /// <c>SpawnSystem.GetLevelUpChance(position, creature)</c>, and CreatureSpawner - the
    /// thing that fills camps, crypts and every other placed spawner - calls the same static
    /// with its own override. Patching the roll rather than the callers is what makes this
    /// cover content this mod has never heard of.
    ///
    /// It runs on whichever client owns the zone the creature spawns in, which is what makes
    /// "personal" work at all: that client asks its own player's record. Two people standing
    /// in the same forest see one answer, the owner's, and that is a property of how Valheim
    /// spawns rather than a decision made here. A player alone in a biome always gets their
    /// own.
    ///
    /// The biome comes from the world generator rather than the loaded heightmap, so it
    /// answers for a position no terrain has been built at yet. Dungeon interiors sit at
    /// y &gt; 3000 directly above their entrance and biome lookups compare x and z only, so a
    /// crypt is its surface biome for free - which is the answer this mod wants.
    /// </summary>
    [HarmonyPatch(typeof(SpawnSystem))]
    internal static class Stars
    {
        /// <summary>
        /// Adds the earned points to whatever the game has already worked out, after all of its
        /// own factors, and scales them by none of those factors. The game's roll is 10, or a
        /// spawner's own override, times <c>Game.m_enemyLevelUpRate</c> (the world setting, below
        /// 1 on a world set to fewer stars) times the biome sector's
        /// <c>GetLevelUpChanceMultiplier</c>. On a world with a world level set the rate is
        /// dropped and the base is raised to a power instead, capped at 70 before the sector
        /// multiplier, and the points go on top of that just the same.
        ///
        /// The sector multiplier is new in 1.0 and not small. A sector can carry alt-biome
        /// modifiers that multiply the roll, and a modifier can require a minimum distance from
        /// the world centre (<c>AltBiome.m_minDistanceFromCenter</c>, 1000 in code, though the
        /// shipped values are asset data), so the roll runs higher in parts of the world far
        /// from it. Found on 2026-09-28, when the stars scenario ran in a Meadows stretch four
        /// kilometres out and every reading came out ten high: the game's own roll there was
        /// 20, and one kill made it 25, not 15 or 30.
        ///
        /// That run printed only the total, so the factor of two is worked out, not read. The
        /// world was made new that day and its saved metadata lists no world modifiers, which
        /// leaves the world setting at 1 and the world level at 0, and no other mod here touches
        /// this roll. What remains is the sector. The `vandi` readout and Devkit's starchance
        /// note both print the sector factor on its own, so the next run reads it directly, and
        /// if it says x1 the doubling came from somewhere this note has missed.
        ///
        /// One gate sits outside this method altogether. <c>SpawnSystem.Spawn</c> asks for the
        /// roll only when the spawn entry's <c>m_levelUpMinCenterDistance</c> is 0 or the spawn
        /// point lies beyond it. An entry that carries that distance spawns with no star roll
        /// inside it: no stars, and none of Vandi's points either, because this postfix is never
        /// called. Which vanilla entries set it is asset data and has not been read.
        ///
        /// 25 and not 30 is Robbin's call of the same day: the points stay flat on top of the
        /// land rather than growing with it, so a kill is worth five points wherever the
        /// creature spawns. That is why nothing Vandi says to a player, in the cfg or the
        /// README, promises a total. The total is the game's number plus this one, and only the
        /// second is Vandi's.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(SpawnSystem.GetLevelUpChance), new[] { typeof(Vector3), typeof(float) })]
        private static void Raise(Vector3 position, ref float __result)
        {
            float extra = Share(position);
            if (extra <= 0f) return;

            float was = __result;
            __result += extra;

            if (VandiConfig.Verbose.Value)
            {
                VandiPlugin.Log.LogInfo("Star chance at " + position + ": " + was.ToString("0.#")
                    + " + " + extra.ToString("0.#") + " = " + __result.ToString("0.#") + ".");
            }
        }

        /// <summary>
        /// What Raise adds at this position, and nothing else: zero while the mod is off.
        ///
        /// Its own method so the `vandi` readout can say how much of the game's number is
        /// Vandi's by asking the very code that adds it. A readout that worked the share out
        /// again for itself could disagree with the patch and still look right.
        /// </summary>
        internal static float Share(Vector3 position)
        {
            return VandiConfig.Enabled.Value ? Boost(position) : 0f;
        }

        /// <summary>
        /// The percentage points the local player has earned in the biome at this position.
        ///
        /// The largest of them when a biome has more than one owner rather than the sum: two
        /// bosses owning one biome is somebody's edit, and adding both would hand out double
        /// the boost for a pairing the default never makes.
        /// </summary>
        private static float Boost(Vector3 position)
        {
            if (Player.m_localPlayer == null) return 0f;

            WorldGenerator world = WorldGenerator.instance;
            if (world == null) return 0f;

            List<string> keys = Bosses.For(world.GetBiome(position));
            if (keys == null || keys.Count == 0) return 0f;

            int most = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                int kills = Kills.LocalCount(keys[i]);
                if (kills > most) most = kills;
            }

            if (most <= 0) return 0f;

            float earned = most * VandiConfig.StarChancePerKill.Value;
            float cap = VandiConfig.StarChanceCap.Value;

            return cap > 0f && earned > cap ? cap : earned;
        }
    }
}

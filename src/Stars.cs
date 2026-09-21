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
        [HarmonyPostfix]
        [HarmonyPatch(nameof(SpawnSystem.GetLevelUpChance), new[] { typeof(Vector3), typeof(float) })]
        private static void Raise(Vector3 position, ref float __result)
        {
            if (!VandiConfig.Enabled.Value) return;

            float extra = Boost(position);
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

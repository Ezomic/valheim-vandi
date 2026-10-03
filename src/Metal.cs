using System;
using System.Collections.Generic;
using System.Globalization;
using Ezomic.Shared;
using HarmonyLib;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// A boss you have killed enough times doubles the metal of its own biome, for you.
    ///
    /// Three decisions carry it, each made so that a friend cannot launder the reward.
    ///
    /// - <b>When.</b> At the moment the drop is made, never when it is picked up. A deposit's
    ///   drops come through ItemDrop.OnCreateNew, so the stack is doubled there, while the
    ///   game still knows whose blow it was. Doubling at pickup would let a friend mine and
    ///   you collect.
    /// - <b>Who.</b> The attacker on the blow that broke the chunk or killed the creature,
    ///   read off its HitData. OnCreateNew carries no attacker, so the three places that
    ///   create metal (MineRock5 and MineRock chunks, and Destructible scrap piles) open a
    ///   window around the call that sees the blow and close it again. A creature has no
    ///   window, see below. Malmr breaks a vein with a clone of the player's own blow, which keeps the
    ///   attacker, so the player whose swing filled the bar gets the whole deposit doubled.
    ///   When the attacker cannot be resolved on this machine (the player's object is not
    ///   loaded here, or the blow came from something that is not a player) nobody is
    ///   credited and nothing doubles. The nearest player is deliberately not guessed at,
    ///   because the game can say who struck and a guess would hand one player's reward to
    ///   another.
    /// - <b>Where.</b> On whichever machine creates the drop, which is the owner of the rock or
    ///   the corpse. It reads the attacker's count from the world's global keys, which every
    ///   client holds, so a client that does not own the rock still gets the right answer from
    ///   the owner. The owner must run Vandi too, which Core's gate already requires.
    ///
    /// Which items are metal is derived, never listed: an input of a smelter that burns fuel,
    /// so the furnace and the blast furnace and not the windmill or the spinning wheel, placed
    /// in a biome by the shared BiomeIndex, whose boss is then the one that counts. Bars and
    /// crafted items are outputs, so they never double.
    ///
    /// Creatures are the exception to "through OnCreateNew": CharacterDrop.DropItems writes the
    /// item's fields itself and never calls it, so a creature's metal is doubled by doubling the
    /// count in its drop list instead. And not in a window around CharacterDrop.OnDeath: a
    /// creature whose Ragdoll has m_dropItems has its list made by Ragdoll.Setup through
    /// GenerateDropList during Character.OnDeath, with CharacterDrop's own drops switched off,
    /// and dropped seconds later by Ragdoll.SpawnLoot, when no blow is in sight. Both paths
    /// call GenerateDropList on the owner while Character.m_lastHit is still the killing
    /// blow, so that is where the list is doubled.
    /// </summary>
    internal static class Metal
    {
        /// <summary>
        /// Pinned placements for the roots, as in Yoke. Iron scrap lives in crypts and is never
        /// placed by a table the game exposes, so without its pin it is unplaced and iron would
        /// never double. The rest are here so a content mod cannot move them by adding a drop.
        /// </summary>
        private const string Roots =
            "CopperOre:blackforest, TinOre:blackforest, IronScrap:swamp, SilverOre:mountain, "
            + "BlackMetalScrap:plains, FlametalOre:ashlands, FlametalOreNew:ashlands, "
            + "CopperScrap:blackforest, BronzeScrap:blackforest";

        private static bool _open;
        private static long _credited;
        private static readonly HashSet<int> Done = new HashSet<int>();

        /// <summary>
        /// The window each Open replaced, restored by the matching Close. Windows nest: a
        /// Destructible that spawns a MineRock5 on destruction calls MineRock5.Damage inside its
        /// own Destroy, and the inner Close must give the outer window back rather than end it.
        /// </summary>
        private static readonly Stack<KeyValuePair<bool, long>> Outer = new Stack<KeyValuePair<bool, long>>();

        /// <summary>Stacks doubled since the game started. Read by `vandi metal`.</summary>
        internal static int Doubled;

        private static readonly Dictionary<string, string> Placed = new Dictionary<string, string>();
        private static HashSet<string> _inputs;
        private static ZNetScene _inputsFor;
        private static float _nextPrepare;
        private static bool _pending;

        internal static void Wire()
        {
            BiomeIndex.Log = VandiPlugin.Log;
            BiomeIndex.BiomeForKey = Bosses.BiomeNameFor;
            BiomeIndex.Overrides = () => Roots;
        }

        private static void Open(HitData hit)
        {
            Outer.Push(new KeyValuePair<bool, long>(_open, _credited));
            _open = false;

            try
            {
                if (!VandiConfig.Enabled.Value || VandiConfig.DoubleAtKills.Value <= 0 || hit == null) return;

                Player player = hit.GetAttacker() as Player;
                if (player == null) return;

                _credited = player.GetPlayerID();
                _open = true;
            }
            catch (Exception error)
            {
                VandiPlugin.LogOnce("Vandi could not read the attacker of a blow, so that blow's metal "
                    + "does not double: " + error);
            }
        }

        private static void Close()
        {
            if (Outer.Count == 0)
            {
                _open = false;
                Done.Clear();
                return;
            }

            KeyValuePair<bool, long> before = Outer.Pop();
            _open = before.Key;
            _credited = before.Value;

            if (Outer.Count == 0) Done.Clear();
        }

        [HarmonyPatch(typeof(MineRock5), "DamageArea")]
        [HarmonyPrefix]
        private static void Rock5Opens(HitData hit)
        {
            Open(hit);
        }

        [HarmonyPatch(typeof(MineRock5), "DamageArea")]
        [HarmonyFinalizer]
        private static void Rock5Closes()
        {
            Close();
        }

        [HarmonyPatch(typeof(MineRock), "RPC_Hit")]
        [HarmonyPrefix]
        private static void RockOpens(HitData hit)
        {
            Open(hit);
        }

        [HarmonyPatch(typeof(MineRock), "RPC_Hit")]
        [HarmonyFinalizer]
        private static void RockCloses()
        {
            Close();
        }

        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Destroy))]
        [HarmonyPrefix]
        private static void DestructibleOpens(HitData hit)
        {
            Open(hit);
        }

        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Destroy))]
        [HarmonyFinalizer]
        private static void DestructibleCloses()
        {
            Close();
        }

        private static System.Reflection.FieldInfo _lastHit;
        private static bool _lastHitTried;

        /// <summary>
        /// The creature's last blow. Character.m_lastHit is protected and is stored on the blow
        /// that kills as well, because the game sets it before subtracting the damage. Bound
        /// lazily so a rename costs the creature half and not the class.
        /// </summary>
        private static HitData LastHit(Character character)
        {
            if (!_lastHitTried)
            {
                _lastHitTried = true;
                try { _lastHit = AccessTools.Field(typeof(Character), "m_lastHit"); }
                catch (Exception) { _lastHit = null; }

                if (_lastHit == null)
                    VandiPlugin.LogOnce("Character.m_lastHit was not found, so a creature's metal "
                        + "cannot be doubled. Scrap piles and deposits are not affected.");
            }

            return _lastHit == null || character == null ? null : _lastHit.GetValue(character) as HitData;
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new[] { typeof(GameObject), typeof(bool) })]
        [HarmonyPostfix]
        private static void CreatedFromObject(GameObject go)
        {
            if (!_open || go == null) return;

            try
            {
                ItemDrop drop;
                if (go.TryGetComponent(out drop)) Double(drop);
            }
            catch (Exception error) { DoubleFailed(error); }
        }

        // Both overloads are patched. The object overload calls this one, and a method this
        // small can be inlined into its caller, which would skip a patch on only one of them.
        // Done makes the pair safe to run in either order.
        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new[] { typeof(ItemDrop), typeof(bool) })]
        [HarmonyPostfix]
        private static void CreatedFromItem(ItemDrop item)
        {
            if (!_open || item == null) return;

            try { Double(item); }
            catch (Exception error) { DoubleFailed(error); }
        }

        private static void DoubleFailed(Exception error)
        {
            VandiPlugin.LogOnce("Doubling a metal drop failed, so that drop was left as the game made it: "
                + error);
        }

        private static void Double(ItemDrop drop)
        {
            if (!Done.Add(drop.GetInstanceID())) return;

            string name = Utils.GetPrefabName(drop.gameObject);

            string why;
            if (!Qualifies(_credited, name, out why)) return;

            ItemDrop.ItemData data = drop.m_itemData;
            int stack = data.m_stack;
            int ceiling = Math.Max(stack, data.m_shared.m_maxStackSize);
            data.m_stack = Math.Min(stack * 2, ceiling);
            Doubled++;

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Doubled " + name + " for player " + _credited + ": " + stack
                    + " became " + data.m_stack + ".");
        }

        /// <summary>
        /// Doubles a creature's metal where its drop list is made. See the class summary for why
        /// this is GenerateDropList and not DropItems. The attacker is read off the creature's
        /// last blow, and nothing doubles when it is not a player on this machine.
        /// </summary>
        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        [HarmonyPostfix]
        private static void CreatureList(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
        {
            if (__result == null || __result.Count == 0) return;

            try
            {
                if (!VandiConfig.Enabled.Value || VandiConfig.DoubleAtKills.Value <= 0) return;

                Character character = __instance == null ? null : __instance.GetComponent<Character>();
                HitData hit = LastHit(character);
                if (hit == null) return;

                Player player = hit.GetAttacker() as Player;
                if (player == null) return;

                long id = player.GetPlayerID();

                for (int i = 0; i < __result.Count; i++)
                {
                    KeyValuePair<GameObject, int> entry = __result[i];
                    if (entry.Key == null) continue;

                    string why;
                    if (!Qualifies(id, Utils.GetPrefabName(entry.Key), out why)) continue;

                    __result[i] = new KeyValuePair<GameObject, int>(entry.Key, entry.Value * 2);
                    Doubled += entry.Value;

                    if (VandiConfig.Verbose.Value)
                        VandiPlugin.Log.LogInfo("Doubled " + entry.Key.name + " for player " + id
                            + ": " + entry.Value + " became " + entry.Value * 2 + ".");
                }
            }
            catch (Exception error) { DoubleFailed(error); }
        }

        /// <summary>
        /// Whether this player's kills make this item drop double, and the reason when not.
        /// The one place the answer is worked out, so the drop and the console cannot differ.
        /// </summary>
        internal static bool Qualifies(long playerId, string prefab, out string why)
        {
            int needed = VandiConfig.DoubleAtKills.Value;
            if (needed <= 0) { why = "DoubleAtKills is 0"; return false; }

            if (IsListed(VandiConfig.NeverDouble.Value, prefab)) { why = "in NeverDouble"; return false; }

            string biome = BiomeOfMetal(prefab, out why);
            if (biome == null) return false;

            Heightmap.Biome parsed;
            try { parsed = (Heightmap.Biome)Enum.Parse(typeof(Heightmap.Biome), biome, true); }
            catch (Exception) { why = "biome " + biome + " is not one the game has"; return false; }

            List<string> keys = Bosses.For(parsed);
            if (keys == null) { why = "no boss is listed for " + biome + " in BossBiomes"; return false; }

            string listed = VandiConfig.DoubleBosses.Value;
            bool any = false;
            int best = 0;

            foreach (string key in keys)
            {
                if (!IsListed(listed, key)) continue;

                any = true;
                int kills = Kills.Count(playerId, key);
                if (kills > best) best = kills;

                if (kills >= needed) { why = key + " killed " + kills + " of " + needed; return true; }
            }

            why = any ? "boss killed " + best + " of " + needed + " times"
                      : "the boss of " + biome + " is not in DoubleBosses";

            return false;
        }

        /// <summary>The biome name of a metal, or null with the reason it is not one.</summary>
        private static string BiomeOfMetal(string prefab, out string why)
        {
            string cached;
            if (Placed.TryGetValue(prefab, out cached))
            {
                why = cached == null ? "not a metal a fuelled smelter takes in, or no biome" : null;
                return cached;
            }

            if (!Ready())
            {
                why = "the biome index is not built yet";
                return null;
            }

            string biome = null;
            why = "not an input of a fuelled smelter";

            if (_inputs.Contains(prefab))
            {
                string placed = BiomeIndex.BiomeOf(prefab);
                if (placed == BiomeIndex.None) why = "no biome could be worked out for it";
                else { biome = placed; why = null; }
            }

            Placed[prefab] = biome;

            return biome;
        }

        /// <summary>
        /// Builds the smelter inputs and the biome index once per world. Lazily, because
        /// ZoneSystem, SpawnSystem and ZNetScene are not all there when the item database is
        /// built, and a build that runs on an incomplete index is thrown away by BiomeIndex
        /// itself. Retried at most every five seconds so a drop in an index that never
        /// completes costs nothing.
        /// </summary>
        private static bool Ready()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null) return false;

            if (_inputs == null || _inputsFor != scene)
            {
                _inputsFor = scene;
                _inputs = FuelledInputs(scene);
                Placed.Clear();
            }

            if (BiomeIndex.Complete) return true;

            if (Time.realtimeSinceStartup < _nextPrepare) return false;
            _nextPrepare = Time.realtimeSinceStartup + 5f;

            try { BiomeIndex.Prepare(); }
            catch (Exception error)
            {
                VandiPlugin.LogOnce("The biome index could not be built, so no metal doubles: " + error);
                return false;
            }

            return BiomeIndex.Complete;
        }

        /// <summary>
        /// What the furnace and the blast furnace take in. A smelter without a fuel item is a
        /// windmill, a spinning wheel or a kiln, and flax and barley are not metal.
        /// </summary>
        private static HashSet<string> FuelledInputs(ZNetScene scene)
        {
            HashSet<string> found = new HashSet<string>();

            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null) continue;

                Smelter smelter;
                if (!prefab.TryGetComponent(out smelter) || smelter.m_fuelItem == null
                    || smelter.m_conversion == null) continue;

                foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
                    if (conversion != null && conversion.m_from != null)
                        found.Add(conversion.m_from.name);
            }

            return found;
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        private static void ItemsRebuilt()
        {
            Reset();
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        private static void ItemsCopied()
        {
            Reset();
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void SceneBuilt()
        {
            Reset();
        }

        private static void Reset()
        {
            BiomeIndex.Invalidate();
            _inputs = null;
            Placed.Clear();
            _nextPrepare = 0f;
            _pending = true;
        }

        /// <summary>
        /// Called from the plugin's Update. Builds the smelter inputs and the biome index as
        /// soon as a world is there, retrying through Ready's own five second throttle until
        /// the index is complete, so the first metal drop of a session does not pay for a scan
        /// of every prefab. A drop that arrives before this has finished still builds on demand.
        /// </summary>
        internal static void Tick()
        {
            if (!_pending || ZNetScene.instance == null) return;

            try { if (Ready()) _pending = false; }
            catch (Exception error)
            {
                _pending = false;
                VandiPlugin.LogOnce("Vandi could not prepare its metal list at world load, so it will "
                    + "be built at the first drop instead: " + error);
            }
        }

        private static bool IsListed(string list, string name)
        {
            if (string.IsNullOrEmpty(list) || string.IsNullOrEmpty(name)) return false;

            foreach (string entry in list.Split(','))
                if (string.Equals(entry.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        /// <summary>
        /// `vandi metal` lists every metal Vandi would double for you and why the others would
        /// not, and `vandi metal CopperOre` answers for one prefab. Written for the scenario,
        /// which reads the verdict and the doubled count, and for a player who wants to know
        /// why a particular ore did not double.
        /// </summary>
        internal static void Print(Terminal term, string[] words)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { term.AddString("vandi metal: no character in a world yet"); return; }

            long id = player.GetPlayerID();

            term.AddString("vandi metal: doubling at " + VandiConfig.DoubleAtKills.Value
                + " kills of the biome's boss (DoubleAtKills), for " + VandiConfig.DoubleBosses.Value);
            term.AddString("vandi metal doubled=" + Doubled.ToString(CultureInfo.InvariantCulture)
                + " stacks since the game started");

            if (words.Length > 2)
            {
                Verdict(term, id, words[2]);
                return;
            }

            if (!Ready()) { term.AddString("vandi metal: the biome index is not built yet, try again"); return; }

            List<string> names = new List<string>(_inputs);
            names.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string name in names) Verdict(term, id, name);
        }

        private static void Verdict(Terminal term, long id, string prefab)
        {
            string why;
            bool yes = Qualifies(id, prefab, out why);

            string biome = null;
            string ignore;
            if (Ready()) biome = BiomeOfMetal(prefab, out ignore);

            term.AddString("vandi metal " + prefab + ": biome " + (biome ?? "none")
                + ", doubled=" + (yes ? "yes" : "no") + " (" + why + ")");
        }
    }
}

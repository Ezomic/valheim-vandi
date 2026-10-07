using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// Who paid for a boss, what that costs them next time, and who gets the credit when it
    /// falls.
    ///
    /// The whole feature turns on one decision Robbin made on 2026-09-19: credit belongs to
    /// the player who made the offering and to nobody else, even if they are dead, offline or
    /// far away when the boss dies. So the summoner has to be written down at the altar and
    /// read back at the corpse, and the two are not the same machine.
    ///
    /// Three moments, each on the machine that actually has the answer:
    ///
    /// - <b>The offering.</b> InitiateSpawnBoss runs on the client that used the altar, which
    ///   is the only machine that knows whose offering it was. It publishes that as a global
    ///   key, one per boss, overwritten each time.
    /// - <b>The spawn.</b> DelayedSpawnBoss runs on whichever client owns the altar, which is
    ///   usually but not always the summoner. It reads the key, gives the boss its stars out
    ///   of that player's record, and stamps the id on the boss's own ZDO so the answer
    ///   travels with the creature rather than with the world.
    /// - <b>The death.</b> The owning client reads the stamp and records the kill for whoever
    ///   is named on it. Only the owner, because a boss with a death animation reaches OnDeath
    ///   on every client animating it. Ownership and the stamp are read in a prefix (Dying),
    ///   since by the time a postfix runs the owner's view has already let go of the ZDO, and
    ///   the kill is recorded in the postfix (Died) from what the prefix saw.
    ///
    /// The global key is a handover between two machines a few seconds apart, not a record.
    /// Two people summoning the same boss type in the same five seconds would hand the second
    /// one's stars to the first one's boss. It is written down rather than defended against:
    /// altars are fixed places, the two players would be standing on each other, and a design
    /// that spends a networked message on that case buys nothing anybody will ever see.
    /// </summary>
    internal static class Summon
    {
        /// <summary>"the last offering for this boss was made by", as a player id.</summary>
        private const string SummonPrefix = "vandi_summon_";

        /// <summary>The summoner, written on the boss itself so it survives the handover.</summary>
        private static readonly int SummonerKey = "vandi_summoner".GetStableHashCode();

        /// <summary>How far from the altar's spawn point a new boss can be and still be ours.</summary>
        private const float SpawnSearchRadius = 8f;

        [HarmonyPatch(typeof(OfferingBowl), "InitiateSpawnBoss")]
        [HarmonyPostfix]
        private static void Offered(OfferingBowl __instance, bool __runOriginal)
        {
            // A postfix runs even when a prefix stopped the summon, and one does: Utangard
            // refuses an altar in a biome the group has not earned. Recording that as an
            // offering would name a summoner for a boss that never came.
            if (!__runOriginal) return;

            if (!VandiConfig.Enabled.Value || Player.m_localPlayer == null) return;

            string boss = Bosses.KeyOf(__instance == null ? null : __instance.m_bossPrefab);
            if (boss == null) return;

            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return;

            long id = Player.m_localPlayer.GetPlayerID();

            // Written every time rather than only on change, unlike the kill counts: this is
            // a message, and the whole point of it is that the value is whoever summoned
            // last. A rebroadcast of the key list per boss summoned is a cost worth paying
            // once an hour at most.
            zone.SetGlobalKey((SummonPrefix + boss).ToLowerInvariant() + " "
                              + id.ToString(CultureInfo.InvariantCulture));

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Offering made for " + boss + " by " + id + ".");
        }

        [HarmonyPatch(typeof(OfferingBowl), "DelayedSpawnBoss")]
        [HarmonyPostfix]
        // ___m_bossSpawnPoint is Harmony's private-field injection: the point the altar
        // picked is private, and a name that does not exist fails at patch time with a line
        // naming it rather than silently finding nothing later.
        private static void Spawned(OfferingBowl __instance, Vector3 ___m_bossSpawnPoint)
        {
            if (!VandiConfig.Enabled.Value || __instance == null) return;

            string boss = Bosses.KeyOf(__instance.m_bossPrefab);
            if (boss == null) return;

            long summoner = Summoner(boss);
            if (summoner == 0L) return;

            Character character = JustSpawned(___m_bossSpawnPoint, boss);
            if (character == null) return;

            ZNetView nview = character.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

            // The stamp goes on whatever happens next, even when the boss gains no stars:
            // the credit half of the mod is not the same decision as the harder half, and
            // turning HarderBosses off must not also stop anyone earning anything.
            nview.GetZDO().Set(SummonerKey, summoner);

            int kills = Kills.Count(summoner, boss);
            int stars = StarsFor(kills);
            if (stars <= 0) return;

            // Level, not stars: the game counts level 1 as a plain creature, so two stars is
            // level three. SetLevel writes to the ZDO we own, which is what makes it stick
            // for everybody who sees the fight.
            character.SetLevel(stars + 1);

            VandiPlugin.Log.LogInfo("Summoned " + boss + " at " + stars + " star(s) for player "
                + summoner + ", who has killed it " + kills + " time(s).");
        }

        /// <summary>
        /// The stars a boss comes back with for a summoner who has killed it this many times: one
        /// per kill, held at BossStarCap, and none while HarderBosses is off. Its own method so the
        /// compendium page reads the number the altar uses.
        /// </summary>
        internal static int StarsFor(int kills)
        {
            if (!VandiConfig.HarderBosses.Value || kills <= 0) return 0;

            int cap = VandiConfig.BossStarCap.Value;
            int stars = kills > cap ? cap : kills;

            return stars < 0 ? 0 : stars;
        }

        /// <summary>What Dying saw on the boss, handed to Died through Harmony's __state.</summary>
        private struct Death
        {
            /// <summary>This machine owned the boss when OnDeath began, so it records the kill.</summary>
            internal bool Owner;

            internal string Boss;

            internal long Summoner;
        }

        /// <summary>
        /// The half of the kill credit that has to run before vanilla's OnDeath: whether this
        /// machine owns the boss, and whose stamp is on it.
        ///
        /// Both answers live on the boss's ZDO, and on the owner that ZDO is gone before any
        /// postfix runs. OnDeath's last line is ZNetScene.Destroy, which calls ResetZDO on the
        /// view first thing, so a postfix finds IsValid false, IsOwner false (it asks IsValid
        /// before anything else) and GetZDO null. On every other client animating the boss,
        /// OnDeath leaves at its own IsOwner return before it reaches Destroy, so there the view
        /// is still whole and IsOwner is false. Died used to ask both questions itself, and
        /// they failed on every machine: the owner at IsValid and everyone else at IsOwner.
        /// Before IsOwner was added on 2026-09-27 it asked IsValid alone, which the owner failed
        /// just the same, so a kill counted only on the other clients watching the death
        /// animation, once per client, and never in singleplayer. Found on 2026-09-28; no log
        /// shows a real kill recorded before then. So the questions are asked here, while the
        /// view is still whole, and the answers travel to Died.
        /// </summary>
        [HarmonyPatch(typeof(Character), "OnDeath")]
        [HarmonyPrefix]
        private static void Dying(Character __instance, out Death __state)
        {
            __state = default(Death);

            if (!VandiConfig.Enabled.Value || __instance == null) return;

            string boss = Bosses.KeyOf(__instance);

            // Not "has a defeat key": a bat has one of those. Only a key the config maps to a
            // biome is a boss as far as this mod is concerned, which also keeps the verbose
            // line in Died from saying something about every bat in a frost cave.
            if (!Bosses.IsBossKey(boss)) return;

            // The owner only, and the same test vanilla makes a few lines into OnDeath, so
            // this answers yes on exactly the machine where OnDeath will run to its end. A
            // creature with m_deathAnimation does not die through CheckDeath's direct call:
            // its death animation fires CharacterAnimEvent.Die, which calls OnDeath on EVERY
            // client animating it. Without this, each player standing at the altar would read
            // the same stamp and add a kill, so one boss could count two or three times. Found
            // on 2026-09-27 while building Utangard's kill tally.
            ZNetView nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

            __state.Owner = true;
            __state.Boss = boss;
            __state.Summoner = nview.GetZDO().GetLong(SummonerKey, 0L);
        }

        /// <summary>
        /// Records the kill, from what Dying read before the ZDO was let go. It stays a postfix
        /// so a kill is written only once vanilla's OnDeath has run to its end, and it touches
        /// neither the view nor the ZDO, which are gone by now on the one machine that gets
        /// this far. A postfix runs even when another mod's prefix skipped OnDeath, which is a
        /// boss that did not die, so __runOriginal is checked as Offered checks it.
        /// </summary>
        [HarmonyPatch(typeof(Character), "OnDeath")]
        [HarmonyPostfix]
        private static void Died(bool __runOriginal, Death __state)
        {
            if (!__runOriginal || !__state.Owner) return;

            string boss = __state.Boss;
            long summoner = __state.Summoner;
            if (summoner == 0L)
            {
                // A boss nobody summoned through an altar this session: the world's own
                // Eikthyr sitting where the world generator put it, or one spawned before
                // this mod was installed. Nothing to credit, and guessing an owner from who
                // is standing nearby is the rule this mod exists to not have.
                if (VandiConfig.Verbose.Value)
                    VandiPlugin.Log.LogInfo(boss + " died with no summoner on it, so nobody is credited.");

                return;
            }

            int count = Kills.Add(summoner, boss);

            VandiPlugin.Log.LogInfo(boss + " credited to player " + summoner + ", now " + count
                + " kill(s).");
        }

        /// <summary>The player id on the world's note for this boss, or 0 when there is none.</summary>
        private static long Summoner(string boss)
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return 0L;

            string value;
            if (!zone.GetGlobalKey((SummonPrefix + boss).ToLowerInvariant(), out value)) return 0L;

            long id;

            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ? id : 0L;
        }

        /// <summary>
        /// The boss the altar has just made, found by looking rather than by being handed it.
        ///
        /// DelayedSpawnBoss instantiates into a local variable and returns nothing, so a
        /// postfix cannot be given the object. Reading the live character list for one with
        /// this boss's defeat key, close to the altar's own spawn point and not yet stamped,
        /// finds it without a transpiler over a method that is likely to change.
        /// </summary>
        private static Character JustSpawned(Vector3 point, string boss)
        {
            List<Character> all = Character.GetAllCharacters();
            if (all == null) return null;

            Character best = null;
            float nearest = SpawnSearchRadius * SpawnSearchRadius;

            for (int i = 0; i < all.Count; i++)
            {
                Character character = all[i];
                if (character == null || Bosses.KeyOf(character) != boss) continue;

                ZNetView nview = character.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                // Already carrying a summoner is an older boss of the same kind standing in
                // the same place, which happens at an altar somebody has used twice.
                if (nview.GetZDO().GetLong(SummonerKey, 0L) != 0L) continue;

                float distance = (character.transform.position - point).sqrMagnitude;
                if (distance > nearest) continue;

                nearest = distance;
                best = character;
            }

            return best;
        }
    }
}

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// Wild creatures of the boss's own biome that were already standing near the fight are drawn
    /// in and sent after the player, on the same clock as the spawned adds.
    ///
    /// <b>They stay the game's own creatures.</b> Not marked, so their deaths do not chip the boss,
    /// and their loot is untouched: the fight pulls the biome in, it does not pay for it.
    ///
    /// <b>Hunting is written by the creature's owner.</b> SetHuntPlayer changes the field on any
    /// machine but only writes the ZDO on the owner, and a non-owner's AI does not run, so a
    /// creature owned elsewhere is asked by routed message, the way a boss add's death is.
    /// The decision itself is the boss owner's, because Wave is only called from there.
    /// </summary>
    internal sealed class Pull
    {
        internal const string PullRpc = "Vandi_Pull";
        internal const string PulledKey = "vandi_pulled";
        private const string CountKey = "vandi_pulled_count";

        private readonly HashSet<ZDOID> _seen = new HashSet<ZDOID>();

        internal void Wave(Character boss)
        {
            if (!VandiConfig.PullWild.Value) return;
            ZNetView bossView = boss.GetComponent<ZNetView>();
            if (bossView == null || !bossView.IsValid()) return;

            ZDO bossZdo = bossView.GetZDO();
            int pulled = bossZdo.GetInt(CountKey, 0);
            if (pulled >= VandiConfig.PullMaxPerFight.Value) return;

            string key = Bosses.KeyOf(boss);
            if (key == null) return;

            Vector3 centre = boss.transform.position;
            float radius = VandiConfig.PullRadius.Value;

            List<Character> near = new List<Character>();
            foreach (Character c in Character.GetAllCharacters())
                if (c != null && c != boss && Vector3.Distance(c.transform.position, centre) <= radius && Wild(c, key))
                    near.Add(c);

            near.Sort((a, b) => Vector3.Distance(a.transform.position, centre).CompareTo(Vector3.Distance(b.transform.position, centre)));

            int room = Mathf.Min(VandiConfig.PullPerWave.Value, VandiConfig.PullMaxPerFight.Value - pulled);
            int sent = 0;

            foreach (Character c in near)
            {
                if (sent >= room) break;

                Send(c, bossZdo.m_uid);
                sent++;
            }

            if (sent > 0) bossZdo.Set(CountKey, pulled + sent);

            if (sent > 0 && VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss pull: " + sent + " wild creature(s) drawn in, " + (pulled + sent) + " of at most "
                    + VandiConfig.PullMaxPerFight.Value + " this fight, " + near.Count + " in reach.");
        }

        private bool Wild(Character c, string bossKey)
        {
            if (c.IsPlayer() || c.IsTamed() || c.IsDead() || c.IsBoss()) return false;
            if (c.m_faction == Character.Faction.Players || c.m_faction == Character.Faction.Boss) return false;
            if (Bosses.IsBossKey(Bosses.KeyOf(c))) return false;
            if (c.GetComponent<MonsterAI>() == null) return false;

            ZNetView view = c.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;

            ZDO zdo = view.GetZDO();
            if (_seen.Contains(zdo.m_uid)) return false;
            if (zdo.GetZDOID(FanSpecial.AddKey) != ZDOID.None) return false;
            if (zdo.GetBool(ZDOVars.s_huntPlayer, false)) return false;

            List<string> owners = Bosses.For(Heightmap.FindBiome(c.transform.position));
            return owners != null && owners.Contains(bossKey);
        }

        private void Send(Character c, ZDOID boss)
        {
            ZNetView view = c.GetComponent<ZNetView>();
            ZDO zdo = view.GetZDO();

            _seen.Add(zdo.m_uid);
            Order(view, zdo, boss, true);
        }

        private static void Order(ZNetView view, ZDO zdo, ZDOID boss, bool hunt)
        {
            if (view.IsOwner()) Hunt(view.gameObject, boss, hunt);
            else ZRoutedRpc.instance.InvokeRoutedRPC(zdo.GetOwner(), PullRpc, zdo.m_uid, boss, hunt);
        }

        private static void Hunt(GameObject go, ZDOID boss, bool hunt)
        {
            BaseAI ai = go.GetComponent<BaseAI>();
            ZNetView view = go.GetComponent<ZNetView>();
            if (ai == null || view == null || !view.IsValid()) return;

            view.GetZDO().Set(PulledKey, hunt ? boss : ZDOID.None);
            ai.SetHuntPlayer(hunt);

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss pull: " + go.name + (hunt ? " now hunting, " : " released, hunting ") + ai.HuntPlayer() + ".");
        }

        internal static void Answer(long sender, ZDOID id, ZDOID boss, bool hunt)
        {
            ZDO zdo = ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(id);
            if (zdo == null || !zdo.IsOwner() || ZNetScene.instance == null) return;

            GameObject go = ZNetScene.instance.FindInstance(id);
            if (go != null) Hunt(go, boss, hunt);
        }

        /// <summary>
        /// The fight is over: what this boss pulled goes back to being wild. The hunt flag is in the
        /// creature's ZDO and survives a reload, so a Draugr drawn in would otherwise hunt the player
        /// for ever. PullRelease off leaves them hunting.
        /// </summary>
        internal static void Release(ZDOID boss)
        {
            if (!VandiConfig.PullRelease.Value) return;

            foreach (Character c in new List<Character>(Character.GetAllCharacters()))
            {
                ZNetView view = c == null ? null : c.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || view.GetZDO().GetZDOID(PulledKey) != boss) continue;

                Order(view, view.GetZDO(), boss, false);
            }
        }
    }

    internal static class PullPatches
    {
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void Register()
        {
            if (ZRoutedRpc.instance == null) return;

            ZRoutedRpc.instance.Register<ZDOID, ZDOID, bool>(Pull.PullRpc, Pull.Answer);

            if (VandiConfig.Verbose.Value) VandiPlugin.Log.LogInfo("Boss pull: " + Pull.PullRpc + " registered.");
        }

        [HarmonyPatch(typeof(Character), "OnDeath")]
        [HarmonyPrefix]
        private static void BossDying(Character __instance)
        {
            if (__instance == null || __instance.GetComponent<FanSpecial>() == null) return;

            ZNetView view = __instance.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.IsOwner()) Pull.Release(view.GetZDO().m_uid);
        }

        // A creature that wakes up still marked by a boss that no longer exists was left hunting by
        // a fight nobody finished: the boss left without dying and the world was saved.
        [HarmonyPatch(typeof(BaseAI), "Awake")]
        [HarmonyPostfix]
        private static void Woke(BaseAI __instance)
        {
            if (!VandiConfig.PullRelease.Value || ZDOMan.instance == null) return;

            ZNetView view = __instance.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return;

            ZDOID boss = view.GetZDO().GetZDOID(Pull.PulledKey);
            if (boss != ZDOID.None) __instance.StartCoroutine(Forget(view, boss));
        }

        // Delayed: the zone's ZDOs are not all loaded while the first creature is waking.
        private static System.Collections.IEnumerator Forget(ZNetView view, ZDOID boss)
        {
            yield return new WaitForSeconds(5f);

            if (view != null && view.IsValid() && view.IsOwner() && ZDOMan.instance.GetZDO(boss) == null)
                Pull.Answer(0L, view.GetZDO().m_uid, boss, false);
        }
    }
}

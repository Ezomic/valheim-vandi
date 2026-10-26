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

        private int _pulled;
        private readonly HashSet<ZDOID> _seen = new HashSet<ZDOID>();

        internal void Wave(Character boss)
        {
            if (!VandiConfig.PullWild.Value) return;
            if (_pulled >= VandiConfig.PullMaxPerFight.Value) return;

            string key = Bosses.KeyOf(boss);
            if (key == null) return;

            Vector3 centre = boss.transform.position;
            float radius = VandiConfig.PullRadius.Value;

            List<Character> near = new List<Character>();
            foreach (Character c in Character.GetAllCharacters())
                if (c != null && c != boss && Vector3.Distance(c.transform.position, centre) <= radius && Wild(c, key))
                    near.Add(c);

            near.Sort((a, b) => Vector3.Distance(a.transform.position, centre).CompareTo(Vector3.Distance(b.transform.position, centre)));

            int room = Mathf.Min(VandiConfig.PullPerWave.Value, VandiConfig.PullMaxPerFight.Value - _pulled);
            int sent = 0;

            foreach (Character c in near)
            {
                if (sent >= room) break;

                Send(c);
                sent++;
            }

            if (sent > 0 && VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss pull: " + sent + " wild creature(s) drawn in, " + _pulled + " of at most "
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

        private void Send(Character c)
        {
            ZNetView view = c.GetComponent<ZNetView>();
            ZDO zdo = view.GetZDO();

            _seen.Add(zdo.m_uid);
            _pulled++;

            if (view.IsOwner()) Hunt(c.gameObject);
            else ZRoutedRpc.instance.InvokeRoutedRPC(zdo.GetOwner(), PullRpc, zdo.m_uid);
        }

        private static void Hunt(GameObject go)
        {
            BaseAI ai = go.GetComponent<BaseAI>();
            if (ai == null) return;

            ai.SetHuntPlayer(true);

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss pull: " + go.name + " now hunting, " + ai.HuntPlayer() + ".");
        }

        internal static void Answer(long sender, ZDOID id)
        {
            ZDO zdo = ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(id);
            if (zdo == null || !zdo.IsOwner() || ZNetScene.instance == null) return;

            GameObject go = ZNetScene.instance.FindInstance(id);
            if (go != null) Hunt(go);
        }
    }

    internal static class PullPatches
    {
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void Register()
        {
            if (ZRoutedRpc.instance != null)
                ZRoutedRpc.instance.Register<ZDOID>(Pull.PullRpc, Pull.Answer);
        }
    }
}

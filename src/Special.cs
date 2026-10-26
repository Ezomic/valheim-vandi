using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// The first of a boss's own moves: Bonemass rears up and throws three skeletons in a fan.
    ///
    /// It is a component rather than a patch on the AI, because nothing here changes how the
    /// vanilla AI decides to attack. It watches, and now and then it borrows the boss for a few
    /// seconds. That keeps it off the seam that breaks when a game update reworks MonsterAI.
    ///
    /// <b>Only on the machine that owns the boss</b>, the same rule as the star roll and the
    /// kill credit: the skeletons are real creatures and have to be made once. Everyone else
    /// sees them arrive, and sees the rear-up because the bones are posed from the boss's own
    /// timer, which every client runs, but only the owner starts it.
    ///
    /// <b>Only for a boss somebody summoned who has killed it before.</b> A boss at level 1, or
    /// one with no summoner stamp, is exactly the game's boss. The special is part of what a
    /// repeat kill buys the boss, not something every Bonemass does.
    ///
    /// <b>The skeletons are marked and are worth nothing.</b> Drops off, so a fight is not a
    /// farm. Their mark is what a later slice reads to let their deaths chip the boss.
    /// </summary>
    internal sealed class FanSpecial : MonoBehaviour
    {
        internal const string AddKey = "vandi_boss_add";

        private const string Skeleton = "Skeleton";

        private Character _boss;
        private ZNetView _nview;
        private BaseAI _ai;
        private float _next;
        private float _pose;
        private Transform _spine2, _spine3, _lArm, _rArm;

        private void Awake()
        {
            _boss = GetComponent<Character>();
            _nview = GetComponent<ZNetView>();
            _ai = GetComponent<BaseAI>();

            Transform root = transform;
            _spine2 = Find(root, "spine2");
            _spine3 = Find(root, "spine3");
            _lArm = Find(root, "l_upperarm");
            _rArm = Find(root, "r_upperarm");

            _next = Time.time + VandiConfig.FanFirstDelay.Value;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;

            return null;
        }

        private float _nextAdd;
        private readonly List<Character> _adds = new List<Character>();

        private void Update()
        {
            if (!VandiConfig.Enabled.Value || !VandiConfig.BossSpecials.Value) return;
            if (_boss == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            if (_boss.IsDead() || _ai == null) return;
            if (!_ai.IsAlerted()) { Why("not alerted"); return; }
            if (_boss.GetLevel() < 2) { Why("level " + _boss.GetLevel()); return; }
            if (_nview.GetZDO().GetLong("vandi_summoner".GetStableHashCode(), 0L) == 0L) { Why("no summoner"); return; }

            Character target = _ai.GetTargetCreature();
            if (target == null) { Why("no target"); return; }

            if (Time.time >= _next && SpecialLock.TryTake(_boss))
            {
                _next = Time.time + VandiConfig.FanCooldown.Value;
                StartCoroutine(Fan(target));
            }

            if (Time.time >= _nextAdd) Wave(target);
        }

        /// <summary>
        /// One add, spawned out at the edge of the fight and sent straight for the player. The gap
        /// to the next one shrinks as the boss loses health, so the fight starts slow and ends
        /// crowded, and never holds more than AddMaxAlive at once.
        /// </summary>
        private void Wave(Character target)
        {
            float health = Mathf.Clamp01(_boss.GetHealthPercentage());
            float gap = Mathf.Lerp(VandiConfig.AddIntervalEnd.Value, VandiConfig.AddIntervalStart.Value, health);
            _nextAdd = Time.time + gap;

            _adds.RemoveAll(a => a == null || a.IsDead());
            if (_adds.Count >= VandiConfig.AddMaxAlive.Value) return;

            ZNetScene scene = ZNetScene.instance;
            GameObject prefab = scene == null ? null : scene.GetPrefab(Skeleton);
            if (prefab == null) return;

            Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(VandiConfig.AddRingMin.Value, VandiConfig.AddRingMax.Value);
            Vector3 at = transform.position + new Vector3(ring.x, 0f, ring.y);

            float height;
            if (!ZoneSystem.instance.GetSolidHeight(at, out height)) return;

            at.y = height + 0.5f;

            GameObject add = Object.Instantiate(prefab, at, Quaternion.LookRotation(target.transform.position - at));
            Mark(add);

            BaseAI ai = add.GetComponent<BaseAI>();
            if (ai != null) ai.SetHuntPlayer(true);

            Character character = add.GetComponent<Character>();
            if (character != null) _adds.Add(character);

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss special: add sent in, " + _adds.Count + " alive, next in " + gap.ToString("0.0") + "s at "
                    + (health * 100f).ToString("0") + "% health.");
        }

        /// <summary>Makes a thing the boss summoned: tied to the boss, and worth no loot.</summary>
        private void Mark(GameObject add)
        {
            ZNetView view = add.GetComponent<ZNetView>();
            if (view != null && view.IsValid()) view.GetZDO().Set(AddKey, _nview.GetZDO().m_uid);

            CharacterDrop drops = add.GetComponent<CharacterDrop>();
            if (drops != null) drops.SetDropsEnabled(false);
        }

        private float _said;

        private void Why(string reason)
        {
            if (!VandiConfig.Verbose.Value || Time.time < _said) return;

            _said = Time.time + 5f;
            VandiPlugin.Log.LogInfo("Boss special: waiting, " + reason + ".");
        }

        private IEnumerator Fan(Character target)
        {
            float rear = VandiConfig.FanRearSeconds.Value;

            for (float t = 0f; t < rear; t += Time.deltaTime)
            {
                _pose = Mathf.SmoothStep(0f, 1f, t / rear);
                yield return null;
            }

            _pose = 1f;

            int count = VandiConfig.FanSkeletons.Value;
            float span = VandiConfig.FanSeconds.Value;

            for (int i = 0; i < count; i++)
            {
                if (_boss == null || _boss.IsDead()) break;

                Throw(target, i, count);
                yield return new WaitForSeconds(span / count);
            }

            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                _pose = 1f - t / 0.6f;
                yield return null;
            }

            _pose = 0f;
            SpecialLock.Release(_boss);
        }

        private void Throw(Character target, int index, int count)
        {
            ZNetScene scene = ZNetScene.instance;
            GameObject prefab = scene == null ? null : scene.GetPrefab(Skeleton);
            if (prefab == null)
            {
                VandiPlugin.Log.LogWarning("Boss special: no '" + Skeleton + "' prefab to throw.");
                return;
            }

            Vector3 toward = target.transform.position - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude < 0.01f) toward = transform.forward;

            float spread = VandiConfig.FanSpread.Value;
            float angle = count <= 1 ? 0f : Mathf.Lerp(-spread, spread, index / (float)(count - 1));
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * toward.normalized;

            Vector3 from = transform.position + Vector3.up * 3.5f + dir * 2f;
            GameObject thrown = Object.Instantiate(prefab, from, Quaternion.LookRotation(dir));

            Mark(thrown);

            Rigidbody body = thrown.GetComponent<Rigidbody>();
            if (body != null) body.velocity = dir * VandiConfig.FanThrowSpeed.Value + Vector3.up * 6f;

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss special: skeleton " + (index + 1) + "/" + count + " thrown at " + angle + " degrees.");
        }

        // After the animator has posed the frame, because a bone written before it is overwritten
        // by it. Each bone is turned from where the animator left it this frame, so the boss keeps
        // moving underneath the pose. Written only while there is something to show, so a boss at rest is the game's.
        private void LateUpdate()
        {
            if (_pose <= 0.001f) return;

            if (_spine2 != null) _spine2.localRotation = _spine2.localRotation * Quaternion.Euler(-25f * _pose, 0f, 0f);
            if (_spine3 != null) _spine3.localRotation = _spine3.localRotation * Quaternion.Euler(-30f * _pose, 0f, 0f);
            if (_lArm != null) _lArm.localRotation = _lArm.localRotation * Quaternion.Euler(0f, 0f, 70f * _pose);
            if (_rArm != null) _rArm.localRotation = _rArm.localRotation * Quaternion.Euler(0f, 0f, -70f * _pose);
        }

        // Humanoid.Start, not Character.Start: Humanoid overrides it without calling base, so a boss
        // (a Humanoid) never runs the base one and a patch there is a patch that never fires.
        [HarmonyPatch(typeof(Humanoid), "Start")]
        [HarmonyPostfix]
        private static void Started(Character __instance)
        {
            if (__instance == null || !Bosses.IsBossKey(Bosses.KeyOf(__instance))) return;
            if (Bosses.KeyOf(__instance) != "defeated_bonemass") return;
            if (__instance.GetComponent<FanSpecial>() != null) return;

            __instance.gameObject.AddComponent<FanSpecial>();

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss special: attached to " + __instance.name + ".");
        }
    }

    /// <summary>
    /// A boss's own adds hurt it when they die, up to a cap.
    ///
    /// The kill is seen by the add's owner, in a prefix, because by the time a postfix runs the
    /// owner's ZDO is gone (the same reason Summon.Dying is a prefix). But the boss may be owned by
    /// a different machine, and only a boss's owner may write its health or its running total, so
    /// the add's owner sends the boss's owner a routed message and that machine does the chip. In
    /// singleplayer the two are the same machine and the message is to itself.
    ///
    /// <b>The total is capped.</b> What adds take off is held to AddChipCap of the bar, whatever
    /// the count, so the adds are a way to shorten the fight and never a way around it. The
    /// total is kept on the boss's own ZDO so it survives a change of owner.
    /// </summary>
    internal static class BossAdds
    {
        private const string ChipRpc = "Vandi_Chip";
        private const string ChippedKey = "vandi_chipped";

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void Register()
        {
            if (ZRoutedRpc.instance != null)
                ZRoutedRpc.instance.Register<ZDOID, float>(ChipRpc, Chip);
        }

        [HarmonyPatch(typeof(Character), "OnDeath")]
        [HarmonyPrefix]
        private static void Dying(Character __instance)
        {
            if (!VandiConfig.Enabled.Value || !VandiConfig.BossSpecials.Value || __instance == null) return;

            ZNetView nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

            ZDOID boss = nview.GetZDO().GetZDOID(FanSpecial.AddKey);
            if (boss == ZDOID.None) return;

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss add " + __instance.name + " died, boss " + boss + ".");

            ZDO bossZdo = ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(boss);
            if (bossZdo == null) return;

            ZRoutedRpc.instance.InvokeRoutedRPC(bossZdo.GetOwner(), ChipRpc, boss,
                VandiConfig.AddChipPercent.Value / 100f);
        }

        private static void Chip(long sender, ZDOID bossId, float fraction)
        {
            ZDO zdo = ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(bossId);
            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Chip message for " + bossId + ": zdo " + (zdo != null) + ", owner " + (zdo != null && zdo.IsOwner()) + ".");

            if (zdo == null || !zdo.IsOwner() || ZNetScene.instance == null) return;

            GameObject go = ZNetScene.instance.FindInstance(bossId);
            Character boss = go == null ? null : go.GetComponent<Character>();
            if (boss == null || boss.IsDead()) return;

            float max = boss.GetMaxHealth();
            float taken = zdo.GetFloat(ChippedKey, 0f);
            float room = max * VandiConfig.AddChipCapPercent.Value / 100f - taken;
            float amount = Mathf.Min(max * fraction, room);
            if (amount <= 0f) return;

            // Never to nothing: the chip shortens the fight, the player ends it.
            boss.SetHealth(Mathf.Max(boss.GetHealth() - amount, 1f));
            zdo.Set(ChippedKey, taken + amount);

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss add died: " + boss.name + " lost " + amount.ToString("0") + " health, "
                    + (taken + amount).ToString("0") + " of at most " + (room + taken).ToString("0") + " so far.");
        }
    }
}

using System.Collections;
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
        internal static readonly int AddKey = "vandi_boss_add".GetStableHashCode();

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

        private void Update()
        {
            if (!VandiConfig.Enabled.Value || !VandiConfig.BossSpecials.Value) return;
            if (_boss == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            if (_boss.IsDead() || Time.time < _next || _ai == null) return;
            if (!_ai.IsAlerted()) { Why("not alerted"); return; }
            if (_boss.GetLevel() < 2) { Why("level " + _boss.GetLevel()); return; }
            if (_nview.GetZDO().GetLong("vandi_summoner".GetStableHashCode(), 0L) == 0L) { Why("no summoner"); return; }

            Character target = _ai.GetTargetCreature();
            if (target == null) { Why("no target"); return; }

            _next = Time.time + VandiConfig.FanCooldown.Value;
            StartCoroutine(Fan(target));
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

            ZNetView view = thrown.GetComponent<ZNetView>();
            if (view != null && view.IsValid()) view.GetZDO().Set(AddKey, _nview.GetZDO().m_uid.ID);

            CharacterDrop drops = thrown.GetComponent<CharacterDrop>();
            if (drops != null) drops.SetDropsEnabled(false);

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
}

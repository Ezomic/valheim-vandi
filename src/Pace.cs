using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// A summoned boss with stars moves and swings a little faster for each one.
    ///
    /// Two levers, chosen from the game's own code. Movement is the Character's speed fields
    /// (walk, run, swim and the two flight speeds): the AI asks for a direction and
    /// <c>UpdateWalking</c> multiplies the field in, so a larger field is a faster boss and
    /// nothing else changes. Attacks are the Animator's speed: the swing, its hit event and the
    /// recovery all play faster together, so the telegraph is shorter by the same fraction.
    /// <c>m_speedFactor</c> on the attack was rejected, it only scales how far the boss slides
    /// while swinging, and a cooldown was rejected because MonsterAI owns it.
    ///
    /// The animator is only touched while the boss is attacking and the game has it at exactly
    /// 1. CharacterAnimEvent puts it back to 1 itself the moment the attack ends, and an
    /// animation event that sets its own speed (a slow wind-up) is left alone rather than
    /// overwritten. Other animations, walk included, are never retimed.
    ///
    /// <b>Only the machine that owns the boss writes anything.</b> Every instance carries the
    /// component, and the stars are read from the level on the ZDO every frame, so a reload or
    /// an owner change needs no hand-over: the new owner finds the level and applies it, and a
    /// machine that stops owning the boss puts the base speeds back. ZSyncAnimation already
    /// copies the owner's animator speed to everybody else.
    ///
    /// Turn speed is deliberately left alone, so a faster boss is still outmanoeuvred.
    /// </summary>
    internal sealed class BossPace : MonoBehaviour
    {
        private Character _boss;
        private ZNetView _nview;
        private Animator _animator;
        private string _key;

        private float _speed, _walk, _run, _swim, _flySlow, _flyFast;

        private float _move = 1f, _attack = 1f;
        private bool _swinging, _told, _raised;
        private float _raisedTo;

        private void Awake()
        {
            _boss = GetComponent<Character>();
            _nview = GetComponent<ZNetView>();
            _animator = GetComponentInChildren<Animator>();
            _key = Bosses.KeyOf(_boss);

            _speed = _boss.m_speed;
            _walk = _boss.m_walkSpeed;
            _run = _boss.m_runSpeed;
            _swim = _boss.m_swimSpeed;
            _flySlow = _boss.m_flySlowSpeed;
            _flyFast = _boss.m_flyFastSpeed;
        }

        private void Update()
        {
            if (_boss == null || _nview == null || !_nview.IsValid()) return;

            bool owner = _nview.IsOwner() && !_boss.IsDead();
            int stars = owner ? Stars() : 0;
            float move = 1f, attack = 1f;
            if (stars > 0) Factors(_key, stars, out move, out attack);

            bool changed = !Mathf.Approximately(move, _move) || !Mathf.Approximately(attack, _attack);
            if (changed)
            {
                _move = move;
                _attack = attack;
                Apply(move);
            }

            if (owner && (changed || !_told))
            {
                _told = true;

                if (VandiConfig.Verbose.Value)
                    VandiPlugin.Log.LogInfo(stars > 0
                        ? "Boss pace: " + Name() + " at " + stars + " star(s), move x" + move.ToString("0.000", CultureInfo.InvariantCulture)
                            + ", attack x" + attack.ToString("0.000", CultureInfo.InvariantCulture) + "."
                        : "Boss pace: " + Name() + " at 0 star(s), left alone.");
            }

            Swing(attack, owner);
        }

        private string Name() { return Utils.GetPrefabName(gameObject); }

        private int Stars()
        {
            if (!VandiConfig.Enabled.Value || !VandiConfig.BossPace.Value) return 0;

            ZDO zdo = _nview.GetZDO();
            if (zdo.GetLong(Summon.SummonerKey, 0L) == 0L) return 0;

            int level = zdo.GetInt(ZDOVars.s_level, 1);
            int stars = level - 1;
            int cap = VandiConfig.BossStarCap.Value;

            return stars > cap ? cap : stars < 0 ? 0 : stars;
        }

        private void Apply(float move)
        {
            _boss.m_speed = _speed * move;
            _boss.m_walkSpeed = _walk * move;
            _boss.m_runSpeed = _run * move;
            _boss.m_swimSpeed = _swim * move;
            _boss.m_flySlowSpeed = _flySlow * move;
            _boss.m_flyFastSpeed = _flyFast * move;
        }

        private void Swing(float attack, bool owner)
        {
            bool attacking = owner && _animator != null && Mathf.Abs(attack - 1f) > 0.0001f && _boss.InAttack();
            if (!attacking)
            {
                _swinging = false;
                Lower();
                return;
            }

            if (!Mathf.Approximately(_animator.speed, 1f)) return;

            _animator.speed = attack;
            _raised = true;
            _raisedTo = attack;

            if (!_swinging && VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss pace: " + Name() + " swings at animator speed " + attack.ToString("0.000", CultureInfo.InvariantCulture) + ".");

            _swinging = true;
        }

        // An attack cut short (a stagger, a death) or a boss handed to another machine mid-swing
        // does not always pass through the game's own reset to 1, so the speed this wrote is
        // taken back here, and only when it is still the value this wrote.
        private void Lower()
        {
            if (!_raised) return;

            _raised = false;
            if (_animator != null && Mathf.Approximately(_animator.speed, _raisedTo)) _animator.speed = 1f;
        }

        private static string _parsedFrom;
        private static readonly Dictionary<string, float[]> Overrides = new Dictionary<string, float[]>();

        /// <summary>
        /// The speed multipliers for a boss at this many stars: one plus the per-star percent, times
        /// the boss's own override of it. Its own method so the scenario and the log read the
        /// number the boss runs on.
        /// </summary>
        internal static void Factors(string key, int stars, out float move, out float attack)
        {
            float moveScale = 1f, attackScale = 1f;

            Parse();
            float[] pair;
            if (key != null && Overrides.TryGetValue(key, out pair)) { moveScale = pair[0]; attackScale = pair[1]; }

            move = 1f + stars * VandiConfig.BossMovePercentPerStar.Value * moveScale / 100f;
            attack = 1f + stars * VandiConfig.BossAttackPercentPerStar.Value * attackScale / 100f;
        }

        private static void Parse()
        {
            string raw = VandiConfig.BossPaceOverrides.Value ?? "";
            if (raw == _parsedFrom) return;

            _parsedFrom = raw;
            Overrides.Clear();

            foreach (string entry in raw.Split(','))
            {
                string[] part = entry.Trim().ToLowerInvariant().Split(':');
                float move, attack;
                if (part.Length != 3 || part[0].Length == 0
                    || !float.TryParse(part[1], NumberStyles.Float, CultureInfo.InvariantCulture, out move)
                    || !float.TryParse(part[2], NumberStyles.Float, CultureInfo.InvariantCulture, out attack))
                {
                    if (entry.Trim().Length > 0)
                        VandiPlugin.Log.LogWarning("BossPaceOverrides: '" + entry.Trim() + "' is not boss:moveMultiplier:attackMultiplier, ignored.");
                    continue;
                }

                Overrides[part[0]] = new[] { move < 0f ? 0f : move, attack < 0f ? 0f : attack };
            }
        }

        // Humanoid.Start, not Character.Start: Humanoid overrides it without calling base, so a boss
        // never runs the base one and a patch there never fires.
        [HarmonyPatch(typeof(Humanoid), "Start")]
        [HarmonyPostfix]
        private static void Started(Character __instance)
        {
            if (__instance == null || !Bosses.IsBossKey(Bosses.KeyOf(__instance))) return;
            if (__instance.GetComponent<BossPace>() != null) return;

            __instance.gameObject.AddComponent<BossPace>();
        }
    }
}

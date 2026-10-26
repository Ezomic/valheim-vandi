using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// One boss's own move, past the first (the Bonemass fan in Special.cs is the model).
    ///
    /// Every special is two halves. <see cref="Run"/> is the decision, and happens only on the
    /// machine that owns the boss: it picks the spot, sends the warning, waits it out, then does
    /// the damage or the spawning. <see cref="Show"/> is what everybody sees, owner included:
    /// the warning drawn on the ground and the effect that follows. The owner sends the warning
    /// first and acts afterwards, so nothing here can hurt a player the warning never reached.
    /// </summary>
    internal abstract class BossSpecial
    {
        internal abstract int Id { get; }
        internal abstract string Name { get; }
        internal abstract string BossKey { get; }
        internal abstract bool On { get; }
        internal abstract float FirstDelay { get; }
        internal abstract float Cooldown { get; }

        /// <summary>How far the target may be for the move to start, so it is never wasted on a far fight.</summary>
        internal virtual float Reach { get { return 45f; } }

        internal abstract void Bind(ConfigFile cfg);
        internal abstract IEnumerator Run(SpecialRun run);
        internal abstract IEnumerator Show(SpecialCall call);
    }

    /// <summary>What travels from the owner to every screen. Four floats and a count are enough for every move.</summary>
    internal struct SpecialCall
    {
        internal int Id, Phase, N;
        internal ZDOID BossId, TargetId;
        internal Vector3 At, Aim;
        internal float A, B, C, D;
        internal Character Boss, Target;
    }

    /// <summary>Shared knobs: the quiet after any move, and one scale over every special's damage.</summary>
    internal static class SpecialConfig
    {
        internal static ConfigEntry<float> Gap, DamageScale;

        internal static void Bind(ConfigFile cfg)
        {
            Gap = cfg.Bind("Specials", "SpecialGap", 10f,
                "Seconds of quiet after any special move ends before the same boss may start another. "
                + "Only one move runs at a time whatever this says; this is the breathing room after it.");

            DamageScale = cfg.Bind("Specials", "SpecialDamageScale", 1f,
                "Multiplier on the damage of every special move at once, for tuning a whole fight without "
                + "touching each move. 0 makes them harmless and leaves every warning in place.");
        }
    }

    /// <summary>
    /// The moves, in one list. A new boss move is a new class and one line here; the config is
    /// bound from the same place, so the .cfg and the list cannot disagree.
    /// </summary>
    internal static class SpecialRegistry
    {
        internal static readonly BossSpecial[] All =
        {
            new BogRingSpecial(),
            new RootWaveSpecial(),
            new MeteorSpecial(),
            new SeekerSpiralSpecial(),
            new IceRingSpecial(),
            new ChargeSpecial(),
            new SpokesSpecial(),
        };

        internal static void Bind(ConfigFile cfg)
        {
            SpecialConfig.Bind(cfg);
            foreach (BossSpecial special in All) special.Bind(cfg);
        }

        internal static List<BossSpecial> For(string bossKey)
        {
            List<BossSpecial> mine = new List<BossSpecial>();
            if (string.IsNullOrEmpty(bossKey)) return mine;

            foreach (BossSpecial special in All)
                if (special.BossKey == bossKey) mine.Add(special);

            return mine;
        }

        internal static BossSpecial ById(int id)
        {
            foreach (BossSpecial special in All)
                if (special.Id == id) return special;

            return null;
        }
    }

    /// <summary>
    /// Which bosses are in the middle of a move. Shared with the Bonemass fan, which is its own
    /// component: a boss does one thing at a time, and two components that each thought it was
    /// free would stack a ring on top of a throw.
    /// </summary>
    internal static class SpecialLock
    {
        private static readonly HashSet<int> Held = new HashSet<int>();

        internal static bool IsHeld(Character boss) { return boss != null && Held.Contains(boss.GetInstanceID()); }

        internal static bool TryTake(Character boss)
        {
            return boss != null && Held.Add(boss.GetInstanceID());
        }

        internal static void Release(Character boss)
        {
            if (boss != null) Held.Remove(boss.GetInstanceID());
        }
    }

    /// <summary>What a move's decision half is handed: the boss, its target, and the way to talk to everyone.</summary>
    internal sealed class SpecialRun
    {
        internal readonly Character Boss;
        internal readonly Character Target;
        internal readonly BaseAI Ai;
        internal readonly ZNetView View;
        internal readonly SpecialRunner Runner;
        internal readonly BossSpecial Special;

        internal SpecialRun(SpecialRunner runner, BossSpecial special, Character target)
        {
            Runner = runner;
            Special = special;
            Boss = runner.Boss;
            Ai = runner.GetComponent<BaseAI>();
            View = runner.GetComponent<ZNetView>();
            Target = target;
        }

        /// <summary>False once the boss is gone, dead, or owned by another machine, which ends the move.</summary>
        internal bool Alive
        {
            get { return Boss != null && !Boss.IsDead() && View != null && View.IsValid() && View.IsOwner(); }
        }

        internal Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>Direction from the boss to the target on the ground, or the boss's facing when they overlap.</summary>
        internal Vector3 Toward(Character target)
        {
            Vector3 d = Flat(target.transform.position - Boss.transform.position);
            return d.sqrMagnitude < 0.01f ? Flat(Boss.transform.forward).normalized : d.normalized;
        }

        internal void Send(SpecialCall call)
        {
            call.Id = Special.Id;
            call.BossId = View.GetZDO().m_uid;
            SpecialNet.Send(call);

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss special " + Special.Name + ": warning sent, phase " + call.Phase + " at "
                    + call.At.ToString("0") + ".");
        }

        /// <summary>A thing the boss made: tied to the boss so its death chips it, and worth no loot.</summary>
        internal void Mark(GameObject add)
        {
            ZNetView view = add.GetComponent<ZNetView>();
            if (view != null && view.IsValid()) view.GetZDO().Set(FanSpecial.AddKey, View.GetZDO().m_uid);

            CharacterDrop drops = add.GetComponent<CharacterDrop>();
            if (drops != null) drops.SetDropsEnabled(false);
        }

        /// <summary>Waits out a warning, and stops early if the boss is gone.</summary>
        internal Coroutine Pause(float seconds)
        {
            return Runner.StartCoroutine(Wait(seconds));
        }

        private IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (!Alive) yield break;
                yield return null;
            }
        }

        internal void Hit(Player player, float damage, HitData.DamageType type, Vector3 from, float push, string what)
        {
            damage *= SpecialConfig.DamageScale.Value;
            if (damage <= 0f) return;

            HitData hit = new HitData();
            hit.m_hitType = HitData.HitType.EnemyHit;
            hit.m_point = player.GetCenterPoint();
            hit.m_dir = Flat(player.transform.position - from).normalized;
            hit.m_pushForce = push;
            hit.m_dodgeable = true;
            hit.m_blockable = false;

            switch (type)
            {
                case HitData.DamageType.Fire: hit.m_damage.m_fire = damage; break;
                case HitData.DamageType.Frost: hit.m_damage.m_frost = damage; break;
                case HitData.DamageType.Lightning: hit.m_damage.m_lightning = damage; break;
                case HitData.DamageType.Poison: hit.m_damage.m_poison = damage; break;
                case HitData.DamageType.Pierce: hit.m_damage.m_pierce = damage; break;
                default: hit.m_damage.m_blunt = damage; break;
            }

            hit.SetAttacker(Boss);
            player.Damage(hit);

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss special " + Special.Name + ": " + what + " hit " + player.GetPlayerName()
                    + " for " + damage.ToString("0") + " " + type + ".");
        }

        /// <summary>A player the ground can hurt: alive, on their feet on the ground (a jump clears it), not a ghost.</summary>
        internal static bool Grounded(Player p)
        {
            return p != null && !p.IsDead() && !p.InGhostMode() && p.IsOnGround();
        }

        internal static bool Reachable(Player p)
        {
            return p != null && !p.IsDead() && !p.InGhostMode();
        }

        internal static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        internal static Vector3 OnGround(Vector3 p)
        {
            float h;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out h)) p.y = h;
            return p;
        }
    }

    /// <summary>
    /// The component on each boss that has a move. It watches the way FanSpecial does and never
    /// touches the vanilla AI, except that a move may borrow the boss for a few seconds.
    ///
    /// <b>Gate, in one place for every move:</b> only the owning machine, only an alerted boss with
    /// a target, only at one star or more, only a boss somebody summoned. A boss at level 1 never
    /// starts one, and says why in the log when Verbose is on.
    /// </summary>
    internal sealed class SpecialRunner : MonoBehaviour
    {
        internal Character Boss { get; private set; }

        private ZNetView _nview;
        private BaseAI _ai;
        private List<BossSpecial> _mine;
        private float[] _next;
        private float _quiet;
        private float _said;

        private void Awake()
        {
            Boss = GetComponent<Character>();
            _nview = GetComponent<ZNetView>();
            _ai = GetComponent<BaseAI>();
            _mine = SpecialRegistry.For(Bosses.KeyOf(Boss));
            _next = new float[_mine.Count];

            for (int i = 0; i < _mine.Count; i++) _next[i] = Time.time + _mine[i].FirstDelay;
        }

        private void Update()
        {
            if (!VandiConfig.Enabled.Value || !VandiConfig.BossSpecials.Value) return;
            if (Boss == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            if (Boss.IsDead() || _ai == null) return;
            if (!_ai.IsAlerted()) { Why("not alerted"); return; }
            if (Boss.GetLevel() < 2) { Why("level " + Boss.GetLevel()); return; }
            if (_nview.GetZDO().GetLong("vandi_summoner".GetStableHashCode(), 0L) == 0L) { Why("no summoner"); return; }

            Character target = _ai.GetTargetCreature();
            if (target == null) { Why("no target"); return; }
            if (SpecialLock.IsHeld(Boss) || Time.time < _quiet) return;

            for (int i = 0; i < _mine.Count; i++)
            {
                BossSpecial special = _mine[i];
                if (!special.On || Time.time < _next[i]) continue;
                if (SpecialRun.FlatDistance(target.transform.position, transform.position) > special.Reach) continue;
                if (!SpecialLock.TryTake(Boss)) return;

                StartCoroutine(Go(special, i, target));
                return;
            }
        }

        private IEnumerator Go(BossSpecial special, int index, Character target)
        {
            SpecialRun run = new SpecialRun(this, special, target);

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss special " + special.Name + ": starting on " + Boss.name + " at level "
                    + Boss.GetLevel() + ".");

            try
            {
                yield return StartCoroutine(special.Run(run));
            }
            finally
            {
                SpecialLock.Release(Boss);
                _next[index] = Time.time + special.Cooldown;
                _quiet = Time.time + SpecialConfig.Gap.Value;
            }
        }

        private void Why(string reason)
        {
            if (!VandiConfig.Verbose.Value || Time.time < _said) return;

            _said = Time.time + 5f;
            VandiPlugin.Log.LogInfo("Boss specials: waiting, " + reason + ".");
        }

        private void OnDestroy()
        {
            SpecialLock.Release(Boss);
        }

        // Humanoid.Start, not Character.Start, for the reason FanSpecial's own patch gives.
        [HarmonyPatch(typeof(Humanoid), "Start")]
        [HarmonyPostfix]
        private static void Started(Character __instance)
        {
            if (__instance == null) return;

            string key = Bosses.KeyOf(__instance);
            if (!Bosses.IsBossKey(key) || SpecialRegistry.For(key).Count == 0) return;
            if (__instance.GetComponent<SpecialRunner>() != null) return;

            __instance.gameObject.AddComponent<SpecialRunner>();

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss specials: attached to " + __instance.name + ".");
        }
    }

    /// <summary>
    /// The warning and the effects travel as one routed message to everybody, the owner included,
    /// so the owner draws exactly what the others do. The message carries only where and how
    /// long; nothing is decided on a receiving machine.
    /// </summary>
    internal static class SpecialNet
    {
        private const string Rpc = "Vandi_Special";

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void Register()
        {
            if (ZRoutedRpc.instance != null)
                ZRoutedRpc.instance.Register<ZPackage>(Rpc, Receive);
        }

        internal static void Send(SpecialCall c)
        {
            if (ZRoutedRpc.instance == null) return;

            ZPackage pkg = new ZPackage();
            pkg.Write(c.Id);
            pkg.Write(c.Phase);
            pkg.Write(c.N);
            pkg.Write(c.BossId);
            pkg.Write(c.TargetId);
            pkg.Write(c.At);
            pkg.Write(c.Aim);
            pkg.Write(c.A);
            pkg.Write(c.B);
            pkg.Write(c.C);
            pkg.Write(c.D);

            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, pkg);
        }

        private static void Receive(long sender, ZPackage pkg)
        {
            SpecialCall c = new SpecialCall();
            c.Id = pkg.ReadInt();
            c.Phase = pkg.ReadInt();
            c.N = pkg.ReadInt();
            c.BossId = pkg.ReadZDOID();
            c.TargetId = pkg.ReadZDOID();
            c.At = pkg.ReadVector3();
            c.Aim = pkg.ReadVector3();
            c.A = pkg.ReadSingle();
            c.B = pkg.ReadSingle();
            c.C = pkg.ReadSingle();
            c.D = pkg.ReadSingle();

            BossSpecial special = SpecialRegistry.ById(c.Id);
            if (special == null || ZNetScene.instance == null) return;

            c.Boss = Find(c.BossId);
            c.Target = Find(c.TargetId);

            FxHost.Run(special.Show(c));
        }

        private static Character Find(ZDOID id)
        {
            if (id == ZDOID.None) return null;

            GameObject go = ZNetScene.instance.FindInstance(id);
            return go == null ? null : go.GetComponent<Character>();
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// Eikthyr's charge. He picks you out and a line runs from him to you, following you for a
    /// moment. Then the line locks, with a ring marked where the charge will end, and after a
    /// short beat he runs the line and a burst of lightning goes off in the ring.
    ///
    /// The run is the game's own: while the move lasts his AI is switched off and he is walked
    /// along the locked line at his run animation and run speed. Nothing is played from a file.
    /// </summary>
    internal sealed class ChargeSpecial : BossSpecial
    {
        private static ConfigEntry<bool> _on;
        private static ConfigEntry<float> _first, _cooldown, _track, _lock, _length, _width, _burst, _damage, _burstDamage, _push, _minRange;

        private static readonly Dictionary<ZDOID, float> LockedAt = new Dictionary<ZDOID, float>();
        private static readonly Dictionary<ZDOID, float> BurstAt = new Dictionary<ZDOID, float>();

        internal override int Id { get { return 6; } }
        internal override string Name { get { return "charge"; } }
        internal override string BossKey { get { return "defeated_eikthyr"; } }
        internal override bool On { get { return _on.Value; } }
        internal override float FirstDelay { get { return _first.Value; } }
        internal override float Cooldown { get { return _cooldown.Value; } }

        internal override void Bind(ConfigFile cfg)
        {
            const string s = "EikthyrCharge";

            _on = SpecialConfig.Bind(cfg, s, "Enabled", true, "A starred Eikthyr picks you out, follows you with a marked line, locks it, then charges along "
                + "it and ends in a burst of lightning. Off removes this move.");
            _first = SpecialConfig.Bind(cfg, s, "FirstDelay", 25f, "Seconds from the boss appearing until its first charge.");
            _cooldown = SpecialConfig.Bind(cfg, s, "Cooldown", 35f, "Seconds between one charge ending and the next being allowed.");
            _track = SpecialConfig.Bind(cfg, s, "TrackSeconds", 1.8f, "How long the marked line follows you before it locks. After it locks it never moves again.");
            _lock = SpecialConfig.Bind(cfg, s, "LockSeconds", 0.9f, "How long the locked line and the burst ring are shown before he runs. This is the time to "
                + "step off the line, and it is not shortened by anything.");
            _length = SpecialConfig.Bind(cfg, s, "Length", 24f, "How far the charge runs, in metres, and so where the burst is.");
            _width = SpecialConfig.Bind(cfg, s, "Width", 3.5f, "Width of the marked line in metres; anyone in it as he passes is hit.");
            _burst = SpecialConfig.Bind(cfg, s, "BurstRadius", 6f, "Radius of the lightning burst at the end of the line, in metres.");
            _damage = SpecialConfig.Bind(cfg, s, "ChargeDamage", 45f, "Blunt damage to a player he runs through.");
            _burstDamage = SpecialConfig.Bind(cfg, s, "BurstDamage", 40f, "Lightning damage to a player inside the ring when the burst goes off.");
            _push = SpecialConfig.Bind(cfg, s, "Knockback", 18f, "Push on a player the charge hits.");
            _minRange = SpecialConfig.Bind(cfg, s, "MinimumDistance", 8f, "He will not start a charge at a target closer than this, so it never begins on top of you.");
        }

        internal override IEnumerator Run(SpecialRun run)
        {
            if (!Ground.CanDraw) yield break;
            if (SpecialRun.FlatDistance(run.Target.transform.position, run.Boss.transform.position) < _minRange.Value) yield break;

            ZNetView targetView = run.Target.GetComponent<ZNetView>();
            ZDOID target = targetView != null && targetView.IsValid() ? targetView.GetZDO().m_uid : ZDOID.None;

            run.Send(new SpecialCall { Phase = 0, TargetId = target, A = _track.Value });
            yield return run.Pause(_track.Value);
            if (!run.Alive) yield break;

            Vector3 start = run.Boss.transform.position;
            Vector3 dir = run.Toward(run.Target);
            float length = _length.Value, width = _width.Value, burst = _burst.Value;
            Vector3 end = SpecialRun.OnGround(start + dir * length);

            run.Send(new SpecialCall { Phase = 1, At = start, Aim = dir, A = length, B = _lock.Value, C = width, D = burst });

            Character boss = run.Boss;
            BaseAI ai = run.Ai;

            try
            {
                ai.enabled = false;
                boss.SetMoveDir(Vector3.zero);
                boss.SetLookDir(dir);

                yield return run.Pause(_lock.Value + SpecialRun.Lag);
                if (!run.Alive) yield break;

                HashSet<Player> hit = new HashSet<Player>();
                float began = Time.time, stuckFor = 0f, last = 0f;
                float limit = length / Mathf.Max(2f, boss.m_runSpeed) * 3f + 1f;

                while (run.Alive && Time.time - began < limit)
                {
                    yield return new WaitForFixedUpdate();

                    boss.SetLookDir(dir);
                    boss.SetMoveDir(dir);
                    boss.SetRun(true);

                    float went = Vector3.Dot(boss.transform.position - start, dir);
                    if (went >= length - 1f) break;

                    stuckFor = went - last < 0.02f ? stuckFor + Time.fixedDeltaTime : 0f;
                    last = went;
                    if (stuckFor > 0.5f) break;

                    foreach (Player p in Player.GetAllPlayers())
                    {
                        if (hit.Contains(p) || !SpecialRun.Grounded(p)) continue;

                        Vector3 rel = p.transform.position - boss.transform.position;
                        float along = Vector3.Dot(rel, dir), across = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, dir)));
                        if (across > width * 0.5f || along < 0f || along > 3.5f) continue;

                        hit.Add(p);
                        run.Hit(p, _damage.Value, HitData.DamageType.Blunt, boss.transform.position, _push.Value, "charge");
                    }
                }
            }
            finally
            {
                if (boss != null)
                {
                    boss.SetMoveDir(Vector3.zero);
                    boss.SetRun(false);
                }

                if (ai != null) ai.enabled = true;
            }

            if (!run.Alive) yield break;

            run.Send(new SpecialCall { Phase = 2, At = end, A = burst });

            foreach (Player p in Player.GetAllPlayers())
            {
                if (!SpecialRun.Reachable(p) || SpecialRun.FlatDistance(p.transform.position, end) > burst) continue;

                run.Hit(p, _burstDamage.Value, HitData.DamageType.Lightning, end, _push.Value, "burst");
            }
        }

        internal override IEnumerator Show(SpecialCall c)
        {
            switch (c.Phase)
            {
                case 0: return Follow(c);
                case 1: return LockedLine(c);
                default: return Flash(c);
            }
        }

        private static IEnumerator Follow(SpecialCall c)
        {
            Drawn drawn = new Drawn();

            try
            {
                if (c.Boss == null || c.Target == null) yield break;

                Zone line = drawn.Add(Zone.Strip(c.Boss.transform.position, Vector3.forward, 1f, 3.5f, Ground.Danger));
                float began = Time.time;
                if (c.Boss != null) FxHost.Run(Ground.Glow(c.Boss, new Color(0.7f, 0.85f, 1f), c.A + 0.9f, 8f));

                while (Time.time - began < c.A)
                {
                    float seen;
                    if (c.Boss == null || c.Target == null || (LockedAt.TryGetValue(c.BossId, out seen) && seen >= began)) break;

                    Vector3 from = c.Boss.transform.position;
                    Vector3 to = c.Target.transform.position - from;
                    line.Move(from, to, Mathf.Min(to.magnitude, 40f));
                    line.Alpha(Ground.Danger.a * (0.7f + 0.3f * Mathf.Sin((Time.time - began) * 10f)));
                    yield return null;
                }
            }
            finally
            {
                drawn.Dispose();
            }
        }

        private static IEnumerator LockedLine(SpecialCall c)
        {
            LockedAt[c.BossId] = Time.time;
            Drawn drawn = new Drawn();

            try
            {
                Zone line = drawn.Add(Zone.Strip(c.At, c.Aim, c.A, c.C, Ground.Danger));
                Vector3 end = SpecialRun.OnGround(c.At + c.Aim * c.A);
                Zone spot = drawn.Add(Zone.Disc(end, c.D, new Color(0.5f, 0.7f, 1f, 0.35f)));
                Band edge = drawn.Add(Band.Ring(end, c.D, 0.35f, new Color(0.75f, 0.9f, 1f, 0.95f)));
                Band fill = drawn.Add(Band.Ring(end, 0.4f, 0.45f, new Color(0.75f, 0.9f, 1f, 0.95f)));
                Zone sweep = drawn.Add(Zone.Strip(c.At, c.Aim, 0.5f, c.C, Ground.DangerEdge));

                for (float t = 0f; t < c.B; t += Time.deltaTime)
                {
                    float k = Ground.Ease(t / c.B);
                    fill.Radius(Mathf.Max(0.4f, c.D * k));
                    sweep.Length(Mathf.Max(0.5f, c.A * k));
                    line.Alpha(Ground.Danger.a * (0.7f + 0.3f * Mathf.Sin(t * 12f)));
                    yield return null;
                }

                fill.Dispose();
                sweep.Dispose();

                float began = Time.time, limit = c.A / 4f + 3f, faint = Ground.Danger.a;
                float seen;
                while (Time.time - began < limit && !(BurstAt.TryGetValue(c.BossId, out seen) && seen >= began))
                {
                    faint = Mathf.Max(0.05f, faint - Time.deltaTime * 0.1f);
                    line.Alpha(faint);
                    yield return null;
                }

                edge.Alpha(0.3f);
                spot.Alpha(0.1f);
                yield return new WaitForSeconds(0.6f);
            }
            finally
            {
                drawn.Dispose();
            }
        }

        private static IEnumerator Flash(SpecialCall c)
        {
            BurstAt[c.BossId] = Time.time;
            Drawn drawn = new Drawn();

            try
            {
                Color white = new Color(0.8f, 0.9f, 1f, 1f);
                GameObject bolt = drawn.Lump(PrimitiveType.Cube, c.At + Vector3.up * 20f, new Vector3(0.5f, 40f, 0.5f), white);
                Band ring = drawn.Add(Band.Ring(c.At, 0.5f, 0.8f, new Color(0.7f, 0.85f, 1f, 0.9f)));
                FxHost.Run(Ground.Flash(c.At, new Color(0.7f, 0.85f, 1f), 0.5f, 10f, c.A * 3f));

                for (float t = 0f; t < 0.5f; t += Time.deltaTime)
                {
                    float k = t / 0.5f;
                    ring.Radius(Mathf.Max(0.5f, c.A * k));
                    ring.Alpha(0.9f * (1f - k));
                    if (bolt != null && k > 0.4f) Object.Destroy(bolt);
                    yield return null;
                }
            }
            finally
            {
                drawn.Dispose();
            }
        }
    }
}

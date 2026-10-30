using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// Fader's scorched spokes (the move Robbin left to the build to pick): the ground around him
    /// is marked in spokes like a wheel, one of them aimed at you, and fire erupts along every
    /// spoke from his feet outward. The safe ground is the wedge between two spokes, which widens
    /// the further out you stand, and the ring close around him where the spokes have not begun.
    /// </summary>
    internal sealed class SpokesSpecial : BossSpecial
    {
        private static ConfigEntry<bool> _on;
        private static ConfigEntry<float> _first, _cooldown, _warning, _length, _width, _inner, _speed, _damage, _push;
        private static ConfigEntry<int> _spokes;

        internal override int Id { get { return 7; } }
        internal override string Name { get { return "spokes"; } }
        internal override string BossKey { get { return "defeated_fader"; } }
        internal override bool On { get { return _on.Value; } }
        internal override float FirstDelay { get { return _first.Value; } }
        internal override float Cooldown { get { return _cooldown.Value; } }

        internal override void Bind(ConfigFile cfg)
        {
            const string s = "FaderSpokes";

            _on = SpecialConfig.Bind(cfg, s, "Enabled", true, "A starred Fader marks spokes of ground around him like a wheel, one aimed at you, then fire "
                + "erupts along them. Off removes this move.");
            _first = SpecialConfig.Bind(cfg, s, "FirstDelay", 40f, "Seconds from the boss appearing until its first set of spokes.");
            _cooldown = SpecialConfig.Bind(cfg, s, "Cooldown", 50f, "Seconds between one set ending and the next being allowed.");
            _spokes = SpecialConfig.Bind(cfg, s, "Spokes", 6, "Spokes in the wheel, evenly spaced. One is aimed at you when the warning starts.");
            _warning = SpecialConfig.Bind(cfg, s, "WarningSeconds", 3f, "How long the spokes are marked before the fire erupts.");
            _length = SpecialConfig.Bind(cfg, s, "Length", 26f, "How far each spoke reaches from him, in metres.");
            _width = SpecialConfig.Bind(cfg, s, "Width", 3.5f, "Width of each spoke in metres.");
            _inner = SpecialConfig.Bind(cfg, s, "InnerGap", 4f, "Where the spokes begin, in metres from him. Inside this the ground is clear, so a fighter "
                + "at his feet is not trapped by the overlap of every spoke at the centre.");
            _speed = SpecialConfig.Bind(cfg, s, "EruptionSpeed", 30f, "How fast the fire runs outward along the spokes, metres a second.");
            _damage = SpecialConfig.Bind(cfg, s, "Damage", 85f, "Fire damage to a player the eruption passes through.");
            _push = SpecialConfig.Bind(cfg, s, "Knockback", 15f, "Push on a player the fire hits.");
        }

        private static Vector3 Spoke(Vector3 first, int index, int count)
        {
            return Quaternion.Euler(0f, 360f * index / count, 0f) * first;
        }

        internal override IEnumerator Run(SpecialRun run)
        {
            if (!Ground.CanDraw) yield break;

            int count = Mathf.Max(2, _spokes.Value);
            Vector3 at = SpecialRun.OnGround(run.Boss.transform.position);
            Vector3 first = run.Toward(run.Target);
            float warning = _warning.Value, length = _length.Value, width = _width.Value, inner = _inner.Value, speed = _speed.Value;

            run.Send(new SpecialCall { At = at, Aim = first, N = count, Phase = Mathf.RoundToInt(speed), A = length, B = warning, C = width, D = inner });

            yield return run.Pause(warning + SpecialRun.Lag);
            if (!run.Alive) yield break;

            HashSet<Player> hit = new HashSet<Player>();

            for (float front = inner; front < length && run.Alive; front += speed * Time.deltaTime)
            {
                foreach (Player p in Player.GetAllPlayers())
                {
                    if (hit.Contains(p) || !SpecialRun.Grounded(p)) continue;

                    Vector3 rel = SpecialRun.OnGround(p.transform.position) - at;
                    rel.y = 0f;
                    float r = rel.magnitude;
                    if (r < inner || r > front) continue;

                    for (int i = 0; i < count; i++)
                    {
                        Vector3 dir = Spoke(first, i, count);
                        float along = Vector3.Dot(rel, dir), across = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, dir)));
                        if (along <= 0f || across > width * 0.5f) continue;

                        hit.Add(p);
                        run.Hit(p, _damage.Value, HitData.DamageType.Fire, at, _push.Value, "spoke");
                        break;
                    }
                }

                yield return null;
            }
        }

        internal override IEnumerator Show(SpecialCall c)
        {
            Drawn drawn = new Drawn();

            try
            {
                List<Zone> floors = new List<Zone>();
                List<Zone> fills = new List<Zone>();
                float inner = c.D, length = c.A - c.D;

                for (int i = 0; i < c.N; i++)
                {
                    Vector3 dir = Spoke(c.Aim, i, c.N);
                    Vector3 from = c.At + dir * inner;
                    floors.Add(drawn.Add(Zone.Strip(from, dir, length, c.C, Ground.Danger)));
                    fills.Add(drawn.Add(Zone.Strip(from, dir, 0.5f, c.C, Ground.DangerEdge)));
                }

                if (c.Boss != null) FxHost.Run(Ground.Glow(c.Boss, new Color(1f, 0.4f, 0.1f), c.B, 10f));

                for (float t = 0f; t < c.B; t += Time.deltaTime)
                {
                    float k = Ground.Ease(t / c.B), pulse = 0.7f + 0.3f * Mathf.Sin(t * 8f);
                    for (int i = 0; i < floors.Count; i++)
                    {
                        fills[i].Length(Mathf.Max(0.5f, length * k));
                        floors[i].Alpha(Ground.Danger.a * pulse);
                    }

                    yield return null;
                }

                foreach (Zone f in fills) f.Dispose();

                Color fire = new Color(1f, 0.5f, 0.1f, 0.85f);
                List<Zone> blaze = new List<Zone>();
                for (int i = 0; i < c.N; i++)
                {
                    Vector3 dir = Spoke(c.Aim, i, c.N);
                    blaze.Add(drawn.Add(Zone.Strip(c.At + dir * inner, dir, 0.5f, c.C, fire)));
                }

                FxHost.Run(Ground.Flash(c.At, new Color(1f, 0.5f, 0.15f), 0.8f, 8f, c.A));

                float run = 0.5f;
                for (float t = 0f; run < length; t += Time.deltaTime)
                {
                    run += Mathf.Max(1, c.Phase) * Time.deltaTime;
                    for (int i = 0; i < blaze.Count; i++) blaze[i].Length(Mathf.Min(run, length));
                    yield return null;
                }

                for (float t = 0f; t < 0.6f; t += Time.deltaTime)
                {
                    float k = 1f - t / 0.6f;
                    foreach (Zone b in blaze) b.Alpha(0.85f * k);
                    foreach (Zone f in floors) f.Alpha(Ground.Danger.a * k * 0.5f);
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

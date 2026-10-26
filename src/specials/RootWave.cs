using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// The Elder's root wave: a long strip of ground is marked in front of him, then the roots
    /// erupt along it from his feet outward in a rolling front. Step out sideways, or jump as the
    /// front passes.
    /// </summary>
    internal sealed class RootWaveSpecial : BossSpecial
    {
        private static ConfigEntry<bool> _on;
        private static ConfigEntry<float> _first, _cooldown, _warning, _length, _width, _speed, _front, _damage, _push;

        internal override int Id { get { return 2; } }
        internal override string Name { get { return "root wave"; } }
        internal override string BossKey { get { return "defeated_gdking"; } }
        internal override bool On { get { return _on.Value; } }
        internal override float FirstDelay { get { return _first.Value; } }
        internal override float Cooldown { get { return _cooldown.Value; } }

        internal override void Bind(ConfigFile cfg)
        {
            const string s = "ElderRootWave";

            _on = SpecialConfig.Bind(cfg, s, "Enabled", true, "A starred Elder marks a strip of ground toward you, then roots erupt along it in a wave. "
                + "Off removes this move and leaves his others.");
            _first = SpecialConfig.Bind(cfg, s, "FirstDelay", 30f, "Seconds from the boss appearing until its first wave.");
            _cooldown = SpecialConfig.Bind(cfg, s, "Cooldown", 40f, "Seconds between one wave ending and the next being allowed.");
            _warning = SpecialConfig.Bind(cfg, s, "WarningSeconds", 2.2f, "How long the strip stays marked before the roots move. The strip is aimed once, when the "
                + "warning starts, and does not follow you.");
            _length = SpecialConfig.Bind(cfg, s, "Length", 28f, "How far from the Elder the wave reaches, in metres.");
            _width = SpecialConfig.Bind(cfg, s, "Width", 4f, "Width of the marked strip in metres.");
            _speed = SpecialConfig.Bind(cfg, s, "WaveSpeed", 16f, "How fast the front of the wave travels, metres a second.");
            _front = SpecialConfig.Bind(cfg, s, "FrontDepth", 3f, "Depth of the front in metres: how much of the strip is dangerous at any moment. "
                + "With the speed this is how long a jump has to last.");
            _damage = SpecialConfig.Bind(cfg, s, "Damage", 50f, "Pierce damage to a player the front passes through on the ground.");
            _push = SpecialConfig.Bind(cfg, s, "Knockback", 12f, "Push on a player the roots hit.");
        }

        internal override IEnumerator Run(SpecialRun run)
        {
            if (!Ground.CanDraw) yield break;

            Vector3 dir = run.Toward(run.Target);
            Vector3 start = SpecialRun.OnGround(run.Boss.transform.position + dir * 3f);
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            float warning = _warning.Value, length = _length.Value, width = _width.Value;
            float speed = _speed.Value, depth = _front.Value;

            run.Send(new SpecialCall { At = start, Aim = dir, A = length, B = warning, C = speed, D = width });

            yield return run.Pause(warning + SpecialRun.Lag);
            if (!run.Alive) yield break;

            HashSet<Player> hit = new HashSet<Player>();

            for (float front = 0f; front < length + depth && run.Alive; front += speed * Time.deltaTime)
            {
                foreach (Player p in Player.GetAllPlayers())
                {
                    if (hit.Contains(p) || !SpecialRun.Grounded(p)) continue;

                    Vector3 rel = p.transform.position - start;
                    float along = Vector3.Dot(rel, dir), across = Mathf.Abs(Vector3.Dot(rel, right));
                    if (across > width * 0.5f || along < 0f || along > front || along < front - depth || along > length) continue;

                    hit.Add(p);
                    run.Hit(p, _damage.Value, HitData.DamageType.Pierce, start, _push.Value, "roots");
                }

                yield return null;
            }
        }

        internal override IEnumerator Show(SpecialCall c)
        {
            Drawn drawn = new Drawn();

            try
            {
                Vector3 right = Vector3.Cross(Vector3.up, c.Aim);
                Zone floor = drawn.Add(Zone.Strip(c.At, c.Aim, c.A, c.D, Ground.Danger));
                Zone fill = drawn.Add(Zone.Strip(c.At, c.Aim, 0.5f, c.D, Ground.DangerEdge));
                if (c.Boss != null) FxHost.Run(Ground.Glow(c.Boss, new Color(0.4f, 1f, 0.3f), c.B, 6f));

                for (float t = 0f; t < c.B; t += Time.deltaTime)
                {
                    fill.Length(Mathf.Max(0.5f, c.A * Ground.Ease(t / c.B)));
                    floor.Alpha(Ground.Danger.a * (0.7f + 0.3f * Mathf.Sin(t * 8f)));
                    yield return null;
                }

                fill.Dispose();
                floor.Alpha(0.15f);

                Color wood = new Color(0.32f, 0.2f, 0.09f, 1f);
                List<GameObject> roots = new List<GameObject>();
                float nextRoot = 0f;

                for (float front = 0f; front < c.A; front += c.C * Time.deltaTime)
                {
                    while (nextRoot < front)
                    {
                        float side = Random.Range(-0.45f, 0.45f) * c.D;
                        Vector3 p = SpecialRun.OnGround(c.At + c.Aim * nextRoot + right * side);
                        GameObject root = drawn.Lump(PrimitiveType.Cube, p, new Vector3(0.45f, 3.2f, 0.45f), wood);
                        root.transform.rotation = Quaternion.Euler(Random.Range(-18f, 18f), Random.Range(0f, 360f), Random.Range(-18f, 18f));
                        root.transform.position = p + Vector3.up * 1.2f;
                        roots.Add(root);
                        nextRoot += 1.1f;
                    }

                    yield return null;
                }

                yield return new WaitForSeconds(0.9f);
            }
            finally
            {
                drawn.Dispose();
            }
        }
    }
}

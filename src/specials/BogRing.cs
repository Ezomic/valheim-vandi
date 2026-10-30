using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// Bonemass's ground sweep: the ground around him is marked out to its full reach for a long
    /// time, then a ring of poison rolls outward from him across the marked ground. You are safe
    /// outside the mark, or in the air as the ring passes under you (a jump clears it).
    /// </summary>
    internal sealed class BogRingSpecial : BossSpecial
    {
        private static ConfigEntry<bool> _on;
        private static ConfigEntry<float> _first, _cooldown, _warning, _radius, _speed, _band, _damage, _push;

        internal override int Id { get { return 1; } }
        internal override string Name { get { return "bog ring"; } }
        internal override string BossKey { get { return "defeated_bonemass"; } }
        internal override bool On { get { return _on.Value; } }
        internal override float FirstDelay { get { return _first.Value; } }
        internal override float Cooldown { get { return _cooldown.Value; } }

        internal override void Bind(ConfigFile cfg)
        {
            const string s = "BonemassBogRing";

            _on = SpecialConfig.Bind(cfg, s, "Enabled", true, "A starred Bonemass marks out the ground around him, then sends a ring of poison across it. "
                + "Off removes this move and leaves his others.");
            _first = SpecialConfig.Bind(cfg, s, "FirstDelay", 30f, "Seconds from the boss appearing until its first ring. Later than the skeleton fan on purpose, "
                + "so the opening is still about the boss.");
            _cooldown = SpecialConfig.Bind(cfg, s, "Cooldown", 45f, "Seconds between one ring ending and the next being allowed.");
            _warning = SpecialConfig.Bind(cfg, s, "WarningSeconds", 3f, "How long the ground stays marked before the ring moves. Nothing hurts until this is over, "
                + "so it should stay long enough to walk out of the mark.");
            _radius = SpecialConfig.Bind(cfg, s, "Radius", 14f, "How far from him the marked ground, and the ring, reach, in metres.");
            _speed = SpecialConfig.Bind(cfg, s, "RingSpeed", 5f, "How fast the ring moves outward, metres a second.");
            _band = SpecialConfig.Bind(cfg, s, "RingWidth", 2.5f, "Thickness of the ring in metres. With the speed this is how long a jump has to last: "
                + "2.5 metres at 5 a second is half a second in the air.");
            _damage = SpecialConfig.Bind(cfg, s, "Damage", 55f, "Poison damage to a player the ring passes through on the ground.");
            _push = SpecialConfig.Bind(cfg, s, "Knockback", 15f, "Push on a player the ring hits.");
        }

        internal override IEnumerator Run(SpecialRun run)
        {
            if (!Ground.CanDraw) yield break;

            Vector3 at = SpecialRun.OnGround(run.Boss.transform.position);
            float warning = _warning.Value, radius = _radius.Value, speed = _speed.Value, band = _band.Value;

            run.Send(new SpecialCall { At = at, A = radius, B = warning, C = speed, D = band });

            yield return run.Pause(warning + SpecialRun.Lag);
            if (!run.Alive) yield break;

            HashSet<Player> hit = new HashSet<Player>();
            float front = 0f;

            while (front < radius + band && run.Alive)
            {
                front += speed * Time.deltaTime;

                foreach (Player p in Player.GetAllPlayers())
                {
                    if (hit.Contains(p) || !SpecialRun.Grounded(p)) continue;

                    float d = SpecialRun.FlatDistance(p.transform.position, at);
                    if (d > front || d < front - band || d > radius) continue;

                    hit.Add(p);
                    run.Hit(p, _damage.Value, HitData.DamageType.Poison, at, _push.Value, "ring");
                }

                yield return null;
            }
        }

        internal override IEnumerator Show(SpecialCall c)
        {
            Drawn drawn = new Drawn();

            try
            {
                Zone floor = drawn.Add(Zone.Disc(c.At, c.A, Ground.Danger));
                Band edge = drawn.Add(Band.Ring(c.At, c.A, 0.35f, Ground.DangerEdge));
                Band fill = drawn.Add(Band.Ring(c.At, 0.5f, 0.5f, Ground.DangerEdge));

                Vector3 centre = c.At;
                if (c.Boss != null) FxHost.Run(Ground.Glow(c.Boss, new Color(0.5f, 1f, 0.2f), c.B, 6f));

                for (float t = 0f; t < c.B; t += Time.deltaTime)
                {
                    float k = t / c.B;
                    fill.Radius(Mathf.Max(0.5f, c.A * Ground.Ease(k)));
                    floor.Alpha(Ground.Danger.a * (0.7f + 0.3f * Mathf.Sin(t * 8f)));
                    yield return null;
                }

                fill.Dispose();
                edge.Alpha(0.35f);
                floor.Alpha(0.12f);

                Band wave = drawn.Add(Band.Ring(centre, 0.5f, c.D, new Color(0.45f, 0.9f, 0.2f, 0.8f)));

                for (float front = 0f; front < c.A + c.D; front += c.C * Time.deltaTime)
                {
                    wave.Radius(Mathf.Max(0.5f, front - c.D * 0.5f));
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

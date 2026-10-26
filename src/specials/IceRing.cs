using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// Moder's ice ring: a circle of ground around you is marked, then it freezes over for a few
    /// seconds. Anyone on the ice takes a bite of frost when it forms and again every second it
    /// lasts, and the frost slows them. The way out is to leave the circle before it forms, or to
    /// run for the edge once it has.
    /// </summary>
    internal sealed class IceRingSpecial : BossSpecial
    {
        private static ConfigEntry<bool> _on;
        private static ConfigEntry<float> _first, _cooldown, _warning, _lasts, _tick, _radius, _entry, _damage;

        internal override int Id { get { return 5; } }
        internal override string Name { get { return "ice ring"; } }
        internal override string BossKey { get { return "defeated_dragon"; } }
        internal override bool On { get { return _on.Value; } }
        internal override float FirstDelay { get { return _first.Value; } }
        internal override float Cooldown { get { return _cooldown.Value; } }
        internal override float Reach { get { return 60f; } }

        internal override void Bind(ConfigFile cfg)
        {
            const string s = "ModerIceRing";

            _on = cfg.Bind(s, "Enabled", true,
                "A starred Moder marks a circle of ground around you, then freezes it for a few seconds. "
                + "Off removes this move.");
            _first = cfg.Bind(s, "FirstDelay", 35f, "Seconds from the boss appearing until its first ring.");
            _cooldown = cfg.Bind(s, "Cooldown", 50f, "Seconds between one ring ending and the next being allowed.");
            _warning = cfg.Bind(s, "WarningSeconds", 3f,
                "How long the circle is marked before it freezes. It is placed once, where you stand when "
                + "the warning starts, and does not follow you.");
            _radius = cfg.Bind(s, "Radius", 9f, "Radius of the circle in metres.");
            _lasts = cfg.Bind(s, "FrozenSeconds", 6f, "How long the ice stays hurting after it forms.");
            _tick = cfg.Bind(s, "TickSeconds", 1f, "Seconds between bites of frost while the ice lasts.");
            _entry = cfg.Bind(s, "FormDamage", 30f, "Frost damage to everyone on the ground inside the circle when it freezes.");
            _damage = cfg.Bind(s, "TickDamage", 12f,
                "Frost damage each tick to anyone inside. Frost damage also slows, in proportion to how much "
                + "of the victim's health it takes, so this is both the cost and the chill.");
        }

        internal override IEnumerator Run(SpecialRun run)
        {
            if (!Ground.CanDraw) yield break;

            Vector3 at = SpecialRun.OnGround(run.Target.transform.position);
            float warning = _warning.Value, radius = _radius.Value, lasts = _lasts.Value, tick = _tick.Value;

            run.Send(new SpecialCall { At = at, A = radius, B = warning, C = lasts });

            yield return run.Pause(warning);
            if (!run.Alive) yield break;

            Bite(run, at, radius, _entry.Value, "ice forms");

            for (float t = tick; t <= lasts && run.Alive; t += tick)
            {
                yield return run.Pause(tick);
                if (!run.Alive) yield break;

                Bite(run, at, radius, _damage.Value, "ice");
            }
        }

        private static void Bite(SpecialRun run, Vector3 at, float radius, float damage, string what)
        {
            foreach (Player p in Player.GetAllPlayers())
            {
                if (!SpecialRun.Reachable(p) || !p.IsOnGround()) continue;
                if (SpecialRun.FlatDistance(p.transform.position, at) > radius) continue;

                run.Hit(p, damage, HitData.DamageType.Frost, at, 0f, what);
            }
        }

        internal override IEnumerator Show(SpecialCall c)
        {
            Drawn drawn = new Drawn();

            try
            {
                Color cold = new Color(0.35f, 0.75f, 1f, 0.4f);
                Color coldEdge = new Color(0.6f, 0.9f, 1f, 0.95f);

                Zone floor = drawn.Add(Zone.Disc(c.At, c.A, cold));
                Band edge = drawn.Add(Band.Ring(c.At, c.A, 0.4f, coldEdge));
                Band fill = drawn.Add(Band.Ring(c.At, 0.5f, 0.5f, coldEdge));
                if (c.Boss != null) FxHost.Run(Ground.Glow(c.Boss, new Color(0.5f, 0.85f, 1f), c.B, 8f));

                for (float t = 0f; t < c.B; t += Time.deltaTime)
                {
                    fill.Radius(Mathf.Max(0.5f, c.A * Ground.Ease(t / c.B)));
                    floor.Alpha(cold.a * (0.7f + 0.3f * Mathf.Sin(t * 8f)));
                    yield return null;
                }

                fill.Dispose();
                floor.Colour(new Color(0.85f, 0.95f, 1f, 0.75f));
                FxHost.Run(Ground.Flash(c.At, new Color(0.6f, 0.85f, 1f), 0.5f, 6f, c.A * 2f));

                Color shard = new Color(0.8f, 0.95f, 1f, 0.8f);
                for (int i = 0; i < 12; i++)
                {
                    Vector2 o = Random.insideUnitCircle * c.A * 0.9f;
                    Vector3 p = SpecialRun.OnGround(c.At + new Vector3(o.x, 0f, o.y));
                    GameObject crystal = drawn.Lump(PrimitiveType.Cube, p + Vector3.up * 0.6f, new Vector3(0.3f, 1.4f, 0.3f), shard);
                    crystal.transform.rotation = Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(0f, 360f), Random.Range(-15f, 15f));
                }

                for (float t = 0f; t < c.C; t += Time.deltaTime)
                {
                    float left = c.C - t;
                    floor.Alpha(left < 1f ? 0.75f * left : 0.75f);
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

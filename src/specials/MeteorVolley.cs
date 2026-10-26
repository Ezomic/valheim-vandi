using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// Yagluth's meteor volley: spots are marked around where you stand, one after another, and
    /// each is struck by a meteor when its own warning runs out. The first mark is on you; the rest
    /// are scattered around you, so the volley is read by watching the ground and not the sky.
    /// </summary>
    internal sealed class MeteorSpecial : BossSpecial
    {
        private static ConfigEntry<bool> _on;
        private static ConfigEntry<float> _first, _cooldown, _warning, _stagger, _spread, _radius, _damage, _push;
        private static ConfigEntry<int> _count;

        internal override int Id { get { return 3; } }
        internal override string Name { get { return "meteor volley"; } }
        internal override string BossKey { get { return "defeated_goblinking"; } }
        internal override bool On { get { return _on.Value; } }
        internal override float FirstDelay { get { return _first.Value; } }
        internal override float Cooldown { get { return _cooldown.Value; } }

        internal override void Bind(ConfigFile cfg)
        {
            const string s = "YagluthMeteors";

            _on = cfg.Bind(s, "Enabled", true,
                "A starred Yagluth marks spots around you on the ground, and meteors land on them. "
                + "Off removes this move and leaves his others.");
            _first = cfg.Bind(s, "FirstDelay", 35f, "Seconds from the boss appearing until its first volley.");
            _cooldown = cfg.Bind(s, "Cooldown", 45f, "Seconds between one volley ending and the next being allowed.");
            _count = cfg.Bind(s, "Meteors", 6, "Meteors in one volley. The first lands where you were standing when it was marked.");
            _warning = cfg.Bind(s, "WarningSeconds", 2.8f,
                "How long each spot is marked before its meteor lands. Nothing falls on a spot that has "
                + "not been marked for this long.");
            _stagger = cfg.Bind(s, "StaggerSeconds", 0.5f, "Seconds between one spot being marked and the next.");
            _spread = cfg.Bind(s, "Spread", 8f, "How far from you, in metres, the scattered spots can be marked.");
            _radius = cfg.Bind(s, "ImpactRadius", 3.5f, "Radius of each marked spot, in metres.");
            _damage = cfg.Bind(s, "Damage", 75f, "Fire damage to a player inside a spot when its meteor lands.");
            _push = cfg.Bind(s, "Knockback", 20f, "Push on a player a meteor hits.");
        }

        internal override IEnumerator Run(SpecialRun run)
        {
            if (!Ground.CanDraw) yield break;

            int count = Mathf.Max(1, _count.Value);
            float warning = _warning.Value, stagger = _stagger.Value, radius = _radius.Value;

            for (int i = 0; i < count && run.Alive; i++)
            {
                Vector3 anchor = run.Target != null ? run.Target.transform.position : run.Boss.transform.position;
                Vector2 scatter = i == 0 ? Vector2.zero : Random.insideUnitCircle.normalized * Random.Range(radius * 0.6f, _spread.Value);
                Vector3 at = SpecialRun.OnGround(anchor + new Vector3(scatter.x, 0f, scatter.y));

                run.Send(new SpecialCall { At = at, A = radius, B = warning, N = i });
                run.Runner.StartCoroutine(Land(run, at, warning, radius));

                yield return run.Pause(stagger);
            }

            yield return run.Pause(warning);
        }

        private IEnumerator Land(SpecialRun run, Vector3 at, float delay, float radius)
        {
            yield return new WaitForSeconds(delay);
            if (!run.Alive) yield break;

            foreach (Player p in Player.GetAllPlayers())
            {
                if (!SpecialRun.Reachable(p)) continue;
                if (SpecialRun.FlatDistance(p.transform.position, at) > radius) continue;
                if (p.transform.position.y - at.y > 4f) continue;

                run.Hit(p, _damage.Value, HitData.DamageType.Fire, at, _push.Value, "meteor");
            }
        }

        internal override IEnumerator Show(SpecialCall c)
        {
            Drawn drawn = new Drawn();

            try
            {
                Zone floor = drawn.Add(Zone.Disc(c.At, c.A, Ground.Danger));
                Band edge = drawn.Add(Band.Ring(c.At, c.A, 0.3f, Ground.DangerEdge));
                Band fill = drawn.Add(Band.Ring(c.At, 0.3f, 0.45f, Ground.DangerEdge));

                const float fall = 0.7f, height = 38f;
                GameObject rock = null;
                Color ember = new Color(1f, 0.45f, 0.1f, 1f);

                for (float t = 0f; t < c.B; t += Time.deltaTime)
                {
                    fill.Radius(Mathf.Max(0.3f, c.A * Ground.Ease(t / c.B)));
                    floor.Alpha(Ground.Danger.a * (0.7f + 0.3f * Mathf.Sin(t * 9f)));

                    float left = c.B - t;
                    if (left < fall)
                    {
                        if (rock == null) rock = drawn.Lump(PrimitiveType.Sphere, c.At + Vector3.up * height, Vector3.one * 2.2f, ember);
                        rock.transform.position = c.At + Vector3.up * (height * (left / fall));
                    }

                    yield return null;
                }

                fill.Dispose();
                edge.Dispose();
                if (rock != null) Object.Destroy(rock);

                FxHost.Run(Ground.Flash(c.At, new Color(1f, 0.55f, 0.15f), 0.6f, 6f, c.A * 3f));

                GameObject burst = drawn.Lump(PrimitiveType.Sphere, c.At + Vector3.up * 0.5f, Vector3.one, new Color(1f, 0.5f, 0.1f, 0.55f));
                for (float t = 0f; t < 0.45f; t += Time.deltaTime)
                {
                    float k = t / 0.45f;
                    burst.transform.localScale = Vector3.one * c.A * 2f * Mathf.Sqrt(k);
                    floor.Alpha(0.5f * (1f - k));
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

using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// The Queen's seeker swarm: a spiral of spots is marked on the ground around her, and a
    /// seeker rises out of each one in turn, from the inside of the spiral out, and hunts you.
    /// The seekers are hers: marked, lootless, and each one that dies chips her.
    /// </summary>
    internal sealed class SeekerSpiralSpecial : BossSpecial
    {
        private static ConfigEntry<bool> _on;
        private static ConfigEntry<float> _first, _cooldown, _warning, _interval, _turns, _inner, _outer;
        private static ConfigEntry<int> _count;
        private static ConfigEntry<string> _prefab;

        internal override int Id { get { return 4; } }
        internal override string Name { get { return "seeker swarm"; } }
        internal override string BossKey { get { return "defeated_queen"; } }
        internal override bool On { get { return _on.Value; } }
        internal override float FirstDelay { get { return _first.Value; } }
        internal override float Cooldown { get { return _cooldown.Value; } }

        internal override void Bind(ConfigFile cfg)
        {
            const string s = "QueenSeekerSwarm";

            _on = cfg.Bind(s, "Enabled", true,
                "A starred Seeker Queen marks a spiral of spots around her, and a seeker rises from each. "
                + "Off removes this move and leaves her others.");
            _first = cfg.Bind(s, "FirstDelay", 35f, "Seconds from the boss appearing until its first swarm.");
            _cooldown = cfg.Bind(s, "Cooldown", 50f, "Seconds between one swarm ending and the next being allowed.");
            _count = cfg.Bind(s, "Seekers", 6, "Seekers in one swarm, one per marked spot.");
            _prefab = cfg.Bind(s, "Prefab", "Seeker",
                "The creature that rises. Seeker is the ordinary one; SeekerBrute is far stronger and would "
                + "want the count turned down.");
            _warning = cfg.Bind(s, "WarningSeconds", 2.5f,
                "How long the first spot is marked before its seeker rises. Later spots are marked the same "
                + "time longer, so every one is marked for at least this long.");
            _interval = cfg.Bind(s, "IntervalSeconds", 0.35f, "Seconds between one seeker rising and the next.");
            _turns = cfg.Bind(s, "SpiralTurns", 1.25f, "How many times round her the spiral goes from its first spot to its last.");
            _inner = cfg.Bind(s, "InnerRadius", 4f, "Distance of the first spot from her, in metres.");
            _outer = cfg.Bind(s, "OuterRadius", 12f, "Distance of the last spot from her, in metres.");
        }

        internal override IEnumerator Run(SpecialRun run)
        {
            if (!Ground.CanDraw) yield break;

            ZNetScene scene = ZNetScene.instance;
            GameObject prefab = scene == null ? null : scene.GetPrefab(_prefab.Value);
            if (prefab == null)
            {
                VandiPlugin.Log.LogWarning("Boss special: no '" + _prefab.Value + "' prefab to release.");
                yield break;
            }

            int count = Mathf.Max(1, _count.Value);
            float warning = _warning.Value, interval = _interval.Value;
            Vector3 centre = run.Boss.transform.position;
            float baseAngle = Random.Range(0f, 360f);
            Vector3[] spots = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                float k = count == 1 ? 0f : i / (float)(count - 1);
                float angle = (baseAngle + k * _turns.Value * 360f) * Mathf.Deg2Rad;
                float radius = Mathf.Lerp(_inner.Value, _outer.Value, k);
                spots[i] = SpecialRun.OnGround(centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);

                run.Send(new SpecialCall { At = spots[i], A = 1.6f, B = warning + i * interval, N = i });
            }

            yield return run.Pause(warning);

            for (int i = 0; i < count && run.Alive; i++)
            {
                Rise(run, prefab, spots[i]);
                yield return run.Pause(interval);
            }
        }

        private void Rise(SpecialRun run, GameObject prefab, Vector3 at)
        {
            Vector3 face = run.Target != null ? run.Target.transform.position - at : run.Boss.transform.forward;
            face.y = 0f;
            if (face.sqrMagnitude < 0.01f) face = Vector3.forward;

            GameObject add = Object.Instantiate(prefab, at + Vector3.up * 0.4f, Quaternion.LookRotation(face));
            run.Mark(add);

            BaseAI ai = add.GetComponent<BaseAI>();
            if (ai != null) ai.SetHuntPlayer(true);

            if (VandiConfig.Verbose.Value)
                VandiPlugin.Log.LogInfo("Boss special seeker swarm: " + prefab.name + " rose at " + at.ToString("0") + ".");
        }

        internal override IEnumerator Show(SpecialCall c)
        {
            Drawn drawn = new Drawn();

            try
            {
                Zone floor = drawn.Add(Zone.Disc(c.At, c.A, Ground.Danger));
                Band edge = drawn.Add(Band.Ring(c.At, c.A, 0.25f, Ground.DangerEdge));
                Band fill = drawn.Add(Band.Ring(c.At, 0.2f, 0.35f, Ground.DangerEdge));

                for (float t = 0f; t < c.B; t += Time.deltaTime)
                {
                    fill.Radius(Mathf.Max(0.2f, c.A * Ground.Ease(t / c.B)));
                    floor.Alpha(Ground.Danger.a * (0.7f + 0.3f * Mathf.Sin(t * 9f + c.N)));
                    yield return null;
                }

                edge.Dispose();
                fill.Dispose();

                for (float t = 0f; t < 0.4f; t += Time.deltaTime)
                {
                    floor.Alpha(0.5f * (1f - t / 0.4f));
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

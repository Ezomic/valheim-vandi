using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// Runs the effect coroutines. A boss can be destroyed while its warning is still on the
    /// ground, and a coroutine on the boss would take the warning with it half drawn; this lives
    /// on its own object for the length of the session.
    /// </summary>
    internal sealed class FxHost : MonoBehaviour
    {
        private static FxHost _instance;

        internal static void Run(IEnumerator routine)
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("vandi_fx");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<FxHost>();
            }

            _instance.StartCoroutine(routine);
        }
    }

    /// <summary>
    /// What a warning is drawn with: flat shapes laid on the terrain, from a shader the game already
    /// ships. Every number in them is a vertex height read off the ground, so a ring on a slope
    /// follows the slope instead of sinking into it.
    ///
    /// A shader that cannot be found turns the drawing off and says so once. A null material
    /// renders magenta, which is a louder wrong answer than no drawing, and a move whose warning
    /// cannot be seen must not hurt anyone: <see cref="CanDraw"/> is what the moves check.
    /// </summary>
    internal static class Ground
    {
        internal const float Lift = 0.18f;

        private static Shader _shader;
        private static bool _tried;

        internal static readonly Color Danger = new Color(1f, 0.18f, 0.08f, 0.38f);
        internal static readonly Color DangerEdge = new Color(1f, 0.45f, 0.12f, 0.95f);

        internal static bool CanDraw { get { return Find() != null; } }

        private static Shader Find()
        {
            if (_tried) return _shader;
            _tried = true;

            string[] names =
            {
                "Legacy Shaders/Particles/Alpha Blended",
                "Particles/Standard Unlit",
                "Sprites/Default",
                "Unlit/Color",
            };

            foreach (string name in names)
            {
                Shader shader = UnityEngine.Shader.Find(name);
                if (shader == null) continue;

                _shader = shader;
                VandiPlugin.Log.LogInfo("Boss special warnings are drawn with " + name + ".");
                return _shader;
            }

            VandiPlugin.Log.LogWarning("No shader available to draw boss special warnings, so the moves stay off.");
            return null;
        }

        internal static Material Material(Color color)
        {
            Material m = new Material(Find());
            m.renderQueue = 3000;
            Tint(m, color);
            return m;
        }

        // The legacy particle shader multiplies by _TintColor and defaults it to half grey, so a
        // colour written only to _Color would arrive at a fraction of its strength.
        internal static void Tint(Material m, Color color)
        {
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        }

        internal static float Height(Vector3 p)
        {
            float h;
            return ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out h) ? h : p.y;
        }

        internal static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>Eases 0 to 1, for a fill that arrives slowly and finishes with a snap.</summary>
        internal static float Ease(float t)
        {
            return Mathf.Clamp01(t) * Mathf.Clamp01(t);
        }

        /// <summary>A light on the boss that swells over the warning, so the boss itself reads as charging.</summary>
        internal static IEnumerator Glow(Character boss, Color color, float seconds, float peak)
        {
            if (boss == null) yield break;

            GameObject go = new GameObject("vandi_charge");
            go.transform.SetParent(boss.transform, false);
            go.transform.localPosition = Vector3.up * 3f;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 18f;
            light.intensity = 0f;

            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (light == null) yield break;

                light.intensity = peak * Ease(t / seconds);
                yield return null;
            }

            if (go != null) Object.Destroy(go);
        }

        /// <summary>A brief light where something lands or goes off.</summary>
        internal static IEnumerator Flash(Vector3 at, Color color, float seconds, float peak, float range)
        {
            GameObject go = new GameObject("vandi_flash");
            go.transform.position = at + Vector3.up * 1.5f;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;

            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                light.intensity = peak * (1f - t / seconds);
                yield return null;
            }

            Object.Destroy(go);
        }
    }

    /// <summary>A flat shape on the ground, a mesh whose vertices follow the terrain.</summary>
    internal sealed class Zone
    {
        private readonly GameObject _go;
        private readonly Material _mat;
        private readonly MeshFilter _filter;
        private readonly Mesh _mesh = new Mesh();
        private Color _color;

        private Vector3 _from, _dir;
        private float _width;

        private Zone(Color color)
        {
            _color = color;
            _go = new GameObject("vandi_zone");
            _mat = Ground.Material(color);
            _filter = _go.AddComponent<MeshFilter>();
            _filter.sharedMesh = _mesh;

            MeshRenderer renderer = _go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        internal static Zone Disc(Vector3 centre, float radius, Color color)
        {
            Zone z = new Zone(color);
            z._go.transform.position = centre;
            z.BuildDisc(radius);
            return z;
        }

        /// <summary>A strip from a point along a direction. <see cref="Length"/> redraws it to any length up to the width of the world.</summary>
        internal static Zone Strip(Vector3 from, Vector3 dir, float length, float width, Color color)
        {
            Zone z = new Zone(color);
            z._from = from;
            z._dir = Ground.Flat(dir).normalized;
            z._width = width;
            z._go.transform.position = from;
            z.Length(length);
            return z;
        }

        internal void Move(Vector3 from, Vector3 dir, float length)
        {
            _from = from;
            _dir = Ground.Flat(dir).normalized;
            _go.transform.position = from;
            Length(length);
        }

        internal void Length(float length)
        {
            length = Mathf.Max(length, 0.01f);

            int cells = Mathf.Max(1, Mathf.CeilToInt(length / 2f));
            Vector3 right = Vector3.Cross(Vector3.up, _dir);
            Vector3[] verts = new Vector3[(cells + 1) * 2];
            int[] tris = new int[cells * 6];

            for (int i = 0; i <= cells; i++)
            {
                Vector3 centre = _from + _dir * (length * i / cells);
                Vector3 l = centre - right * (_width * 0.5f);
                Vector3 r = centre + right * (_width * 0.5f);
                verts[i * 2] = new Vector3(l.x - _from.x, Ground.Height(l) + Ground.Lift - _from.y, l.z - _from.z);
                verts[i * 2 + 1] = new Vector3(r.x - _from.x, Ground.Height(r) + Ground.Lift - _from.y, r.z - _from.z);
            }

            for (int i = 0; i < cells; i++)
            {
                int a = i * 2, t = i * 6;
                tris[t] = a; tris[t + 1] = a + 2; tris[t + 2] = a + 1;
                tris[t + 3] = a + 1; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
            }

            Fill(verts, tris);
        }

        private void BuildDisc(float radius)
        {
            const int segments = 40, rings = 5;
            Vector3 c = _go.transform.position;
            Vector3[] verts = new Vector3[1 + segments * rings];
            int[] tris = new int[segments * (1 + (rings - 1) * 2) * 3];

            verts[0] = new Vector3(0f, Ground.Height(c) + Ground.Lift - c.y, 0f);

            for (int r = 1; r <= rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments, d = radius * r / rings;
                    Vector3 w = new Vector3(c.x + Mathf.Cos(a) * d, 0f, c.z + Mathf.Sin(a) * d);
                    verts[1 + (r - 1) * segments + s] = new Vector3(w.x - c.x, Ground.Height(w) + Ground.Lift - c.y, w.z - c.z);
                }

            int n = 0;
            for (int s = 0; s < segments; s++)
            {
                tris[n++] = 0;
                tris[n++] = 1 + (s + 1) % segments;
                tris[n++] = 1 + s;
            }

            for (int r = 1; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int inner = 1 + (r - 1) * segments, outer = 1 + r * segments, s2 = (s + 1) % segments;
                    tris[n++] = inner + s; tris[n++] = inner + s2; tris[n++] = outer + s;
                    tris[n++] = inner + s2; tris[n++] = outer + s2; tris[n++] = outer + s;
                }

            Fill(verts, tris);
        }

        private void Fill(Vector3[] verts, int[] tris)
        {
            _mesh.Clear();
            _mesh.vertices = verts;
            _mesh.triangles = tris;
            _mesh.RecalculateBounds();
        }

        internal void Alpha(float a)
        {
            _color.a = a;
            Ground.Tint(_mat, _color);
        }

        internal void Colour(Color c)
        {
            _color = c;
            Ground.Tint(_mat, _color);
        }

        internal void Dispose()
        {
            if (_go != null) Object.Destroy(_go);
            if (_mat != null) Object.Destroy(_mat);
            Object.Destroy(_mesh);
        }
    }

    /// <summary>A thick line lying on the ground: a ring that can change radius, or a segment that can move.</summary>
    internal sealed class Band
    {
        private const int Points = 56;

        private readonly GameObject _go;
        private readonly Material _mat;
        private readonly LineRenderer _line;
        private Color _color;
        private Vector3 _centre;

        private Band(Color color, float width, bool loop)
        {
            _color = color;
            _go = new GameObject("vandi_band");

            // TransformZ lays the ribbon flat in the plane facing the transform's forward, so up.
            // The default (View) would turn the ribbon to the camera and a ring seen from a
            // shallow angle would be a hairline.
            _go.transform.rotation = Quaternion.LookRotation(Vector3.up);

            _mat = Ground.Material(color);
            _line = _go.AddComponent<LineRenderer>();
            _line.sharedMaterial = _mat;
            _line.useWorldSpace = true;
            _line.alignment = LineAlignment.TransformZ;
            _line.loop = loop;
            _line.widthMultiplier = width;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
        }

        internal static Band Ring(Vector3 centre, float radius, float width, Color color)
        {
            Band b = new Band(color, width, true);
            b._centre = centre;
            b._line.positionCount = Points;
            b.Radius(radius);
            return b;
        }

        internal static Band Segment(Vector3 a, Vector3 b, float width, Color color)
        {
            Band s = new Band(color, width, false);
            s._line.positionCount = 2;
            s.Set(a, b);
            return s;
        }

        internal void Radius(float radius)
        {
            for (int i = 0; i < Points; i++)
            {
                float a = i * Mathf.PI * 2f / Points;
                Vector3 p = _centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                p.y = Ground.Height(p) + Ground.Lift + 0.05f;
                _line.SetPosition(i, p);
            }
        }

        internal void Set(Vector3 a, Vector3 b)
        {
            a.y = Ground.Height(a) + Ground.Lift + 0.05f;
            b.y = Ground.Height(b) + Ground.Lift + 0.05f;
            _line.SetPosition(0, a);
            _line.SetPosition(1, b);
        }

        internal void Width(float w) { _line.widthMultiplier = w; }

        internal void Alpha(float a)
        {
            _color.a = a;
            Ground.Tint(_mat, _color);
        }

        internal void Dispose()
        {
            if (_go != null) Object.Destroy(_go);
            if (_mat != null) Object.Destroy(_mat);
        }
    }

    /// <summary>Things drawn for one effect, gathered so a coroutine that ends early still removes them.</summary>
    internal sealed class Drawn
    {
        private readonly List<Zone> _zones = new List<Zone>();
        private readonly List<Band> _bands = new List<Band>();
        private readonly List<GameObject> _objects = new List<GameObject>();

        internal Zone Add(Zone z) { _zones.Add(z); return z; }
        internal Band Add(Band b) { _bands.Add(b); return b; }

        /// <summary>A solid lump, for a meteor or a root: a primitive without its collider, tinted.</summary>
        internal GameObject Lump(PrimitiveType type, Vector3 at, Vector3 scale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = at;
            go.transform.localScale = scale;

            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = Ground.Material(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _objects.Add(go);
            return go;
        }

        internal void Dispose()
        {
            foreach (Zone z in _zones) z.Dispose();
            foreach (Band b in _bands) b.Dispose();
            foreach (GameObject go in _objects)
            {
                if (go == null) continue;

                Renderer renderer = go.GetComponent<Renderer>();
                if (renderer != null && renderer.sharedMaterial != null) Object.Destroy(renderer.sharedMaterial);
                Object.Destroy(go);
            }

            _zones.Clear();
            _bands.Clear();
            _objects.Clear();
        }
    }
}

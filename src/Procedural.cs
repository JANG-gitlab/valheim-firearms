using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimFirearms
{
    /// <summary>Géométrie à faces plates : chaque face a ses propres sommets (normales nettes, look « low-poly »).</summary>
    internal class MeshBuilder
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<List<int>> _sub = new List<List<int>>();
        public float UvScale = 3f;

        public MeshBuilder(int submeshes) { for (int i = 0; i < submeshes; i++) _sub.Add(new List<int>()); }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            // Unity : sens horaire vu depuis la normale = face avant ; la normale vaut Cross(b-a, d-a).
            var n = Vector3.Cross(b - a, d - a).normalized;
            int i0 = Add(n, a, b, c, d);
            _sub[sub].AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
        }

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i0 = Add(n, a, b, c);
            _sub[sub].AddRange(new[] { i0, i0 + 1, i0 + 2 });
        }

        private int Add(Vector3 n, params Vector3[] pts)
        {
            // Projection planaire selon l'axe dominant de la normale.
            var an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            System.Func<Vector3, Vector2> uv = an.x >= an.y && an.x >= an.z ? (p => new Vector2(p.z, p.y))
                : an.y >= an.z ? (System.Func<Vector3, Vector2>)(p => new Vector2(p.z, p.x)) : (p => new Vector2(p.x, p.y));
            int i0 = _v.Count;
            foreach (var p in pts) { _v.Add(p); _n.Add(n); _uv.Add(uv(p) * UvScale); }
            return i0;
        }

        /// <summary>Hexaèdre quelconque. Ordre : face arrière (z-) bas-gauche, bas-droit, haut-droit, haut-gauche puis face avant pareil.</summary>
        public void Hexa(int sub, Vector3[] p)
        {
            // p0..p3 : arrière ; p4..p7 : avant (même ordre). Faces orientées vers l'extérieur.
            Quad(sub, p[1], p[0], p[3], p[2]); // arrière
            Quad(sub, p[4], p[5], p[6], p[7]); // avant
            Quad(sub, p[0], p[4], p[7], p[3]); // gauche
            Quad(sub, p[5], p[1], p[2], p[6]); // droite
            Quad(sub, p[3], p[7], p[6], p[2]); // haut
            Quad(sub, p[0], p[1], p[5], p[4]); // bas
        }

        /// <summary>Tronc de pyramide le long de Z : rectangles arrière et avant (yMin, yMax, demi-largeur), centrés en x = cx.</summary>
        public void Frustum(int sub, float zBack, float zFront, Vector3 back, Vector3 front, float cx = 0f)
        {
            // back/front = (yMin, yMax, halfWidth)
            Hexa(sub, new[]
            {
                new Vector3(cx - back.z, back.x, zBack), new Vector3(cx + back.z, back.x, zBack),
                new Vector3(cx + back.z, back.y, zBack), new Vector3(cx - back.z, back.y, zBack),
                new Vector3(cx - front.z, front.x, zFront), new Vector3(cx + front.z, front.x, zFront),
                new Vector3(cx + front.z, front.y, zFront), new Vector3(cx - front.z, front.y, zFront),
            });
        }

        public void Box(int sub, Vector3 min, Vector3 max) =>
            Frustum(sub, min.z, max.z, new Vector3(min.y, max.y, (max.x - min.x) / 2), new Vector3(min.y, max.y, (max.x - min.x) / 2), (min.x + max.x) / 2);

        /// <summary>Cylindre d'axe Z.</summary>
        public void Cylinder(int sub, Vector3 center, float radius, float zBack, float zFront, int seg = 10, float radiusFront = -1f)
        {
            if (radiusFront < 0) radiusFront = radius;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1));
                var b0 = center + d0 * radius + Vector3.forward * zBack; var b1 = center + d1 * radius + Vector3.forward * zBack;
                var f0 = center + d0 * radiusFront + Vector3.forward * zFront; var f1 = center + d1 * radiusFront + Vector3.forward * zFront;
                Quad(sub, b0, b1, f1, f0);
                var cb = center + Vector3.forward * zBack; var cf = center + Vector3.forward * zFront;
                Tri(sub, cb, b1, b0);
                Tri(sub, cf, f0, f1);
            }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _uv);
            m.subMeshCount = _sub.Count;
            for (int i = 0; i < _sub.Count; i++) m.SetTriangles(_sub[i], i);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }

    internal static class Procedural
    {
        private static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();
        private static Mesh _quad;

        /// <summary>Quad unité dans le plan XY, visible depuis -Z (le côté vers lequel pointe -forward).</summary>
        public static Mesh Quad
        {
            get
            {
                if (_quad) return _quad;
                _quad = new Mesh { name = "vfa_quad" };
                _quad.SetVertices(new List<Vector3> { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) });
                _quad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                _quad.RecalculateNormals(); _quad.RecalculateBounds();
                return _quad;
            }
        }

        public static Shader FindShader(string name)
        {
            if (Shaders.TryGetValue(name, out var s) && s) return s;
            s = Shader.Find(name) ?? Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(x => x.name == name);
            if (!s) Plugin.Log.LogWarning("Shader introuvable : " + name);
            Shaders[name] = s;
            return s;
        }

        private static Texture2D NewTex(int w, int h, bool alpha, TextureWrapMode wrap = TextureWrapMode.Repeat) =>
            new Texture2D(w, h, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, true) { wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 4 };

        public static Texture2D WoodTexture(Color dark, Color light, int seed)
        {
            var t = NewTex(256, 256, false); var px = new Color[256 * 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float u = x / 256f, v = y / 256f;
                    // Veines le long de U (axe du bois), ondulées par du bruit.
                    float warp = Mathf.PerlinNoise(u * 3 + seed, v * 8) * 6f;
                    float grain = Mathf.Sin((v * 40f + warp) * Mathf.PI) * 0.5f + 0.5f;
                    grain = Mathf.Pow(grain, 3f);
                    float fine = Mathf.PerlinNoise(u * 60 + seed, v * 300) * 0.25f;
                    float blot = Mathf.PerlinNoise(u * 4 + 10 + seed, v * 4) * 0.3f;
                    px[y * 256 + x] = Color.Lerp(light, dark, Mathf.Clamp01(grain * 0.6f + fine + blot));
                }
            t.SetPixels(px); t.Apply(true); return t;
        }

        public static Texture2D MetalTexture(Color baseColor, float wear, int seed)
        {
            var t = NewTex(128, 128, false); var px = new Color[128 * 128];
            var rnd = new System.Random(seed);
            for (int i = 0; i < px.Length; i++)
            {
                int x = i % 128, y = i / 128;
                float n = Mathf.PerlinNoise(x * 0.08f + seed, y * 0.08f) * 0.12f + (float)rnd.NextDouble() * 0.05f;
                px[i] = baseColor * (0.9f + n);
            }
            // Rayures d'usure : traits clairs fins.
            for (int s = 0; s < 40 * wear; s++)
            {
                float x0 = rnd.Next(128), y0 = rnd.Next(128), ang = (float)rnd.NextDouble() * Mathf.PI, len = rnd.Next(6, 30);
                for (int k = 0; k < len; k++)
                {
                    int x = Mathf.RoundToInt(x0 + Mathf.Cos(ang) * k) & 127, y = Mathf.RoundToInt(y0 + Mathf.Sin(ang) * k) & 127;
                    px[y * 128 + x] = Color.Lerp(px[y * 128 + x], baseColor * 2.6f, 0.5f);
                }
            }
            t.SetPixels(px); t.Apply(true); return t;
        }

        /// <summary>Étoile de flash de bouche (additive) : cœur chaud + branches.</summary>
        public static Texture2D FlashTexture(int seed)
        {
            const int S = 128; var t = NewTex(S, S, true, TextureWrapMode.Clamp); var px = new Color[S * S];
            var rnd = new System.Random(seed); int branches = 5 + rnd.Next(3); float phase = (float)rnd.NextDouble() * 6.28f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - S / 2f) / (S / 2f), dy = (y - S / 2f) / (S / 2f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                    float star = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * branches * 0.5f + phase)), 10f) * Mathf.Clamp01(1 - r);
                    float core = Mathf.Clamp01(1 - r * 2.2f);
                    float i = Mathf.Clamp01(core * core + star * 0.9f);
                    px[y * S + x] = new Color(1f, 0.75f + 0.25f * core, 0.4f + 0.6f * core * core, i);
                }
            t.SetPixels(px); t.Apply(true); return t;
        }

        public static Texture2D SoftDot(int size = 32)
        {
            var t = NewTex(size, size, true, TextureWrapMode.Clamp); var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                float dx = (i % size - size / 2f + 0.5f) / (size / 2f), dy = (i / size - size / 2f + 0.5f) / (size / 2f);
                float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy)); a *= a;
                px[i] = new Color(1, 1, 1, a);
            }
            t.SetPixels(px); t.Apply(true); return t;
        }

        /// <summary>Fumée : tache de bruit douce.</summary>
        public static Texture2D SmokeTexture()
        {
            const int S = 64; var t = NewTex(S, S, true, TextureWrapMode.Clamp); var px = new Color[S * S];
            for (int i = 0; i < px.Length; i++)
            {
                int x = i % S, y = i / S; float dx = (x - S / 2f) / (S / 2f), dy = (y - S / 2f) / (S / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float n = Mathf.PerlinNoise(x * 0.12f, y * 0.12f) * 0.6f + Mathf.PerlinNoise(x * 0.3f + 5, y * 0.3f) * 0.4f;
                float a = Mathf.Clamp01((1 - r) * 1.3f) * Mathf.Clamp01(n * 1.4f - 0.15f);
                px[i] = new Color(1, 1, 1, a);
            }
            t.SetPixels(px); t.Apply(true); return t;
        }

        /// <summary>Impact de balle : trou sombre, collerette irrégulière, transparence autour.</summary>
        public static Texture2D BulletHoleTexture(int seed)
        {
            const int S = 64; var t = NewTex(S, S, true, TextureWrapMode.Clamp); var px = new Color[S * S];
            var rnd = new System.Random(seed); float ph = (float)rnd.NextDouble() * 10;
            for (int i = 0; i < px.Length; i++)
            {
                int x = i % S, y = i / S; float dx = (x - S / 2f) / (S / 2f), dy = (y - S / 2f) / (S / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                float jag = 0.12f * Mathf.PerlinNoise(Mathf.Cos(a) * 2 + ph, Mathf.Sin(a) * 2 + ph);
                float hole = Mathf.Clamp01((0.2f + jag - r) * 25f);
                float ring = Mathf.Clamp01((0.55f + jag * 2 - r) * 6f) * 0.55f;
                float alpha = Mathf.Max(hole, ring * Mathf.PerlinNoise(x * 0.3f + ph, y * 0.3f));
                float shade = Mathf.Lerp(0.25f, 0.02f, hole);
                px[i] = new Color(shade, shade, shade, alpha);
            }
            t.SetPixels(px); t.Apply(true); return t;
        }

        /// <summary>Texture de traçante : dégradé transversal, plus intense au centre.</summary>
        public static Texture2D TracerTexture()
        {
            var t = NewTex(64, 16, true, TextureWrapMode.Clamp); var px = new Color[64 * 16];
            for (int i = 0; i < px.Length; i++)
            {
                int x = i % 64, y = i / 64; float v = Mathf.Abs(y - 7.5f) / 7.5f, u = x / 63f;
                float a = Mathf.Clamp01(1 - v * v) * Mathf.Clamp01(u * 1.5f) ;
                px[i] = new Color(1, 1, 1, a);
            }
            t.SetPixels(px); t.Apply(true); return t;
        }

        public static Material ParticleMaterial(Texture tex, bool additive, Color tint)
        {
            var shader = FindShader(additive ? "Legacy Shaders/Particles/Additive" : "Legacy Shaders/Particles/Alpha Blended");
            var m = new Material(shader) { mainTexture = tex, renderQueue = 3100 };
            m.SetColor("_TintColor", tint);
            return m;
        }

        /// <summary>Matériau éclairé du jeu (Custom/Creature cloné depuis l'arbalète) avec une texture à nous.</summary>
        public static Material LitMaterial(Material template, Texture2D tex, Color color, float metallic, float gloss)
        {
            var m = template ? new Material(template) : new Material(FindShader("Standard"));
            m.name = "vfa_" + tex.name;
            m.mainTexture = tex; m.color = color;
            foreach (var p in new[] { "_BumpMap", "_MetallicGlossMap", "_EmissionMap" })
                if (m.HasProperty(p)) m.SetTexture(p, null);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (m.HasProperty("_MetalGloss")) m.SetFloat("_MetalGloss", gloss);
            if (m.HasProperty("_UseGlossmap")) m.SetFloat("_UseGlossmap", 0);
            if (m.HasProperty("_MetalColor")) m.SetColor("_MetalColor", Color.white);
            m.DisableKeyword("_NORMALMAP"); m.DisableKeyword("_METALLICGLOSSMAP"); m.DisableKeyword("_EMISSION");
            return m;
        }
    }
}

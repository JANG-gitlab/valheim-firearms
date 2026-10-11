using System.Collections.Generic;
using UnityEngine;

namespace ValheimFirearms
{
    internal enum SurfaceKind { Flesh, Wood, Stone, Metal, Dirt, Water, Foliage, Generic }

    /// <summary>
    /// Effets visuels locaux (jamais réseau) : particules d'impact, decals, traçantes, douilles.
    /// Un ParticleSystem par type, en espace monde, alimenté par Emit() : peu coûteux même en rafale.
    /// </summary>
    internal class Effects : MonoBehaviour
    {
        public static Effects Instance { get; private set; }

        private ParticleSystem _sparks, _dust, _chips, _blood, _smoke, _splash, _leaves;
        private Material _holeMat, _holeMatLight, _tracerMat;
        private Mesh _quad, _casingMesh;
        private Material _casingMat;
        private readonly List<DecalEntry> _decals = new List<DecalEntry>();
        private readonly List<Casing> _casings = new List<Casing>();
        private readonly List<LineRenderer> _tracerPool = new List<LineRenderer>();

        private struct DecalEntry { public GameObject Go; public float Born; }
        private class Casing { public Transform T; public Vector3 V; public Vector3 Spin; public float Born; public bool Resting; }

        public static Effects Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("VFA_Effects");
            Instance = go.AddComponent<Effects>();
            Instance.Build();
            return Instance;
        }

        private void Build()
        {
            var dot = Procedural.SoftDot();
            var smoke = Procedural.SmokeTexture();
            _sparks = MakeSystem("sparks", Procedural.ParticleMaterial(dot, true, new Color(1f, 0.7f, 0.35f, 1f)), stretched: true, gravity: 1.2f, drag: 0.5f);
            _dust = MakeSystem("dust", Procedural.ParticleMaterial(smoke, false, new Color(0.5f, 0.5f, 0.5f, 0.6f)), stretched: false, gravity: -0.03f, drag: 3f, growth: 2.5f);
            _chips = MakeSystem("chips", Procedural.ParticleMaterial(dot, false, new Color(0.5f, 0.5f, 0.5f, 1f)), stretched: false, gravity: 1.6f, drag: 0.3f);
            _blood = MakeSystem("blood", Procedural.ParticleMaterial(smoke, false, new Color(0.5f, 0.5f, 0.5f, 0.9f)), stretched: false, gravity: 0.4f, drag: 2.5f, growth: 2f);
            _smoke = MakeSystem("muzzlesmoke", Procedural.ParticleMaterial(smoke, false, new Color(0.5f, 0.5f, 0.5f, 0.35f)), stretched: false, gravity: -0.05f, drag: 2.2f, growth: 3.5f);
            _splash = MakeSystem("splash", Procedural.ParticleMaterial(dot, false, new Color(0.6f, 0.65f, 0.7f, 0.8f)), stretched: true, gravity: 1f, drag: 0.4f);
            _leaves = MakeSystem("leaves", Procedural.ParticleMaterial(dot, false, new Color(0.5f, 0.5f, 0.5f, 1f)), stretched: false, gravity: 0.25f, drag: 2f);

            _holeMat = Procedural.ParticleMaterial(Procedural.BulletHoleTexture(3), false, new Color(0.5f, 0.5f, 0.5f, 1f));
            _holeMatLight = Procedural.ParticleMaterial(Procedural.BulletHoleTexture(9), false, new Color(0.62f, 0.6f, 0.56f, 0.9f));
            _holeMat.renderQueue = _holeMatLight.renderQueue = 2460; // juste après la géométrie opaque
            _tracerMat = Procedural.ParticleMaterial(Procedural.TracerTexture(), true, new Color(1f, 0.62f, 0.3f, 1f));

            _quad = Procedural.Quad;

            _casingMesh = GunModel.CasingMesh();
            _casingMat = GunModel.Materials(Items.LitTemplate)[GunModel.Brass];
        }

        private ParticleSystem MakeSystem(string name, Material mat, bool stretched, float gravity, float drag, float growth = 0f)
        {
            var go = new GameObject("vfa_ps_" + name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 3000; main.gravityModifier = gravity; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = drag > 0; lim.drag = drag; lim.multiplyDragByParticleSize = false; lim.multiplyDragByParticleVelocity = true; lim.dampen = 0f; lim.limit = 1000f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            if (growth > 0)
            {
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, growth)));
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = stretched ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            r.velocityScale = stretched ? 0.025f : 0f; r.lengthScale = stretched ? 1.5f : 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
            ps.Play();
            return ps;
        }

        private static void Burst(ParticleSystem ps, Vector3 pos, Vector3 dir, float cone, int count, Vector2 speed, Vector2 size, Vector2 life, Color c1, Color c2)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                var d = Vector3.Slerp(dir, Random.onUnitSphere, cone).normalized;
                if (Vector3.Dot(d, dir) < 0) d = Vector3.Reflect(d, dir);
                ep.position = pos;
                ep.velocity = d * Random.Range(speed.x, speed.y);
                ep.startSize = Random.Range(size.x, size.y);
                ep.startLifetime = Random.Range(life.x, life.y);
                ep.startColor = Color.Lerp(c1, c2, Random.value);
                ep.rotation = Random.Range(0, 360f);
                ps.Emit(ep, 1);
            }
        }

        // ---------------------------------------------------------------- impacts

        public void Impact(SurfaceKind kind, Vector3 pos, Vector3 normal, Vector3 bulletDir, Collider col, bool exitHole = false)
        {
            var refl = Vector3.Reflect(bulletDir, normal).normalized;
            var outDir = Vector3.Slerp(normal, refl, 0.35f).normalized;
            switch (kind)
            {
                case SurfaceKind.Flesh:
                    Burst(_blood, pos, exitHole ? bulletDir : outDir, 0.45f, 10, new Vector2(0.5f, 2.5f), new Vector2(0.08f, 0.2f), new Vector2(0.3f, 0.7f),
                        new Color(0.45f, 0.02f, 0.02f, 0.9f), new Color(0.25f, 0f, 0f, 0.8f));
                    Burst(_chips, pos, exitHole ? bulletDir : outDir, 0.35f, 6, new Vector2(1.5f, 4f), new Vector2(0.015f, 0.03f), new Vector2(0.3f, 0.6f),
                        new Color(0.5f, 0.03f, 0.03f), new Color(0.3f, 0.01f, 0.01f));
                    GunAudio.Play(GunAudio.Pick("impact_flesh_1", "impact_flesh_2"), pos, 0.9f, 40f);
                    return;
                case SurfaceKind.Wood:
                    Burst(_chips, pos, outDir, 0.5f, 14, new Vector2(1.5f, 5f), new Vector2(0.015f, 0.04f), new Vector2(0.5f, 1.2f),
                        new Color(0.55f, 0.38f, 0.2f), new Color(0.75f, 0.58f, 0.36f));
                    Burst(_dust, pos, normal, 0.3f, 4, new Vector2(0.3f, 0.9f), new Vector2(0.1f, 0.2f), new Vector2(0.6f, 1.2f),
                        new Color(0.55f, 0.45f, 0.32f, 0.5f), new Color(0.4f, 0.32f, 0.22f, 0.4f));
                    GunAudio.Play(GunAudio.PickVanilla("sfx_arrow_hit") ?? GunAudio.Get("impact_dirt_2"), pos, 0.7f, 45f, pitchJitter: 0.15f);
                    Decal(pos, normal, col, light: false, size: Random.Range(0.045f, 0.06f));
                    return;
                case SurfaceKind.Stone:
                case SurfaceKind.Metal:
                    bool metal = kind == SurfaceKind.Metal;
                    Burst(_sparks, pos, refl, metal ? 0.35f : 0.5f, metal ? 18 : 8, new Vector2(3f, 9f), new Vector2(0.012f, 0.025f), new Vector2(0.12f, 0.35f),
                        new Color(1f, 0.85f, 0.5f), new Color(1f, 0.55f, 0.2f));
                    if (!metal)
                    {
                        Burst(_dust, pos, outDir, 0.35f, 7, new Vector2(0.6f, 2f), new Vector2(0.12f, 0.25f), new Vector2(0.8f, 1.6f),
                            new Color(0.62f, 0.6f, 0.56f, 0.6f), new Color(0.45f, 0.44f, 0.42f, 0.5f));
                        Burst(_chips, pos, outDir, 0.5f, 8, new Vector2(2f, 6f), new Vector2(0.01f, 0.03f), new Vector2(0.4f, 0.9f),
                            new Color(0.45f, 0.45f, 0.43f), new Color(0.6f, 0.6f, 0.58f));
                    }
                    GunAudio.Play(GunAudio.Pick("impact_dirt_1", "impact_dirt_2"), pos, 0.8f, 45f, pitchJitter: 0.15f);
                    if (Random.value < (metal ? 0.6f : 0.25f)) GunAudio.Play(GunAudio.Pick("ricochet_1", "ricochet_2"), pos, 0.55f, 70f, pitchJitter: 0.12f);
                    if (metal) GunAudio.Play(GunAudio.PickVanilla("sfx_metalbars_hit"), pos, 0.25f, 40f, pitchJitter: 0.2f);
                    Decal(pos, normal, col, light: !metal, size: Random.Range(0.05f, 0.08f));
                    return;
                case SurfaceKind.Dirt:
                    Burst(_dust, pos, outDir, 0.25f, 8, new Vector2(0.8f, 2.5f), new Vector2(0.14f, 0.3f), new Vector2(0.8f, 1.6f),
                        new Color(0.42f, 0.34f, 0.24f, 0.65f), new Color(0.3f, 0.25f, 0.18f, 0.55f));
                    Burst(_chips, pos, normal, 0.35f, 10, new Vector2(1.5f, 4.5f), new Vector2(0.015f, 0.04f), new Vector2(0.4f, 0.9f),
                        new Color(0.3f, 0.24f, 0.16f), new Color(0.22f, 0.18f, 0.12f));
                    GunAudio.Play(GunAudio.Pick("impact_dirt_1", "impact_dirt_2"), pos, 0.85f, 45f, pitchJitter: 0.15f);
                    Decal(pos, normal, col, light: false, size: Random.Range(0.06f, 0.09f));
                    return;
                case SurfaceKind.Water:
                    Burst(_splash, pos, Vector3.up, 0.15f, 18, new Vector2(2f, 5f), new Vector2(0.02f, 0.05f), new Vector2(0.35f, 0.7f),
                        new Color(0.8f, 0.85f, 0.9f, 0.9f), new Color(0.6f, 0.7f, 0.75f, 0.7f));
                    Burst(_dust, pos, Vector3.up, 0.2f, 4, new Vector2(0.3f, 0.8f), new Vector2(0.15f, 0.25f), new Vector2(0.4f, 0.8f),
                        new Color(0.85f, 0.9f, 0.95f, 0.4f), new Color(0.75f, 0.8f, 0.85f, 0.3f));
                    GunAudio.Play(GunAudio.Get("impact_dirt_2"), pos, 0.5f, 35f, pitchJitter: 0.25f);
                    return;
                case SurfaceKind.Foliage:
                    Burst(_leaves, pos, outDir, 0.6f, 8, new Vector2(0.5f, 2f), new Vector2(0.03f, 0.06f), new Vector2(0.8f, 1.6f),
                        new Color(0.25f, 0.4f, 0.12f), new Color(0.4f, 0.5f, 0.15f));
                    return;
                default:
                    Burst(_dust, pos, outDir, 0.35f, 5, new Vector2(0.5f, 1.5f), new Vector2(0.1f, 0.2f), new Vector2(0.6f, 1.2f),
                        new Color(0.55f, 0.53f, 0.5f, 0.5f), new Color(0.4f, 0.4f, 0.38f, 0.4f));
                    GunAudio.Play(GunAudio.Get("impact_dirt_1"), pos, 0.7f, 40f, pitchJitter: 0.15f);
                    Decal(pos, normal, col, light: true, size: 0.06f);
                    return;
            }
        }

        private void Decal(Vector3 pos, Vector3 normal, Collider col, bool light, float size)
        {
            if (FirearmsConfig.MaxDecals.Value <= 0) return;
            while (_decals.Count >= FirearmsConfig.MaxDecals.Value) { var old = _decals[0]; _decals.RemoveAt(0); if (old.Go) Destroy(old.Go); }
            var go = new GameObject("vfa_decal");
            go.transform.position = pos + normal * 0.006f;
            go.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0, 0, Random.Range(0, 360f));
            go.transform.localScale = Vector3.one * size;
            // Rattaché à l'objet touché (pièce de construction, arbre) : disparaît avec lui. Pas au terrain (échelle ok mais inutile).
            if (col && !(col.GetComponentInParent<Heightmap>()) && col.transform.lossyScale.x > 0.001f) go.transform.SetParent(col.transform, true);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = light ? _holeMatLight : _holeMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _decals.Add(new DecalEntry { Go = go, Born = Time.time });
        }

        // ---------------------------------------------------------------- bouche du canon

        public void MuzzleSmoke(Vector3 pos, Vector3 dir)
        {
            Burst(_smoke, pos + dir * 0.05f, dir, 0.25f, 3, new Vector2(0.6f, 2.2f), new Vector2(0.06f, 0.14f), new Vector2(0.5f, 1.1f),
                new Color(0.8f, 0.78f, 0.75f, 0.35f), new Color(0.6f, 0.6f, 0.6f, 0.25f));
            Burst(_sparks, pos, dir, 0.12f, 2, new Vector2(8f, 18f), new Vector2(0.006f, 0.012f), new Vector2(0.04f, 0.09f),
                new Color(1f, 0.85f, 0.5f), new Color(1f, 0.6f, 0.25f));
        }

        public void EjectCasing(Transform ejection, Vector3 inheritVelocity)
        {
            if (!ejection) return;
            Casing c = null;
            if (_casings.Count >= 40) { c = _casings[0]; _casings.RemoveAt(0); }
            if (c == null || !c.T)
            {
                var go = new GameObject("vfa_casing");
                go.AddComponent<MeshFilter>().sharedMesh = _casingMesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = _casingMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c = new Casing { T = go.transform };
            }
            c.T.position = ejection.position;
            c.T.rotation = ejection.rotation * Quaternion.Euler(0, 90, 0);
            c.V = inheritVelocity + ejection.right * Random.Range(2.5f, 4f) + ejection.up * Random.Range(1f, 2f) - ejection.forward * Random.Range(0f, 0.6f);
            c.Spin = Random.insideUnitSphere * 1500f;
            c.Born = Time.time; c.Resting = false;
            _casings.Add(c);
        }

        // ---------------------------------------------------------------- traçantes

        public LineRenderer TakeTracer()
        {
            LineRenderer lr = null;
            while (_tracerPool.Count > 0 && !lr) { lr = _tracerPool[_tracerPool.Count - 1]; _tracerPool.RemoveAt(_tracerPool.Count - 1); }
            if (!lr)
            {
                var go = new GameObject("vfa_tracer");
                go.transform.SetParent(transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = _tracerMat;
                lr.positionCount = 2; lr.useWorldSpace = true;
                lr.textureMode = LineTextureMode.Stretch;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
                lr.numCapVertices = 2;
                lr.widthCurve = new AnimationCurve(new Keyframe(0, 0.006f), new Keyframe(1, 0.028f));
                lr.colorGradient = new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(new Color(1f, 0.45f, 0.15f), 0), new GradientColorKey(new Color(1f, 0.85f, 0.6f), 1) },
                    alphaKeys = new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(1, 1) },
                };
            }
            lr.gameObject.SetActive(true);
            return lr;
        }

        public void ReturnTracer(LineRenderer lr)
        {
            if (!lr) return;
            lr.gameObject.SetActive(false);
            _tracerPool.Add(lr);
        }

        private void Update()
        {
            float dt = Time.deltaTime, life = FirearmsConfig.DecalLifetime.Value;
            while (_decals.Count > 0 && (!_decals[0].Go || Time.time - _decals[0].Born > life))
            {
                if (_decals[0].Go) Destroy(_decals[0].Go);
                _decals.RemoveAt(0);
            }

            for (int i = _casings.Count - 1; i >= 0; i--)
            {
                var c = _casings[i];
                if (!c.T) { _casings.RemoveAt(i); continue; }
                if (Time.time - c.Born > 6f) { Destroy(c.T.gameObject); _casings.RemoveAt(i); continue; }
                if (c.Resting) continue;
                c.V += Physics.gravity * dt;
                var step = c.V * dt;
                if (Physics.Raycast(c.T.position, step.normalized, out var hit, step.magnitude + 0.006f, Ballistics.SolidMask, QueryTriggerInteraction.Ignore))
                {
                    c.T.position = hit.point + hit.normal * 0.006f;
                    if (c.V.magnitude < 1.2f) { c.Resting = true; c.T.rotation = Quaternion.LookRotation(Vector3.Cross(hit.normal, Random.onUnitSphere), hit.normal) * Quaternion.Euler(90, 0, 0); continue; }
                    c.V = Vector3.Reflect(c.V, hit.normal) * 0.35f;
                    c.Spin *= 0.5f;
                    if (Random.value < 0.5f) GunAudio.Play(GunAudio.Get("casing"), hit.point, 0.35f, 15f, pitchJitter: 0.2f);
                }
                else c.T.position += step;
                c.T.Rotate(c.Spin * dt, Space.Self);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace ValheimFirearms
{
    /// <summary>
    /// Balles simulées pas à pas : gravité, traînée quadratique, raycast sur chaque segment parcouru.
    /// Pas de Projectile vanilla. Une balle « autoritaire » (tirée par le joueur local) signale ses impacts
    /// au serveur ; une balle « cosmétique » (tir d'un autre joueur) ne fait que des effets.
    /// </summary>
    internal class Ballistics : MonoBehaviour
    {
        public static int SolidMask;
        public static Ballistics Instance { get; private set; }
        private readonly List<Bullet> _bullets = new List<Bullet>();
        private static readonly RaycastHit[] HitBuf = new RaycastHit[32];

        public class Bullet
        {
            public Vector3 Pos, Vel, Origin, VisualOffset;
            public float Traveled, Energy = 1f;
            public int Penetrations, HitIndex, ShotId;
            public bool Authoritative, LocalShooter, Ricocheted, FlybyDone, Dead;
            public Character Shooter;
            public readonly HashSet<int> HitIds = new HashSet<int>();
            public LineRenderer Tracer;
            public Vector3 TracerTail;
        }

        private enum HitResult { Continue, Stop, Redirect }

        public static void InitMasks()
        {
            SolidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain",
                "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
        }

        public static Ballistics Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("VFA_Ballistics").AddComponent<Ballistics>();
            return Instance;
        }

        public Bullet Spawn(Vector3 origin, Vector3 dir, Vector3 muzzlePos, Character shooter, bool authoritative, int shotId, bool tracer)
        {
            var b = new Bullet
            {
                Pos = origin, Origin = origin, Vel = dir.normalized * FirearmsConfig.MuzzleVelocity.Value,
                Shooter = shooter, Authoritative = authoritative, ShotId = shotId,
                LocalShooter = shooter && shooter == Player.m_localPlayer,
                VisualOffset = muzzlePos - origin, TracerTail = muzzlePos,
            };
            if (tracer && !IsDedicated()) b.Tracer = Effects.Ensure().TakeTracer();
            _bullets.Add(b);
            // Premier pas immédiat : à bout portant, l'impact ne doit pas attendre la frame suivante.
            Advance(b, Time.deltaTime > 0 ? Mathf.Min(Time.deltaTime, 0.02f) : 0.016f);
            return b;
        }

        private static bool IsDedicated() => ZNet.instance && ZNet.instance.IsDedicated();

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            for (int i = _bullets.Count - 1; i >= 0; i--)
            {
                var b = _bullets[i];
                if (!b.Dead) Advance(b, dt);
                UpdateTracer(b);
                if (b.Dead)
                {
                    if (b.Tracer) Effects.Ensure().ReturnTracer(b.Tracer);
                    _bullets.RemoveAt(i);
                }
            }
        }

        private void Advance(Bullet b, float dt)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 120f)));
            float h = dt / steps;
            for (int s = 0; s < steps && !b.Dead; s++) Step(b, h);
        }

        private void Step(Bullet b, float h)
        {
            var v0 = b.Vel;
            b.Vel += Physics.gravity * (FirearmsConfig.GravityScale.Value * h);
            b.Vel -= b.Vel * (FirearmsConfig.Drag.Value * b.Vel.magnitude * h);
            var next = b.Pos + (v0 + b.Vel) * (0.5f * h);
            var seg = next - b.Pos;
            float len = seg.magnitude;
            if (len < 1e-5f) { b.Dead = true; return; }
            var dir = seg / len;

            // Surface de l'eau sur ce segment ?
            float waterDist = float.MaxValue;
            float level = Floating.GetLiquidLevel(next);
            if (next.y < level && b.Pos.y >= level - 0.05f)
                waterDist = len * Mathf.Clamp01((b.Pos.y - level) / Mathf.Max(1e-4f, b.Pos.y - next.y));

            int n = Physics.RaycastNonAlloc(b.Pos, dir, HitBuf, len, SolidMask, QueryTriggerInteraction.Collide);
            System.Array.Sort(HitBuf, 0, n, Comparer);
            for (int i = 0; i < n; i++)
            {
                var hit = HitBuf[i];
                if (hit.distance > waterDist) break;
                var r = ProcessHit(b, hit, dir);
                if (r == HitResult.Continue) continue;
                if (r == HitResult.Stop) { b.Pos = hit.point; b.Dead = true; }
                return; // Redirect : ricochet, Pos/Vel déjà mis à jour
            }
            if (waterDist < float.MaxValue)
            {
                var wp = b.Pos + dir * waterDist;
                if (!IsDedicated()) Effects.Ensure().Impact(SurfaceKind.Water, wp, Vector3.up, dir, null);
                b.Pos = wp; b.Dead = true; // l'eau arrête une balle en moins d'un mètre
                return;
            }

            CheckFlyby(b, b.Pos, next);
            b.Pos = next;
            b.Traveled += len;
            if (b.Traveled > FirearmsConfig.MaxRange.Value || b.Vel.sqrMagnitude < 60f * 60f) b.Dead = true;
        }

        private static readonly IComparer<RaycastHit> Comparer = Comparer<RaycastHit>.Create((x, y) => x.distance.CompareTo(y.distance));

        private HitResult ProcessHit(Bullet b, RaycastHit hit, Vector3 dir)
        {
            var col = hit.collider;
            if (!col) return HitResult.Continue;
            var ch = col.GetComponentInParent<Character>();
            if (col.isTrigger && !ch) return HitResult.Continue;
            bool fx = !IsDedicated();
            var normal = hit.distance == 0 ? -dir : hit.normal;
            var point = hit.distance == 0 ? b.Pos : hit.point;

            if (ch)
            {
                if (ch == b.Shooter || ch.IsDead()) return HitResult.Continue;
                if (!b.HitIds.Add(ch.GetInstanceID())) return HitResult.Continue;
                bool head = IsHeadshot(ch, point);
                short weak = ch.FindWeakSpotIndex(col);
                if (fx) Effects.Ensure().Impact(FleshKind(ch), point, normal, dir, col);
                float dist = b.Traveled + hit.distance;
                if (b.Authoritative && !b.Ricocheted)
                    Network.ReportHit(b.ShotId, b.HitIndex, ch, point, head, weak, dist, b.Energy);
                b.HitIndex++;
                if (FirearmsConfig.DebugHits.Value)
                    Plugin.Log.LogInfo($"[tir {b.ShotId}] touche {ch.name} à {dist:F1} m tête={head} weak={weak} énergie={b.Energy:F2}");

                float maxH = BaseMaxHealth(ch.m_health, ch.GetLevel());
                if (CanPenetrate(maxH, b.Penetrations))
                {
                    b.Energy *= 1f - PenetrationLoss(maxH);
                    b.Penetrations++;
                    if (b.Energy < 0.08f) return HitResult.Stop;
                    return HitResult.Continue;
                }
                return HitResult.Stop;
            }

            var kind = Classify(col);
            if (fx) Effects.Ensure().Impact(kind, point, normal, dir, col);

            if (kind == SurfaceKind.Foliage) return HitResult.Continue; // feuillage : traverse sans effet
            if (kind == SurfaceKind.Wood && IsThinWood(col) && FirearmsConfig.WoodPenetrationEnergyLoss.Value < 1f &&
                b.Energy > 0.3f && b.Penetrations < FirearmsConfig.MaxPenetrations.Value)
            {
                b.Energy *= 1f - FirearmsConfig.WoodPenetrationEnergyLoss.Value;
                b.Vel *= Mathf.Sqrt(Mathf.Max(0.05f, 1f - FirearmsConfig.WoodPenetrationEnergyLoss.Value));
                b.Penetrations++;
                // Trou de sortie de l'autre côté de la planche.
                var back = new Ray(point + dir * 0.6f, -dir);
                if (fx && col.Raycast(back, out var exit, 0.6f))
                    Effects.Ensure().Impact(SurfaceKind.Wood, exit.point, exit.normal, dir, col, exitHole: true);
                return HitResult.Continue;
            }

            // Ricochet sur pierre/métal à incidence rasante : cosmétique uniquement (aucun dégât reporté ensuite).
            if ((kind == SurfaceKind.Stone || kind == SurfaceKind.Metal) && !b.Ricocheted && Vector3.Dot(dir, normal) > -0.26f && Random.value < 0.65f)
            {
                b.Ricocheted = true;
                b.Vel = (Vector3.Reflect(b.Vel, normal) * 0.45f) + Random.insideUnitSphere * 25f;
                b.Pos = point + normal * 0.02f;
                b.Energy *= 0.3f;
                return HitResult.Redirect;
            }
            return HitResult.Stop;
        }

        private void CheckFlyby(Bullet b, Vector3 a, Vector3 c)
        {
            if (b.LocalShooter || b.FlybyDone || IsDedicated()) return;
            var cam = GameCamera.instance;
            if (!cam) return;
            var l = cam.transform.position;
            var ab = c - a;
            float t = Mathf.Clamp01(Vector3.Dot(l - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            var p = a + ab * t;
            float d = Vector3.Distance(p, l);
            if (t > 0f && t < 1f && d < 4f)
            {
                b.FlybyDone = true;
                GunAudio.Play(GunAudio.Get("flyby"), p, Mathf.Lerp(1f, 0.4f, d / 4f), 20f, pitchJitter: 0.1f);
            }
        }

        private static void UpdateTracer(Bullet b)
        {
            if (!b.Tracer) return;
            // La traçante part de la bouche du canon et rejoint la trajectoire réelle (issue de l'œil) sur ~10 m.
            float blend = Mathf.Clamp01(1f - b.Traveled / 10f);
            var head = b.Pos + b.VisualOffset * blend;
            var dir = b.Vel.sqrMagnitude > 1 ? b.Vel.normalized : Vector3.forward;
            float len = Mathf.Min(12f, Vector3.Distance(head, b.Origin + b.VisualOffset));
            var tail = head - dir * len;
            b.Tracer.SetPosition(0, tail);
            b.Tracer.SetPosition(1, head);
        }

        // ------------------------------------------------------------ règles partagées client / serveur

        public static float BaseMaxHealth(float prefabHealth, int level) => prefabHealth * Mathf.Max(1, level);

        public static bool CanPenetrate(float maxHealth, int penetrationsSoFar) =>
            penetrationsSoFar < FirearmsConfig.MaxPenetrations.Value && maxHealth <= FirearmsConfig.PenetrationMaxTargetHealth.Value;

        public static float PenetrationLoss(float maxHealth) =>
            Mathf.Clamp01(FirearmsConfig.PenetrationEnergyLoss.Value *
                          (0.5f + 0.5f * maxHealth / Mathf.Max(1f, FirearmsConfig.PenetrationMaxTargetHealth.Value)));

        /// <summary>Fraction de vitesse restante après une distance d (traînée quadratique, gravité négligée).</summary>
        public static float SpeedFraction(float distance) => Mathf.Exp(-FirearmsConfig.Drag.Value * distance);

        public static float Damage(float distance, float energy, bool headshot)
        {
            float speed = SpeedFraction(distance);
            float e = speed * speed * energy;
            float dmg = FirearmsConfig.Damage.Value * Mathf.Max(FirearmsConfig.MinDamageFraction.Value, Mathf.Sqrt(e));
            return headshot ? dmg * FirearmsConfig.HeadshotMultiplier.Value : dmg;
        }

        public static bool IsHeadshot(Character ch, Vector3 point)
        {
            if (!ch.m_head) return false;
            float r = Mathf.Clamp(ch.GetRadius() * 0.6f, 0.17f, 1.2f);
            return (point - ch.GetHeadPoint()).sqrMagnitude <= r * r * 1.4f;
        }

        // ------------------------------------------------------------ surfaces

        private static readonly string[] Bloodless = { "Skeleton", "Golem", "Ghost", "Wraith", "Charred", "Surtling", "Blob", "Bonemass", "Dverger_Rogue_Ice" };

        private static SurfaceKind FleshKind(Character ch)
        {
            var n = ch.name;
            foreach (var k in Bloodless)
                if (n.Contains(k)) return k == "Golem" ? SurfaceKind.Stone : SurfaceKind.Generic;
            return SurfaceKind.Flesh;
        }

        public static SurfaceKind Classify(Collider col)
        {
            if (col.GetComponentInParent<Heightmap>()) return SurfaceKind.Dirt;
            var wnt = col.GetComponentInParent<WearNTear>();
            if (wnt)
            {
                switch (wnt.m_materialType)
                {
                    case WearNTear.MaterialType.Wood:
                    case WearNTear.MaterialType.HardWood:
                    case WearNTear.MaterialType.Timberwood: return SurfaceKind.Wood;
                    case WearNTear.MaterialType.Iron: return SurfaceKind.Metal;
                    default: return SurfaceKind.Stone;
                }
            }
            if (col.GetComponentInParent<TreeBase>() || col.GetComponentInParent<TreeLog>() || col.GetComponentInParent<Ship>()) return SurfaceKind.Wood;
            if (col.GetComponentInParent<MineRock>() || col.GetComponentInParent<MineRock5>()) return SurfaceKind.Stone;
            var d = col.GetComponentInParent<Destructible>();
            if (d && d.m_destructibleType == DestructibleType.Tree) return SurfaceKind.Wood;
            if (col.gameObject.layer == LayerMask.NameToLayer("static_solid")) return SurfaceKind.Stone;

            var name = (col.transform.root.name + "/" + col.name).ToLowerInvariant();
            var mat = col.GetComponent<Renderer>()?.sharedMaterial;
            if (mat) name += "/" + mat.name.ToLowerInvariant();
            if (Has(name, "bush", "leaf", "leaves", "shrub", "vines", "grass", "fern")) return SurfaceKind.Foliage;
            if (Has(name, "rock", "stone", "cliff", "boulder", "marble", "obsidian", "flint", "copper", "tin", "silver")) return SurfaceKind.Stone;
            if (Has(name, "wood", "log", "tree", "plank", "beam", "stump", "branch", "barrel", "crate", "chest")) return SurfaceKind.Wood;
            if (Has(name, "metal", "iron", "bronze", "anvil")) return SurfaceKind.Metal;
            if (Has(name, "dirt", "mud", "soil", "ground")) return SurfaceKind.Dirt;
            return SurfaceKind.Generic;
        }

        private static bool IsThinWood(Collider col)
        {
            var w = col.GetComponentInParent<WearNTear>();
            return w && w.m_materialType != WearNTear.MaterialType.HardWood;
        }

        private static bool Has(string s, params string[] keys)
        {
            foreach (var k in keys) if (s.Contains(k)) return true;
            return false;
        }
    }
}

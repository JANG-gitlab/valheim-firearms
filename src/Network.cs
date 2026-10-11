using System.Collections.Generic;
using UnityEngine;

namespace ValheimFirearms
{
    /// <summary>
    /// Flux réseau (le serveur fait autorité) :
    ///   client --VFA_Shot--> serveur : origine, direction (dispersion incluse), id du tir
    ///   serveur : vérifie identité, position, cadence, chargeur, rechargement ; enregistre le tir
    ///   serveur --VFA_ShotFx--> tout le monde : les autres clients rejouent la balle (traçante, son, impacts)
    ///   client --VFA_Hit--> serveur : impact sur une créature (point, tête, ordre de pénétration)
    ///   serveur : vérifie la cohérence avec le tir enregistré et la position de la cible (ZDO), CALCULE les dégâts,
    ///             puis envoie le RPC_Damage vanilla au propriétaire de la cible (résistances, armure, PvP, mort…)
    ///   serveur --VFA_HitConfirm--> tireur : marqueur de touche + progression de compétence
    /// </summary>
    internal static class Network
    {
        private const string RpcShot = "VFA_Shot", RpcHit = "VFA_Hit", RpcReload = "VFA_Reload",
            RpcShotFx = "VFA_ShotFx", RpcReloadFx = "VFA_ReloadFx", RpcConfirm = "VFA_HitConfirm";

        public static void Register()
        {
            var r = ZRoutedRpc.instance;
            if (r == null) return;
            r.Register<ZPackage>(RpcShot, Server_OnShot);
            r.Register<ZPackage>(RpcHit, Server_OnHit);
            r.Register<ZPackage>(RpcReload, Server_OnReload);
            r.Register<ZPackage>(RpcShotFx, Client_OnShotFx);
            r.Register<ZPackage>(RpcReloadFx, Client_OnReloadFx);
            r.Register<ZPackage>(RpcConfirm, Client_OnConfirm);
            Peers.Clear();
            Plugin.Log.LogInfo("RPC armes à feu enregistrés");
        }

        private static long ServerId => ZRoutedRpc.instance.GetServerPeerID();

        // ================================================================= côté client (tireur)

        public static void SendShot(int shotId, Vector3 origin, Vector3 dir, bool tracer)
        {
            var p = new ZPackage();
            p.Write(shotId); p.Write(Player.m_localPlayer.GetZDOID()); p.Write(origin); p.Write(dir); p.Write(tracer);
            ZRoutedRpc.instance.InvokeRoutedRPC(ServerId, RpcShot, p);
        }

        public static void ReportHit(int shotId, int hitIndex, Character target, Vector3 point, bool head, short weakSpot, float distance, float energy)
        {
            var id = target.GetZDOID();
            if (id.IsNone()) return;
            var p = new ZPackage();
            p.Write(shotId); p.Write(hitIndex); p.Write(id); p.Write(point); p.Write(head); p.Write((int)weakSpot); p.Write(distance); p.Write(energy);
            ZRoutedRpc.instance.InvokeRoutedRPC(ServerId, RpcHit, p);
        }

        public static void SendReload(bool empty)
        {
            var p = new ZPackage();
            p.Write(Player.m_localPlayer.GetZDOID()); p.Write(empty);
            ZRoutedRpc.instance.InvokeRoutedRPC(ServerId, RpcReload, p);
        }

        // ================================================================= côté serveur

        private class ShotRec
        {
            public Vector3 Origin, Dir; public float Time, Energy = 1f; public int Hits, Pens; public bool Stopped;
            public ZDOID Shooter; public readonly HashSet<ZDOID> Targets = new HashSet<ZDOID>();
        }

        private class PeerState
        {
            public float Tokens = 4f, LastRefill, ReloadStart = -1f, ReloadDur, LastWarn = -100f, LastSeen;
            public int Rounds = -1, RoundsAfterReload;
            public readonly Dictionary<int, ShotRec> Shots = new Dictionary<int, ShotRec>();
        }

        private static readonly Dictionary<long, PeerState> Peers = new Dictionary<long, PeerState>();

        private static bool IsServer => ZNet.instance && ZNet.instance.IsServer();

        private static PeerState State(long peer)
        {
            if (!Peers.TryGetValue(peer, out var s)) Peers[peer] = s = new PeerState { LastRefill = Time.time };
            s.LastSeen = Time.time;
            return s;
        }

        /// <summary>Identité et position du personnage d'un pair, vues par le serveur.</summary>
        private static bool ShooterInfo(long sender, out ZDOID characterId, out Vector3 pos)
        {
            characterId = ZDOID.None; pos = Vector3.zero;
            if (sender == ZNet.GetUID())
            {
                var lp = Player.m_localPlayer;
                if (!lp) return false;
                characterId = lp.GetZDOID(); pos = lp.transform.position;
                return true;
            }
            var peer = ZNet.instance.GetPeer(sender);
            if (peer == null || peer.m_characterID.IsNone()) return false;
            characterId = peer.m_characterID;
            var zdo = ZDOMan.instance.GetZDO(characterId);
            pos = zdo != null ? zdo.GetPosition() : peer.m_refPos;
            return true;
        }

        private static void Reject(long sender, PeerState st, string why)
        {
            if (FirearmsConfig.DebugHits.Value || Time.time - st.LastWarn > 5f)
            {
                st.LastWarn = Time.time;
                Plugin.Log.LogWarning($"Tir refusé ({sender}) : {why}");
            }
        }

        private static void Server_OnShot(long sender, ZPackage p)
        {
            if (!IsServer) return;
            int shotId = p.ReadInt(); var shooter = p.ReadZDOID(); var origin = p.ReadVector3(); var dir = p.ReadVector3().normalized; bool tracer = p.ReadBool();
            var st = State(sender);
            if (!ShooterInfo(sender, out var charId, out var pos)) { Reject(sender, st, "tireur inconnu"); return; }
            if (shooter != charId) { Reject(sender, st, "identité du tireur incohérente"); return; }

            float now = Time.time;
            if (FirearmsConfig.ServerValidation.Value)
            {
                if (Vector3.Distance(origin, pos + Vector3.up * 1.5f) > FirearmsConfig.MaxOriginError.Value)
                { Reject(sender, st, $"origine à {Vector3.Distance(origin, pos):F1} m du joueur"); return; }

                // Seau à jetons : cadence nominale × tolérance, petite réserve pour la gigue réseau.
                float rate = FirearmsConfig.RoundsPerMinute.Value / 60f * FirearmsConfig.FireRateTolerance.Value;
                st.Tokens = Mathf.Min(4f, st.Tokens + (now - st.LastRefill) * rate);
                st.LastRefill = now;
                if (st.Tokens < 1f) { Reject(sender, st, "cadence trop élevée"); return; }

                if (st.ReloadStart >= 0f)
                {
                    if (now - st.ReloadStart < st.ReloadDur * 0.7f) { Reject(sender, st, "tir pendant le rechargement"); return; }
                    st.Rounds = st.RoundsAfterReload; st.ReloadStart = -1f;
                }
                if (st.Rounds < 0) st.Rounds = FirearmsConfig.MagazineSize.Value + 1; // premier tir vu : on fait confiance au chargeur en cours
                if (st.Rounds <= 0) { Reject(sender, st, "chargeur vide"); return; }
                st.Tokens -= 1f;
                st.Rounds--;
            }

            st.Shots[shotId] = new ShotRec { Origin = origin, Dir = dir, Time = now, Shooter = charId };
            if (st.Shots.Count > 64)
            {
                var old = new List<int>();
                foreach (var kv in st.Shots) if (now - kv.Value.Time > 5f) old.Add(kv.Key);
                foreach (var k in old) st.Shots.Remove(k);
            }

            var fx = new ZPackage();
            fx.Write(sender); fx.Write(charId); fx.Write(origin); fx.Write(dir); fx.Write(tracer);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcShotFx, fx);
        }

        private static void Server_OnHit(long sender, ZPackage p)
        {
            if (!IsServer) return;
            int shotId = p.ReadInt(), hitIndex = p.ReadInt(); var target = p.ReadZDOID(); var point = p.ReadVector3();
            bool head = p.ReadBool(); int weak = p.ReadInt(); float dist = p.ReadSingle(), claimedEnergy = p.ReadSingle();
            var st = State(sender);
            if (!st.Shots.TryGetValue(shotId, out var rec)) { Reject(sender, st, $"impact d'un tir inconnu #{shotId}"); return; }

            var zdo = ZDOMan.instance.GetZDO(target);
            var prefab = zdo != null ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
            var ch = prefab ? prefab.GetComponent<Character>() : null;
            if (!ch) { Reject(sender, st, "cible invalide"); return; }
            if (target == rec.Shooter) { Reject(sender, st, "auto-tir"); return; }

            if (FirearmsConfig.ServerValidation.Value)
            {
                if (rec.Stopped || hitIndex != rec.Hits || rec.Targets.Contains(target)) { Reject(sender, st, "ordre des impacts incohérent"); return; }
                if (Time.time - rec.Time > 3f) { Reject(sender, st, "impact trop tardif"); return; }
                if (dist > FirearmsConfig.MaxRange.Value + 5f || Vector3.Distance(point, rec.Origin) > dist + 2f) { Reject(sender, st, "distance incohérente"); return; }

                // Le point doit être sur la trajectoire annoncée (écart permis = chute balistique + marge).
                float t = Vector3.Dot(point - rec.Origin, rec.Dir);
                float off = Vector3.Distance(point, rec.Origin + rec.Dir * t);
                float tof = t / Mathf.Max(1f, FirearmsConfig.MuzzleVelocity.Value);
                float drop = 0.5f * 9.81f * FirearmsConfig.GravityScale.Value * tof * tof;
                if (t < -1f || off > 1f + drop * 1.6f + t * 0.006f) { Reject(sender, st, $"impact hors trajectoire ({off:F2} m)"); return; }

                // Et proche de la cible telle que le serveur la connaît.
                Dimensions(ch, prefab, out float height, out float radius);
                var tp = zdo.GetPosition();
                var flat = new Vector2(point.x - tp.x, point.z - tp.z).magnitude;
                float tol = FirearmsConfig.HitTolerance.Value;
                if (flat > radius + tol || point.y < tp.y - tol || point.y > tp.y + height + tol) { Reject(sender, st, $"impact loin de la cible ({flat:F1} m)"); return; }
                if (head && point.y < tp.y + height * 0.45f) head = false;
            }

            int level = zdo.GetInt(ZDOVars.s_level, 1);
            float maxH = Ballistics.BaseMaxHealth(ch.m_health, level);
            float energy = Mathf.Min(rec.Energy, Mathf.Clamp01(claimedEnergy));
            float dmg = Ballistics.Damage(dist, energy, head);

            rec.Hits++; rec.Targets.Add(target);
            if (Ballistics.CanPenetrate(maxH, rec.Pens)) { rec.Energy = energy * (1f - Ballistics.PenetrationLoss(maxH)); rec.Pens++; }
            else rec.Stopped = true;

            var hit = new HitData
            {
                m_point = point, m_dir = rec.Dir, m_attacker = rec.Shooter, m_skill = Skills.SkillType.Crossbows,
                m_ranged = true, m_blockable = true, m_dodgeable = true, m_pushForce = FirearmsConfig.PushForce.Value,
                m_backstabBonus = 3f, m_staggerMultiplier = 1f, m_hitType = HitData.HitType.PlayerHit, m_weakSpot = (short)weak,
            };
            // Dégâts « bruts » (m_damage) : HitData.ApplyResistance ne les modifie jamais, donc ni résistance ni
            // immunité de la cible (mobs, boss, points faibles). Sinon : perforant classique.
            if (FirearmsConfig.IgnoreResistances.Value) hit.m_damage.m_damage = dmg;
            else hit.m_damage.m_pierce = dmg;
            // Le RPC_Damage vanilla ignore les tirs de joueur sur un joueur sans PvP, sauf si ce drapeau est mis.
            hit.m_ignorePVP = FirearmsConfig.IgnorePvP.Value;
            long owner = zdo.GetOwner();
            if (owner != 0) ZRoutedRpc.instance.InvokeRoutedRPC(owner, target, "RPC_Damage", hit);

            if (FirearmsConfig.DebugHits.Value)
                Plugin.Log.LogInfo($"[serveur] tir #{shotId} de {sender} -> {prefab.name} {dmg:F1} dégâts (dist {dist:F0} m, énergie {energy:F2}, tête {head})");

            var c = new ZPackage();
            c.Write(target); c.Write(dmg); c.Write(head);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcConfirm, c);
        }

        private static void Dimensions(Character ch, GameObject prefab, out float height, out float radius)
        {
            var cap = prefab.GetComponent<CapsuleCollider>();
            float s = prefab.transform.localScale.y;
            height = cap ? Mathf.Max(cap.height, cap.radius * 2f) * s : 2f;
            radius = cap ? cap.radius * s : 0.5f;
        }

        private static void Server_OnReload(long sender, ZPackage p)
        {
            if (!IsServer) return;
            var shooter = p.ReadZDOID(); bool empty = p.ReadBool();
            var st = State(sender);
            if (!ShooterInfo(sender, out var charId, out _) || charId != shooter) { Reject(sender, st, "rechargement : identité incohérente"); return; }
            st.ReloadStart = Time.time;
            st.ReloadDur = empty ? FirearmsConfig.ReloadTime.Value : FirearmsConfig.TacticalReloadTime.Value;
            st.RoundsAfterReload = FirearmsConfig.MagazineSize.Value + (empty ? 0 : 1);

            var fx = new ZPackage();
            fx.Write(sender); fx.Write(charId); fx.Write(empty); fx.Write(st.ReloadDur);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcReloadFx, fx);
        }

        // ================================================================= côté clients (effets)

        private static bool FromServer(long sender) => sender == ServerId || sender == ZNet.GetUID() && IsServer;

        private static void Client_OnShotFx(long sender, ZPackage p)
        {
            if (ZNet.instance.IsDedicated() || !FromServer(sender)) return;
            long shooterPeer = p.ReadLong(); var shooterId = p.ReadZDOID(); var origin = p.ReadVector3(); var dir = p.ReadVector3(); bool tracer = p.ReadBool();
            if (shooterPeer == ZNet.GetUID()) return; // nos propres tirs sont déjà simulés localement

            var go = ZNetScene.instance.FindInstance(shooterId);
            var shooter = go ? go.GetComponent<Player>() : null;
            var visual = GunVisual.For(shooter);
            Vector3 muzzle = origin;
            if (visual) muzzle = visual.Fire(shooter.GetVelocity());
            GunAudio.PlayShot(muzzle);
            Ballistics.Ensure().Spawn(origin, dir, muzzle, shooter, authoritative: false, shotId: 0, tracer: tracer);
        }

        private static void Client_OnReloadFx(long sender, ZPackage p)
        {
            if (ZNet.instance.IsDedicated() || !FromServer(sender)) return;
            long shooterPeer = p.ReadLong(); var shooterId = p.ReadZDOID(); bool empty = p.ReadBool(); float dur = p.ReadSingle();
            if (shooterPeer == ZNet.GetUID()) return;
            var go = ZNetScene.instance.FindInstance(shooterId);
            var shooter = go ? go.GetComponent<Player>() : null;
            var visual = GunVisual.For(shooter);
            if (visual) visual.PlayReload(dur, empty);
        }

        private static void Client_OnConfirm(long sender, ZPackage p)
        {
            if (!FromServer(sender)) return;
            var target = p.ReadZDOID(); float dmg = p.ReadSingle(); bool head = p.ReadBool();
            WeaponController.Instance?.OnHitConfirmed(target, dmg, head);
        }
    }
}

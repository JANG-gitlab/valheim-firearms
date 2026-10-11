using UnityEngine;

namespace ValheimFirearms
{
    /// <summary>
    /// Logique de tir du joueur local : cadence, modes, chargeur, rechargement, recul, dispersion, visée, HUD.
    /// Les entrées viennent de Player.SetControls (patché) : clic gauche = tir, clic droit (« Bloquer ») = viser.
    /// </summary>
    internal class WeaponController : MonoBehaviour
    {
        public static WeaponController Instance { get; private set; }
        private const string MagKey = "vfa_mag", ModeKey = "vfa_mode";
        private static readonly RaycastHit[] AimBuf = new RaycastHit[32];

        private bool _fireHeld, _aimHeld, _firePrev, _semiFired;
        private float _nextShot, _lastShotTime = -10f, _spreadAccum, _pendingV, _pendingH, _recoilDebt, _aim, _lastPitch;
        private bool _havePitch;
        private int _shotId, _burst;
        private bool _reloading, _reloadEmpty;
        private float _reloadStart, _reloadDur;
        private ItemDrop.ItemData _reloadItem;
        private float _hitMarkerT = -10f; private bool _hitHead;
        private int _reserveCache; private float _reserveT;
        private GUIStyle _big, _small;

        public float Aim => _aim;
        public bool WantsFacing => _aim > 0.05f || _fireHeld || Time.time - _lastShotTime < 1f;

        public static void Ensure()
        {
            if (Instance) return;
            Instance = new GameObject("VFA_WeaponController").AddComponent<WeaponController>();
        }

        public static bool IsGun(ItemDrop.ItemData item) => item != null && item.m_shared.m_name == Items.RifleToken;

        public static ItemDrop.ItemData GetGun(Humanoid h)
        {
            if (!h) return null;
            var w = h.GetCurrentWeapon();
            return IsGun(w) ? w : null;
        }

        public void SetInput(bool fire, bool aim) { _fireHeld = fire; _aimHeld = aim; }

        public static int GetMag(ItemDrop.ItemData item) =>
            item.m_customData.TryGetValue(MagKey, out var s) && int.TryParse(s, out var n) ? n : 0;

        private static void SetMag(ItemDrop.ItemData item, int n) => item.m_customData[MagKey] = n.ToString();

        private static bool IsAuto(ItemDrop.ItemData item) => !item.m_customData.TryGetValue(ModeKey, out var s) || s != "semi";

        private int Reserve(Player p)
        {
            if (Time.time - _reserveT > 0.25f) { _reserveT = Time.time; _reserveCache = p.GetInventory().CountItems(Items.AmmoToken); }
            return _reserveCache;
        }

        private void Update()
        {
            var p = Player.m_localPlayer;
            var gun = p ? GetGun(p) : null;
            float dt = Time.deltaTime;
            if (gun == null)
            {
                if (_reloading) CancelReload(p);
                _fireHeld = _aimHeld = false;
                _aim = Mathf.MoveTowards(_aim, 0, dt * 6f);
                _havePitch = false;
                return;
            }

            bool input = p.TakeInput();
            if (input && ZInput.GetKeyDown(FirearmsConfig.ReloadKey.Value, false)) TryReload(p, gun);
            if (input && ZInput.GetKeyDown(FirearmsConfig.FireModeKey.Value, false)) ToggleMode(p, gun);
            UpdateReload(p, gun);

            bool canAct = input && !_reloading && !p.InMinorAction() && !p.InDodge() && !p.IsStaggering() && !p.IsSwimming()
                          && !p.InPlaceMode() && !p.InEmote() && !p.IsDead() && !p.IsSleeping();
            if (!_fireHeld) _semiFired = false;
            if (_fireHeld && canAct)
            {
                bool pressed = !_firePrev;
                int mag = GetMag(gun);
                if (mag <= 0)
                {
                    if (pressed)
                    {
                        GunAudio.Play(GunAudio.Get("dryfire"), p.transform.position + Vector3.up, 0.8f, 15f);
                        p.Message(MessageHud.MessageType.Center, "$vfa_msg_empty [" + FirearmsConfig.ReloadKey.Value + "]");
                    }
                }
                else if (gun.m_shared.m_useDurability && gun.m_durability <= 0f)
                {
                    if (pressed) p.Message(MessageHud.MessageType.Center, "$vfa_msg_broken");
                }
                else if ((IsAuto(gun) || !_semiFired) && Time.time >= _nextShot)
                {
                    float interval = 60f / Mathf.Max(1, FirearmsConfig.RoundsPerMinute.Value);
                    // Garde la cadence exacte même si la frame tombe entre deux coups.
                    _nextShot = (Time.time - _nextShot < interval ? _nextShot : Time.time) + interval;
                    _semiFired = true;
                    Fire(p, gun, mag);
                }
            }
            _firePrev = _fireHeld;
            if (Time.time - _lastShotTime > 0.35f) _burst = 0;

            UpdateRecoil(p, dt);
            if (Time.time - _lastShotTime > 0.08f) _spreadAccum = Mathf.Max(0f, _spreadAccum - FirearmsConfig.SpreadRecovery.Value * dt);
            _aim = Mathf.MoveTowards(_aim, _aimHeld && input && !_reloading && !p.IsRunning() ? 1f : 0f, dt * 6f);
        }

        // ------------------------------------------------------------------ tir

        private void Fire(Player p, ItemDrop.ItemData gun, int mag)
        {
            SetMag(gun, mag - 1);
            if (gun.m_shared.m_useDurability) gun.m_durability = Mathf.Max(0f, gun.m_durability - 0.25f);

            var eye = p.m_eye.position;
            var aimPoint = FindAimPoint(p, eye);
            var dir = (aimPoint - eye).normalized;
            // Hausse réglée à 100 m : la balle croise la ligne de visée à 100 m puis chute.
            float tof = 100f / Mathf.Max(50f, FirearmsConfig.MuzzleVelocity.Value);
            dir = (dir + Vector3.up * (0.5f * 9.81f * FirearmsConfig.GravityScale.Value * tof * tof / 100f)).normalized;
            float spread = CurrentSpread(p);
            var off = Random.insideUnitCircle * spread;
            dir = Quaternion.LookRotation(dir) * Quaternion.Euler(off.y, off.x, 0f) * Vector3.forward;

            var visual = GunVisual.For(p);
            var muzzle = visual ? visual.Fire(p.GetVelocity()) : eye;
            _shotId++;
            bool tracer = FirearmsConfig.TracerEveryNth.Value > 0 && _shotId % FirearmsConfig.TracerEveryNth.Value == 0;
            Network.SendShot(_shotId, eye, dir, tracer);
            Ballistics.Ensure().Spawn(eye, dir, muzzle, p, authoritative: true, shotId: _shotId, tracer: tracer);
            GunAudio.PlayShot(muzzle);
            p.AddNoise(60f);
            if (GameCamera.instance) GameCamera.instance.AddShake(muzzle, 8f, 0.12f, false);

            // Recul : relevé vertical qui s'accentue un peu en rafale, dérive horizontale aléatoire (légère tendance à droite).
            float m = Mathf.Lerp(1f, FirearmsConfig.AimRecoilMultiplier.Value, _aim) * (p.IsCrouching() ? 0.85f : 1f);
            float kickV = FirearmsConfig.RecoilVertical.Value * m * (1f + 0.25f * Mathf.Min(_burst, 8) / 8f);
            float h = FirearmsConfig.RecoilHorizontal.Value * m;
            _pendingV += kickV;
            _pendingH += Random.Range(-h, h) + h * 0.25f;
            _recoilDebt += kickV * FirearmsConfig.RecoilReturnFraction.Value;
            _spreadAccum = Mathf.Min(FirearmsConfig.MaxSpread.Value, _spreadAccum + FirearmsConfig.SpreadPerShot.Value);
            _lastShotTime = Time.time;
            _burst++;
            if (FirearmsConfig.DebugHits.Value) Plugin.Log.LogInfo($"[tir {_shotId}] dispersion {spread:F2}° chargeur {mag - 1}");
        }

        /// <summary>Point visé : premier obstacle sous le réticule, en ignorant le joueur et ce qui est entre la caméra et lui.</summary>
        private static Vector3 FindAimPoint(Player p, Vector3 eye)
        {
            var cam = GameCamera.instance ? GameCamera.instance.m_camera : Camera.main;
            if (!cam) return eye + p.m_eye.forward * 100f;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float minDist = Vector3.Dot(eye - ray.origin, ray.direction) + 0.3f;
            float max = FirearmsConfig.MaxRange.Value;
            int n = Physics.RaycastNonAlloc(ray, AimBuf, max, Ballistics.SolidMask, QueryTriggerInteraction.Ignore);
            float best = max; bool found = false;
            for (int i = 0; i < n; i++)
            {
                var hit = AimBuf[i];
                if (hit.distance < minDist || hit.distance >= best) continue;
                if (hit.collider.GetComponentInParent<Character>() == p) continue;
                best = hit.distance; found = true;
            }
            return ray.GetPoint(found ? best : max);
        }

        private float CurrentSpread(Player p)
        {
            float mult = Mathf.Lerp(1f, FirearmsConfig.AimSpreadMultiplier.Value, _aim);
            float speed = p.GetVelocity().magnitude;
            if (!p.IsOnGround()) mult *= FirearmsConfig.MoveSpreadMultiplier.Value * 1.3f;
            else if (speed > 0.5f) mult *= Mathf.Lerp(1f, FirearmsConfig.MoveSpreadMultiplier.Value, Mathf.Clamp01(speed / 6f));
            if (p.IsCrouching()) mult *= 0.8f;
            return (FirearmsConfig.BaseSpread.Value + _spreadAccum) * mult;
        }

        private void UpdateRecoil(Player p, float dt)
        {
            // Si le joueur compense à la souris (vers le bas), cette part du relevé n'a plus à revenir toute seule.
            if (_havePitch)
            {
                float user = p.m_lookPitch - _lastPitch;
                if (user > 0f) _recoilDebt = Mathf.Max(0f, _recoilDebt - user);
            }
            // Le coup est appliqué sur ~40 ms plutôt qu'en une frame : plus lisible, même amplitude.
            float k = 1f - Mathf.Exp(-dt * 40f);
            float dv = _pendingV * k, dh = _pendingH * k;
            _pendingV -= dv; _pendingH -= dh;
            float rate = FirearmsConfig.RecoilRecovery.Value * (Time.time - _lastShotTime < 0.12f ? 0.25f : 1f);
            float back = _recoilDebt * (1f - Mathf.Exp(-dt * rate));
            _recoilDebt -= back;
            if (dv != 0f || dh != 0f || back != 0f) p.SetMouseLook(new Vector2(dh, dv - back));
            _lastPitch = p.m_lookPitch;
            _havePitch = true;
        }

        // ------------------------------------------------------------------ rechargement / modes

        private void TryReload(Player p, ItemDrop.ItemData gun)
        {
            if (_reloading || p.InMinorAction()) return;
            int mag = GetMag(gun), cap = FirearmsConfig.MagazineSize.Value;
            if (mag >= cap) return;
            if (Reserve(p) <= 0) { _reserveT = 0; if (Reserve(p) <= 0) { p.Message(MessageHud.MessageType.Center, "$vfa_msg_noammo"); return; } }
            _reloadEmpty = mag == 0;
            _reloadDur = _reloadEmpty ? FirearmsConfig.ReloadTime.Value : FirearmsConfig.TacticalReloadTime.Value;
            _reloadStart = Time.time; _reloading = true; _reloadItem = gun;
            // Action mineure vanilla : barre de progression, animation de rechargement d'arbalète, annulée si on change d'arme.
            p.m_actionQueue.Add(new Player.MinorActionData
            {
                m_item = gun, m_type = Player.MinorActionData.ActionType.Reload, m_duration = _reloadDur,
                m_progressText = "$hud_reloading " + gun.m_shared.m_name,
                m_animation = "reload_crossbow", m_doneAnimation = "reload_crossbow_done",
            });
            GunVisual.For(p)?.PlayReload(_reloadDur, _reloadEmpty);
            Network.SendReload(_reloadEmpty);
        }

        private void UpdateReload(Player p, ItemDrop.ItemData gun)
        {
            if (!_reloading) return;
            if (gun != _reloadItem) { CancelReload(p); return; }
            if (p.IsReloadActionQueued()) return;
            if (Time.time - _reloadStart < _reloadDur - 0.15f) { CancelReload(p); return; }
            _reloading = false;
            int mag = GetMag(gun);
            int cap = FirearmsConfig.MagazineSize.Value + (_reloadEmpty ? 0 : 1); // une cartouche reste chambrée
            _reserveT = 0;
            int take = Mathf.Min(cap - mag, Reserve(p));
            if (take > 0) p.GetInventory().RemoveItem(Items.AmmoToken, take);
            SetMag(gun, mag + Mathf.Max(0, take));
            _reserveT = 0;
        }

        private void CancelReload(Player p)
        {
            _reloading = false;
            if (p) GunVisual.For(p)?.CancelReload();
        }

        private void ToggleMode(Player p, ItemDrop.ItemData gun)
        {
            bool auto = !IsAuto(gun);
            gun.m_customData[ModeKey] = auto ? "auto" : "semi";
            GunAudio.Play(GunAudio.Get("dryfire"), p.transform.position + Vector3.up, 0.35f, 10f, pitchJitter: 0f);
            p.Message(MessageHud.MessageType.TopLeft, auto ? "$vfa_mode_auto" : "$vfa_mode_semi");
        }

        public void OnHitConfirmed(ZDOID target, float damage, bool head)
        {
            _hitMarkerT = Time.time; _hitHead = head;
            Player.m_localPlayer?.RaiseSkill(Skills.SkillType.Crossbows, head ? 0.5f : 0.25f);
            if (head) GunAudio.Play(GunAudio.Get("impact_flesh_2"), Player.m_localPlayer.transform.position, 0.25f, 5f, pitchJitter: 0f);
        }

        // ------------------------------------------------------------------ HUD

        private void OnGUI()
        {
            var p = Player.m_localPlayer;
            var gun = p ? GetGun(p) : null;
            if (gun == null || Hud.instance == null || Hud.IsUserHidden() || InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen()) return;
            float s = Screen.height / 1080f;
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
                _small = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            }
            _big.fontSize = Mathf.RoundToInt(42 * s); _small.fontSize = Mathf.RoundToInt(20 * s);

            int mag = GetMag(gun), reserve = Reserve(p);
            var area = new Rect(Screen.width - 420 * s, Screen.height - 230 * s, 380 * s, 60 * s);
            Shadowed(area, $"{mag} <size={Mathf.RoundToInt(26 * s)}>/ {reserve}</size>", _big, mag == 0 ? new Color(1f, 0.35f, 0.3f) : Color.white);
            var area2 = new Rect(area.x, area.y + 58 * s, area.width, 26 * s);
            Shadowed(area2, (IsAuto(gun) ? "AUTO" : "SEMI") + (_reloading ? "  ·  RECHARGEMENT" : ""), _small, new Color(1f, 0.85f, 0.55f));

            // Indicateur de dispersion : 4 traits autour du réticule, écartés selon le cône réel.
            var cam = GameCamera.instance ? GameCamera.instance.m_camera : Camera.main;
            if (cam)
            {
                float spread = CurrentSpread(p);
                float px = Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Screen.height * 0.5f;
                float gap = 8f * s + px, len = 7f * s, w = Mathf.Max(1f, 2f * s);
                float cx = Screen.width / 2f, cy = Screen.height / 2f;
                GUI.color = new Color(1, 1, 1, 0.75f);
                GUI.DrawTexture(new Rect(cx - w / 2, cy - gap - len, w, len), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - w / 2, cy + gap, w, len), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - gap - len, cy - w / 2, len, w), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx + gap, cy - w / 2, len, w), Texture2D.whiteTexture);

                float hm = Time.time - _hitMarkerT;
                if (hm < 0.3f)
                {
                    GUI.color = _hitHead ? new Color(1f, 0.25f, 0.2f, 1f - hm / 0.3f) : new Color(1f, 1f, 1f, 1f - hm / 0.3f);
                    for (int i = 0; i < 4; i++)
                    {
                        var m = GUI.matrix;
                        GUIUtility.RotateAroundPivot(45f + 90f * i, new Vector2(cx, cy));
                        GUI.DrawTexture(new Rect(cx + 7 * s, cy - w / 2, 9 * s, w * 1.5f), Texture2D.whiteTexture);
                        GUI.matrix = m;
                    }
                }
                GUI.color = Color.white;
            }
        }

        private static void Shadowed(Rect r, string text, GUIStyle st, Color c)
        {
            st.richText = true;
            st.normal.textColor = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, st);
            st.normal.textColor = c;
            GUI.Label(r, text, st);
        }
    }
}

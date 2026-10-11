using System.Collections;
using UnityEngine;

namespace ValheimFirearms
{
    /// <summary>
    /// Posé sur le modèle de l'arme (donc sur chaque instance tenue en main, locale ou distante) :
    /// flash de bouche (vraie Light pulsée + sprites additifs), culasse qui recule, chargeur animé au rechargement.
    /// </summary>
    internal class GunVisual : MonoBehaviour
    {
        private Transform _muzzle, _ejection, _bolt, _mag, _flashRoot;
        private Light _light;
        private Renderer[] _flash;
        private Vector3 _boltRest, _magRest, _rootRest;
        private Quaternion _magRestRot;
        private float _fireT = -10f, _lightPeak, _reloadStart = -10f, _reloadDur;
        private bool _reloadEmpty;
        private Coroutine _reloadSounds;
        private static Material[] _flashMats;

        public Transform Muzzle => _muzzle;

        public void SetPose()
        {
            Items.ApplyModelPose(transform);
            _rootRest = transform.localPosition;
        }

        public static GunVisual For(Player p)
        {
            if (!p) return null;
            var ve = p.GetComponent<VisEquipment>();
            var inst = ve ? ve.m_leftItemInstance : null;
            return inst ? inst.GetComponentInChildren<GunVisual>() : null;
        }

        /// <summary>Ajoute la lumière et les sprites de flash au modèle (fait une fois sur le prefab).</summary>
        public static void Decorate(GameObject gun)
        {
            var muzzle = gun.transform.Find("muzzle");
            var lightGo = new GameObject("muzzle_light");
            lightGo.transform.SetParent(muzzle, false);
            lightGo.transform.localPosition = new Vector3(0, 0, 0.12f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.72f, 0.42f); l.intensity = 0f; l.enabled = false;
            l.renderMode = LightRenderMode.ForcePixel; // sinon Unity peut la rétrograder en lumière par sommet
            l.shadows = LightShadows.None;

            if (_flashMats == null)
            {
                _flashMats = new Material[3];
                for (int i = 0; i < 3; i++)
                    _flashMats[i] = Procedural.ParticleMaterial(Procedural.FlashTexture(17 + i * 31), true, new Color(1f, 0.85f, 0.6f, 1f));
            }
            var flashRoot = new GameObject("flash");
            flashRoot.transform.SetParent(muzzle, false);
            // Une étoile face à l'avant + deux plans croisés allongés le long du canon (cône de flamme vu de profil).
            AddFlashQuad(flashRoot.transform, "front", new Vector3(0, 0, 0.02f), Quaternion.identity, new Vector3(0.2f, 0.2f, 1));
            AddFlashQuad(flashRoot.transform, "side_h", new Vector3(0, 0, 0.11f), Quaternion.Euler(0, 90, 0), new Vector3(0.24f, 0.12f, 1));
            AddFlashQuad(flashRoot.transform, "side_v", new Vector3(0, 0, 0.11f), Quaternion.Euler(0, 90, 90), new Vector3(0.24f, 0.12f, 1));
            flashRoot.SetActive(false);
            gun.AddComponent<GunVisual>();
        }

        private static void AddFlashQuad(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = Procedural.Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _flashMats[0];
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        private void Awake()
        {
            _muzzle = transform.Find("muzzle");
            _ejection = transform.Find("ejection");
            _bolt = transform.Find("bolt");
            _mag = transform.Find("magazine");
            _flashRoot = _muzzle ? _muzzle.Find("flash") : null;
            _light = GetComponentInChildren<Light>(true);
            _flash = _flashRoot ? _flashRoot.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            if (_bolt) _boltRest = _bolt.localPosition;
            if (_mag) { _magRest = _mag.localPosition; _magRestRot = _mag.localRotation; }
            _rootRest = transform.localPosition;
        }

        /// <summary>Joue le départ du coup. Renvoie la position de la bouche du canon.</summary>
        public Vector3 Fire(Vector3 shooterVelocity)
        {
            _fireT = Time.time;
            _lightPeak = FirearmsConfig.MuzzleLightIntensity.Value * Random.Range(0.75f, 1.25f);
            if (_light)
            {
                _light.enabled = true;
                _light.range = FirearmsConfig.MuzzleLightRange.Value * Random.Range(0.85f, 1.1f);
                _light.shadows = FirearmsConfig.MuzzleLightShadows.Value ? LightShadows.Hard : LightShadows.None;
                _light.intensity = _lightPeak;
            }
            if (_flashRoot)
            {
                _flashRoot.gameObject.SetActive(true);
                _flashRoot.localRotation = Quaternion.Euler(0, 0, Random.Range(0, 360f));
                _flashRoot.localScale = Vector3.one * Random.Range(0.75f, 1.3f);
                foreach (var r in _flash) r.sharedMaterial = _flashMats[Random.Range(0, _flashMats.Length)];
            }
            var pos = _muzzle ? _muzzle.position : transform.position;
            var fwd = _muzzle ? _muzzle.forward : transform.forward;
            var fx = Effects.Ensure();
            fx.MuzzleSmoke(pos, fwd);
            fx.EjectCasing(_ejection, shooterVelocity);
            return pos;
        }

        public void PlayReload(float duration, bool empty)
        {
            _reloadStart = Time.time; _reloadDur = duration; _reloadEmpty = empty;
            if (_reloadSounds != null) StopCoroutine(_reloadSounds);
            _reloadSounds = StartCoroutine(ReloadSounds(duration, empty));
        }

        public void CancelReload()
        {
            _reloadStart = -10f;
            if (_reloadSounds != null) StopCoroutine(_reloadSounds);
            _reloadSounds = null;
        }

        private IEnumerator ReloadSounds(float d, bool empty)
        {
            yield return new WaitForSeconds(d * 0.12f);
            GunAudio.Play(GunAudio.Get("mag_out"), transform.position, 0.8f, 25f);
            yield return new WaitForSeconds(d * (empty ? 0.38f : 0.46f));
            GunAudio.Play(GunAudio.Get("mag_in"), transform.position, 0.9f, 25f);
            if (!empty) yield break;
            yield return new WaitForSeconds(d * 0.25f);
            GunAudio.Play(GunAudio.Get("charge"), transform.position, 0.9f, 25f);
        }

        private void LateUpdate()
        {
            float t = Time.time - _fireT;

            // Lumière : montée instantanée puis décroissance exponentielle (~20 ms), petit scintillement.
            if (_light && _light.enabled)
            {
                if (t > 0.08f) { _light.enabled = false; _light.intensity = 0; }
                else _light.intensity = _lightPeak * Mathf.Exp(-t / 0.02f) * Random.Range(0.85f, 1.15f);
            }
            if (_flashRoot && _flashRoot.gameObject.activeSelf && t > 0.035f) _flashRoot.gameObject.SetActive(false);

            // Culasse : recul en 25 ms, retour en 35 ms. Recul de l'arme entière : 2 cm, amorti.
            if (_bolt)
            {
                float back = t < 0.025f ? t / 0.025f : Mathf.Clamp01(1f - (t - 0.025f) / 0.035f);
                float charge = ReloadPhase(out float f) && _reloadEmpty ? Bump(f, 0.72f, 0.86f) : 0f;
                _bolt.localPosition = _boltRest + Vector3.back * (0.075f * Mathf.Max(back, charge));
            }
            transform.localPosition = _rootRest + Vector3.back * (0.022f * Mathf.Exp(-t / 0.04f) * (t < 0.3f ? 1 : 0));

            // Chargeur : sort par le bas, disparaît, un neuf remonte.
            if (_mag)
            {
                if (ReloadPhase(out float f))
                {
                    float outA = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.08f, 0.24f, f));
                    float inA = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.36f, 0.56f, f));
                    float off = f < 0.36f ? outA : 1f - inA;
                    _mag.gameObject.SetActive(!(f > 0.24f && f < 0.36f));
                    _mag.localPosition = _magRest + new Vector3(0, -0.32f, 0.04f) * off;
                    _mag.localRotation = _magRestRot * Quaternion.Euler(-25f * off, 0, 0);
                }
                else if (_mag.localPosition != _magRest || !_mag.gameObject.activeSelf)
                {
                    _mag.gameObject.SetActive(true);
                    _mag.localPosition = _magRest; _mag.localRotation = _magRestRot;
                }
            }
        }

        private bool ReloadPhase(out float f)
        {
            f = _reloadDur > 0 ? (Time.time - _reloadStart) / _reloadDur : 2f;
            return f >= 0f && f <= 1f;
        }

        private static float Bump(float f, float a, float b)
        {
            if (f < a || f > b) return 0f;
            float u = (f - a) / (b - a);
            return u < 0.5f ? u * 2f : (1f - u) * 2f;
        }
    }
}

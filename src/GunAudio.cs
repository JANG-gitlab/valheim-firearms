using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;

namespace ValheimFirearms
{
    internal static class GunAudio
    {
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<string, AudioClip[]> VanillaClips = new Dictionary<string, AudioClip[]>();
        private static AudioMixerGroup _sfxGroup;
        private static readonly List<Voice> Pool = new List<Voice>();
        private static Transform _root;
        private static AnimationCurve _gunRolloff, _softRolloff;
        private const float SpeedOfSound = 343f;

        private class Voice
        {
            public AudioSource Src;
            public AudioLowPassFilter Lp;
            public float FreeAt;
        }

        public static void LoadAll(string pluginDir)
        {
            var dir = Path.Combine(pluginDir, "sounds");
            if (!Directory.Exists(dir)) { Plugin.Log.LogError("Dossier de sons introuvable : " + dir); return; }
            foreach (var f in Directory.GetFiles(dir, "*.wav"))
            {
                try { Clips[Path.GetFileNameWithoutExtension(f)] = LoadWav(f); }
                catch (Exception e) { Plugin.Log.LogError($"Son illisible {f} : {e.Message}"); }
            }
            Plugin.Log.LogInfo($"{Clips.Count} sons chargés");
        }

        /// <summary>Récupère le groupe de mixage SFX et quelques sons d'impact depuis les prefabs vanilla.</summary>
        public static void BindVanilla()
        {
            foreach (var name in new[] { "sfx_arrow_hit", "sfx_metalbars_hit", "sfx_rock_hit", "sfx_wood_hit" })
            {
                var p = Jotunn.Managers.PrefabManager.Instance.GetPrefab(name);
                var zsfx = p ? p.GetComponent<ZSFX>() : null;
                if (zsfx == null) continue;
                VanillaClips[name] = zsfx.m_audioClips;
                if (_sfxGroup == null) _sfxGroup = p.GetComponent<AudioSource>()?.outputAudioMixerGroup;
            }
        }

        /// <summary>WAV PCM 16 bits (ce que produit notre conversion ffmpeg).</summary>
        private static AudioClip LoadWav(string path)
        {
            var d = File.ReadAllBytes(path);
            if (d.Length < 44 || d[0] != 'R' || d[8] != 'W') throw new InvalidDataException("pas un WAV");
            int ch = 1, rate = 44100, bits = 16, pos = 12, dataOff = -1, dataLen = 0;
            while (pos + 8 <= d.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(d, pos, 4);
                int len = BitConverter.ToInt32(d, pos + 4);
                if (id == "fmt ") { ch = BitConverter.ToInt16(d, pos + 10); rate = BitConverter.ToInt32(d, pos + 12); bits = BitConverter.ToInt16(d, pos + 22); }
                else if (id == "data") { dataOff = pos + 8; dataLen = Math.Min(len, d.Length - dataOff); break; }
                pos += 8 + len + (len & 1);
            }
            if (dataOff < 0 || bits != 16) throw new InvalidDataException("format non géré (PCM 16 bits attendu)");
            int n = dataLen / 2; var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = BitConverter.ToInt16(d, dataOff + i * 2) / 32768f;
            var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), n / ch, ch, rate, false);
            clip.SetData(s, 0);
            return clip;
        }

        public static AudioClip Get(string name) => Clips.TryGetValue(name, out var c) ? c : null;

        public static AudioClip Pick(params string[] names) => Get(names[UnityEngine.Random.Range(0, names.Length)]);

        public static AudioClip PickVanilla(string prefab) =>
            VanillaClips.TryGetValue(prefab, out var a) && a.Length > 0 ? a[UnityEngine.Random.Range(0, a.Length)] : null;

        private static Vector3 ListenerPos()
        {
            var cam = GameCamera.instance;
            if (cam && cam.m_listner) return cam.m_listner.transform.position;
            return Camera.main ? Camera.main.transform.position : Vector3.zero;
        }

        private static Voice GetVoice()
        {
            if (!_root)
            {
                _root = new GameObject("VFA_Audio").transform;
                Pool.Clear();
                // Courbes d'atténuation : un tir reste audible loin, contrairement aux sons courts.
                _gunRolloff = new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.03f, 0.8f), new Keyframe(0.1f, 0.5f),
                    new Keyframe(0.33f, 0.22f), new Keyframe(0.66f, 0.08f), new Keyframe(1, 0));
                _softRolloff = new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.1f, 0.45f), new Keyframe(0.3f, 0.12f), new Keyframe(1, 0));
            }
            foreach (var v in Pool)
                if (v.Src && Time.time >= v.FreeAt) return v;
            if (Pool.Count >= 48)
            {
                Pool.Sort((a, b) => a.FreeAt.CompareTo(b.FreeAt));
                return Pool[0];
            }
            var go = new GameObject("voice"); go.transform.SetParent(_root, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 1f; src.dopplerLevel = 0f; src.spread = 30f;
            src.outputAudioMixerGroup = _sfxGroup;
            var voice = new Voice { Src = src, Lp = go.AddComponent<AudioLowPassFilter>() };
            Pool.Add(voice);
            return voice;
        }

        /// <param name="loud">Son d'arme (atténuation lente + délai de propagation + passe-bas avec la distance).</param>
        public static void Play(AudioClip clip, Vector3 pos, float volume, float maxDistance, bool loud = false, float pitchJitter = 0.04f, float extraDelay = 0f)
        {
            if (clip == null || ZNet.instance != null && ZNet.instance.IsDedicated()) return;
            float dist = Vector3.Distance(ListenerPos(), pos);
            if (dist > maxDistance) return;
            var v = GetVoice();
            v.Src.transform.position = pos;
            v.Src.clip = clip;
            v.Src.volume = Mathf.Clamp01(volume * FirearmsConfig.Volume.Value);
            v.Src.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            v.Src.rolloffMode = AudioRolloffMode.Custom;
            v.Src.maxDistance = maxDistance;
            v.Src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, loud ? _gunRolloff : _softRolloff);
            v.Src.outputAudioMixerGroup = _sfxGroup;
            // L'air absorbe les aigus : coupure de 22 kHz près du tireur à ~1,5 kHz à 300 m.
            v.Lp.cutoffFrequency = loud ? Mathf.Lerp(22000f, 1500f, Mathf.Pow(Mathf.Clamp01(dist / 300f), 0.6f)) : 22000f;
            float delay = (loud ? dist / SpeedOfSound : 0f) + extraDelay;
            if (delay > 0.005f) v.Src.PlayDelayed(delay); else v.Src.Play();
            v.FreeAt = Time.time + delay + clip.length / Mathf.Max(0.5f, v.Src.pitch) + 0.05f;
        }

        public static void PlayShot(Vector3 pos)
        {
            float range = FirearmsConfig.AudibleDistance.Value;
            Play(Pick("shot_1", "shot_2"), pos, 1f, range, loud: true, pitchJitter: 0.035f);
            // Queue de réverbération : courte et dense sous un toit, longue et roulante dehors.
            Cover.GetCoverForPoint(pos + Vector3.up, out float cover, out bool underRoof);
            bool indoor = underRoof && cover > 0.6f;
            if (indoor) Play(Get("tail_indoor"), pos, 0.75f, range * 0.6f, loud: true, pitchJitter: 0.02f, extraDelay: 0.02f);
            else Play(Get("tail_outdoor"), pos, 0.55f, range, loud: true, pitchJitter: 0.03f, extraDelay: 0.06f);
        }
    }
}

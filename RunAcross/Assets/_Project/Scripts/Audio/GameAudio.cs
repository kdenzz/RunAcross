using RunAcross.Core;
using UnityEngine;

namespace RunAcross.Audio
{
    public enum Sfx { Shoot, TargetHit, GateOpen, UiClick, UiHover, Jump, Land, Checkpoint, Complete, Respawn }

    /// <summary>
    /// One place for all sound. Three channels: Master scales everything (AudioListener.volume),
    /// Music scales the looping track, FX scales every one-shot. Volumes follow <see cref="GameSettings"/>.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        public AudioClip music;
        [Tooltip("Indexed by the Sfx enum.")]
        public AudioClip[] clips = new AudioClip[10];
        [Range(0f, 1f)] public float musicMix = 0.55f;

        const int Voices = 10;
        AudioSource _music;
        AudioSource[] _voices;
        int _next;

        void Awake()
        {
            Instance = this;
            GameSettings.Load();
            _music = gameObject.AddComponent<AudioSource>();
            _music.clip = music;
            _music.loop = true;
            _music.playOnAwake = false;
            _music.spatialBlend = 0f;

            _voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 4f;
                s.maxDistance = 45f;
                s.dopplerLevel = 0f;
                _voices[i] = s;
            }

            GameSettings.Changed += ApplyVolumes;
            ApplyVolumes();
        }

        void Start()
        {
            if (music) _music.Play();
        }

        void OnDestroy()
        {
            GameSettings.Changed -= ApplyVolumes;
            if (Instance == this) Instance = null;
        }

        void ApplyVolumes()
        {
            AudioListener.volume = GameSettings.MasterVolume;
            _music.volume = GameSettings.MusicVolume * musicMix;
        }

        /// <summary>2D one-shot (UI, player feedback).</summary>
        public static void Play(Sfx sfx, float volume = 1f, float pitchJitter = 0f) => Instance?.PlayInternal(sfx, null, volume, pitchJitter);

        /// <summary>Positional one-shot in the world.</summary>
        public static void PlayAt(Sfx sfx, Vector3 position, float volume = 1f, float pitchJitter = 0f) => Instance?.PlayInternal(sfx, position, volume, pitchJitter);

        void PlayInternal(Sfx sfx, Vector3? position, float volume, float pitchJitter)
        {
            int i = (int)sfx;
            if (i >= clips.Length || clips[i] == null) return;
            var s = _voices[_next];
            _next = (_next + 1) % Voices;
            s.Stop();
            s.clip = clips[i];
            s.spatialBlend = position.HasValue ? 0.85f : 0f;
            if (position.HasValue) s.transform.position = position.Value;
            s.volume = volume * GameSettings.FxVolume;
            s.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            s.Play();
        }
    }
}

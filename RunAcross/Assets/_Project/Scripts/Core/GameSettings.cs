using System;
using UnityEngine;

namespace RunAcross.Core
{
    public enum GraphicsQuality { Low = 0, Medium = 1, High = 2 }

    /// <summary>Player-facing options, persisted in PlayerPrefs. Anything that reacts to them listens to <see cref="Changed"/>.</summary>
    public static class GameSettings
    {
        public static event Action Changed;

        public static GraphicsQuality Quality { get; private set; } = GraphicsQuality.High;
        public static float MasterVolume { get; private set; } = 0.8f;
        public static float MusicVolume { get; private set; } = 0.6f;
        public static float FxVolume { get; private set; } = 0.8f;
        public static float MouseSensitivity { get; private set; } = 1f;
        public static bool InvertY { get; private set; }
        public static bool Fullscreen { get; private set; } = true;

        static bool _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _loaded = false; Changed = null; }

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            Quality = (GraphicsQuality)PlayerPrefs.GetInt("ra.quality", (int)GraphicsQuality.High);
            MasterVolume = PlayerPrefs.GetFloat("ra.vol.master", 0.8f);
            MusicVolume = PlayerPrefs.GetFloat("ra.vol.music", 0.6f);
            FxVolume = PlayerPrefs.GetFloat("ra.vol.fx", 0.8f);
            MouseSensitivity = PlayerPrefs.GetFloat("ra.mouse.sens", 1f);
            InvertY = PlayerPrefs.GetInt("ra.mouse.invertY", 0) == 1;
            Fullscreen = PlayerPrefs.GetInt("ra.fullscreen", Application.isEditor ? 0 : 1) == 1;
        }

        public static void SetQuality(GraphicsQuality q) { Quality = q; Commit(); }
        public static void SetMasterVolume(float v) { MasterVolume = Mathf.Clamp01(v); Commit(); }
        public static void SetMusicVolume(float v) { MusicVolume = Mathf.Clamp01(v); Commit(); }
        public static void SetFxVolume(float v) { FxVolume = Mathf.Clamp01(v); Commit(); }
        public static void SetMouseSensitivity(float v) { MouseSensitivity = Mathf.Clamp(v, 0.2f, 3f); Commit(); }
        public static void SetInvertY(bool v) { InvertY = v; Commit(); }
        public static void SetFullscreen(bool v) { Fullscreen = v; Commit(); }

        static void Commit()
        {
            PlayerPrefs.SetInt("ra.quality", (int)Quality);
            PlayerPrefs.SetFloat("ra.vol.master", MasterVolume);
            PlayerPrefs.SetFloat("ra.vol.music", MusicVolume);
            PlayerPrefs.SetFloat("ra.vol.fx", FxVolume);
            PlayerPrefs.SetFloat("ra.mouse.sens", MouseSensitivity);
            PlayerPrefs.SetInt("ra.mouse.invertY", InvertY ? 1 : 0);
            PlayerPrefs.SetInt("ra.fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}

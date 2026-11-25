using RunAcross.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RunAcross.Core
{
    /// <summary>
    /// Applies the Low / Medium / High preset. The URP side (render scale, MSAA, shadows) lives in the
    /// three quality levels' pipeline assets (RP_Low/Medium/High); this adds what those can't hold:
    /// post-processing cost, planar reflection resolution and the camera's anti-aliasing.
    /// </summary>
    public class GraphicsApplier : MonoBehaviour
    {
        public Volume volume;
        public PlanarReflection reflection;
        public UniversalAdditionalCameraData cameraData;

        [Header("Depth of field")]
        public float menuFocusDistance = 11.5f;
        public float menuAperture = 5.6f;
        public float playFocusDistance = 5f;
        public float playAperture = 9f;

        VolumeProfile _profile; // runtime copy, so play mode never edits the asset on disk
        bool _gameplayFocus;

        void Awake()
        {
            GameSettings.Load();
            if (volume) _profile = volume.profile;
            GameSettings.Changed += Apply;
            Apply();
        }

        void OnDestroy() => GameSettings.Changed -= Apply;

        public void SetGameplayFocus(bool on)
        {
            _gameplayFocus = on;
            ApplyDepthOfField();
        }

        void Apply()
        {
            var q = GameSettings.Quality;
            if (QualitySettings.count >= 3 && QualitySettings.GetQualityLevel() != (int)q)
                QualitySettings.SetQualityLevel((int)q, true);

            if (reflection) reflection.resolutionScale = q switch { GraphicsQuality.Low => 0.3f, GraphicsQuality.Medium => 0.55f, _ => 1f };

            if (cameraData)
                cameraData.antialiasing = q switch
                {
                    GraphicsQuality.Low => AntialiasingMode.None,
                    GraphicsQuality.Medium => AntialiasingMode.FastApproximateAntialiasing,
                    _ => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
                };

            if (_profile)
            {
                if (_profile.TryGet(out Bloom bloom))
                {
                    bloom.highQualityFiltering.Override(q == GraphicsQuality.High);
                    bloom.downscale.Override(q == GraphicsQuality.Low ? BloomDownscaleMode.Quarter : BloomDownscaleMode.Half);
                }
                if (_profile.TryGet(out FilmGrain grain)) grain.active = q != GraphicsQuality.Low;
                ApplyDepthOfField();
            }

            if (!Application.isEditor && Screen.fullScreen != GameSettings.Fullscreen)
                Screen.fullScreenMode = GameSettings.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        void ApplyDepthOfField()
        {
            if (!_profile || !_profile.TryGet(out DepthOfField dof)) return;
            var q = GameSettings.Quality;
            dof.active = q != GraphicsQuality.Low;
            // Bokeh on High; the much cheaper Gaussian blur of the far field on Medium.
            dof.mode.Override(q == GraphicsQuality.High ? DepthOfFieldMode.Bokeh : DepthOfFieldMode.Gaussian);
            float focus = _gameplayFocus ? playFocusDistance : menuFocusDistance;
            dof.focusDistance.Override(focus);
            dof.aperture.Override(_gameplayFocus ? playAperture : menuAperture);
            dof.gaussianStart.Override(focus + 4f);
            dof.gaussianEnd.Override(focus + 30f);
            dof.gaussianMaxRadius.Override(1f);
        }
    }
}

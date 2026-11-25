using System;
using RunAcross.Audio;
using UnityEngine;

namespace RunAcross.Gameplay
{
    /// <summary>A neon disc that flips from orange to cyan when shot. <see cref="PathOpener"/>s listen for it.</summary>
    public class ShootTarget : MonoBehaviour
    {
        public Renderer[] glowRenderers;
        public Material idleMaterial;
        public Material hitMaterial;
        public Light glowLight;
        public Color idleLightColor = new Color(1f, 0.25f, 0.02f);
        public Color hitLightColor = new Color(0.05f, 0.7f, 1f);
        public float bobHeight = 0.12f;
        public float spinSpeed = 40f;

        public event Action<ShootTarget> Activated;
        public bool IsHit { get; private set; }

        Vector3 _home;
        float _phase, _punch;

        void Awake()
        {
            _home = transform.localPosition;
            _phase = UnityEngine.Random.value * 10f;
            SetLook(false);
        }

        void Update()
        {
            float t = Time.time + _phase;
            transform.localPosition = _home + Vector3.up * (Mathf.Sin(t * 1.6f) * bobHeight);
            transform.Rotate(0f, 0f, (IsHit ? spinSpeed * 4f : spinSpeed) * Time.deltaTime, Space.Self);
            if (_punch > 0f)
            {
                _punch = Mathf.Max(0f, _punch - Time.deltaTime * 4f);
                transform.localScale = Vector3.one * (1f + 0.35f * Mathf.Sin(_punch * Mathf.PI));
            }
        }

        /// <summary>Returns true the first time the target is hit.</summary>
        public bool Hit()
        {
            if (IsHit) return false;
            IsHit = true;
            _punch = 1f;
            SetLook(true);
            GameAudio.PlayAt(Sfx.TargetHit, transform.position, 1f);
            Activated?.Invoke(this);
            return true;
        }

        public void ResetTarget()
        {
            IsHit = false;
            SetLook(false);
        }

        void SetLook(bool hit)
        {
            var mat = hit ? hitMaterial : idleMaterial;
            if (mat != null)
                foreach (var r in glowRenderers) if (r) r.sharedMaterial = mat;
            if (glowLight) glowLight.color = hit ? hitLightColor : idleLightColor;
        }
    }
}

using RunAcross.Core;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunAcross.Player
{
    /// <summary>
    /// Drives the third-person orbit camera from the Player/Look action. Mouse delta is in pixels per
    /// frame, so it is applied without deltaTime; sticks are rates, so they are scaled by deltaTime.
    /// Sensitivity and invert-Y come from <see cref="GameSettings"/>.
    /// </summary>
    [RequireComponent(typeof(CinemachineOrbitalFollow))]
    public class PlayerLook : MonoBehaviour
    {
        public InputActionAsset actions;
        [Tooltip("Degrees per pixel of mouse movement at sensitivity 1.")]
        public float mouseDegreesPerPixel = 0.12f;
        [Tooltip("Degrees per second at full stick deflection (yaw, pitch).")]
        public Vector2 stickDegreesPerSecond = new(180f, 110f);
        public CinemachineBrain brain;

        CinemachineOrbitalFollow _orbit;
        InputAction _look;

        void Awake()
        {
            _orbit = GetComponent<CinemachineOrbitalFollow>();
            _look = actions.FindActionMap("Player", true).FindAction("Look", true);
        }

        void Update()
        {
            if (!_look.enabled || Time.timeScale == 0f) return;
            if (brain && brain.IsBlending) return; // don't fight the menu -> play blend

            Vector2 v = _look.ReadValue<Vector2>();
            if (v == Vector2.zero) return;

            bool pointer = _look.activeControl?.device is Pointer;
            Vector2 deg = pointer ? v * mouseDegreesPerPixel : Vector2.Scale(v, stickDegreesPerSecond) * Time.unscaledDeltaTime;
            deg *= GameSettings.MouseSensitivity;
            if (GameSettings.InvertY) deg.y = -deg.y;

            var h = _orbit.HorizontalAxis;
            h.Value = h.ClampValue(h.Value + deg.x);
            _orbit.HorizontalAxis = h;

            var vert = _orbit.VerticalAxis;
            vert.Value = vert.ClampValue(vert.Value - deg.y); // moving up looks up: camera drops lower
            _orbit.VerticalAxis = vert;
        }
    }
}

using System;
using RunAcross.Audio;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunAcross.Player
{
    /// <summary>Camera-relative walk / sprint / jump on a CharacterController, driven by the "Player" action map.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        public InputActionAsset actions;
        public Transform cameraTransform;

        [Header("Movement")]
        public float walkSpeed = 3.5f;
        public float sprintSpeed = 6.5f;
        public float turnSpeed = 12f;

        [Header("Jump")]
        public float jumpHeight = 1.2f;
        public float gravity = -20f;
        [Tooltip("Grace period after leaving a ledge during which a jump still counts.")]
        public float coyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing fires on landing.")]
        public float jumpBuffer = 0.12f;
        public float respawnBelowY = -8f;

        public event Action Respawned;
        /// <summary>Raised the frame a jump launches.</summary>
        public event Action Jumped;

        /// <summary>Horizontal speed the courier is being driven at (m/s), excluding platform carry.</summary>
        public float PlanarSpeed { get; private set; }
        /// <summary>On the ground, with a short grace so CharacterController's flicker on steps doesn't register as a fall.</summary>
        public bool IsGrounded => Time.time - _lastGroundedTime <= 0.1f;
        public float VerticalVelocity => _verticalVelocity;

        public bool InputEnabled { get; private set; }
        public Vector3 CheckpointPosition => _checkpointPosition;

        CharacterController _controller;
        InputActionMap _map;
        InputAction _move, _jump, _sprint;
        Vector3 _checkpointPosition;
        Quaternion _checkpointRotation;
        float _verticalVelocity, _lastGroundedTime = -1f, _lastJumpPressTime = -1f;
        bool _wasGrounded = true;
        Gameplay.MovingPlatform _ground;
        Quaternion? _faceOverride;
        float _faceOverrideUntil;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _map = actions.FindActionMap("Player", true);
            _move = _map.FindAction("Move", true);
            _jump = _map.FindAction("Jump", true);
            _sprint = _map.FindAction("Sprint");
            SetCheckpoint(transform.position, transform.rotation);
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void OnDisable() => _map?.Disable();

        public void SetInputEnabled(bool on)
        {
            InputEnabled = on;
            if (on) _map.Enable(); else _map.Disable();
        }

        public void SetCheckpoint(Vector3 position, Quaternion rotation)
        {
            _checkpointPosition = position;
            _checkpointRotation = rotation;
        }

        /// <summary>Turn to face a world direction for a moment (e.g. while shooting), overriding movement facing.</summary>
        public void FaceTowards(Vector3 worldDirection, float holdSeconds = 0.35f)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 0.0001f) return;
            _faceOverride = Quaternion.LookRotation(worldDirection);
            _faceOverrideUntil = Time.time + holdSeconds;
        }

        void Update()
        {
            Vector2 input = InputEnabled ? _move.ReadValue<Vector2>() : Vector2.zero;
            Vector3 forward = cameraTransform ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 direction = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);

            if (_faceOverride.HasValue && Time.time < _faceOverrideUntil)
                transform.rotation = Quaternion.Slerp(transform.rotation, _faceOverride.Value, 25f * Time.deltaTime);
            else if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);

            bool grounded = _controller.isGrounded;
            if (grounded)
            {
                _lastGroundedTime = Time.time;
                if (!_wasGrounded && _verticalVelocity < -6f) GameAudio.PlayAt(Sfx.Land, transform.position, 0.6f, 0.1f);
                if (_verticalVelocity < 0f) _verticalVelocity = -2f;
            }
            _wasGrounded = grounded;

            if (InputEnabled && _jump.WasPressedThisFrame()) _lastJumpPressTime = Time.time;
            bool canJump = Time.time - _lastGroundedTime <= coyoteTime && _verticalVelocity <= 0f;
            if (canJump && Time.time - _lastJumpPressTime <= jumpBuffer)
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                _lastJumpPressTime = -1f;
                _lastGroundedTime = -1f;
                GameAudio.PlayAt(Sfx.Jump, transform.position, 0.7f, 0.08f);
                Jumped?.Invoke();
            }
            _verticalVelocity += gravity * Time.deltaTime;

            float speed = InputEnabled && _sprint != null && _sprint.IsPressed() ? sprintSpeed : walkSpeed;
            // ride moving platforms: they move first (execution order), we follow by their delta
            Vector3 carry = _ground ? _ground.Delta : Vector3.zero;
            _ground = null; // re-set by OnControllerColliderHit while we're still standing on it
            PlanarSpeed = direction.magnitude * speed;
            _controller.Move((direction * speed + Vector3.up * _verticalVelocity) * Time.deltaTime + carry);

            if (transform.position.y < respawnBelowY) Respawn();
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > 0.6f) _ground = hit.collider.GetComponentInParent<Gameplay.MovingPlatform>();
        }

        public void Respawn() =>Teleport(_checkpointPosition, _checkpointRotation, true);

        public void Teleport(Vector3 position, Quaternion rotation, bool announce = false)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;
            _verticalVelocity = 0f;
            _faceOverride = null;
            _ground = null;
            PlanarSpeed = 0f;
            if (announce)
            {
                GameAudio.Play(Sfx.Respawn, 0.7f);
                Respawned?.Invoke();
            }
        }
    }
}

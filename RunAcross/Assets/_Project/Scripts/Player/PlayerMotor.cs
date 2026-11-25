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
        public float respawnBelowY = -15f;

        CharacterController _controller;
        InputAction _move, _jump, _sprint;
        Vector3 _spawnPosition;
        float _verticalVelocity;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            var map = actions.FindActionMap("Player", true);
            _move = map.FindAction("Move", true);
            _jump = map.FindAction("Jump", true);
            _sprint = map.FindAction("Sprint");
            _spawnPosition = transform.position;
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void OnEnable() => actions.FindActionMap("Player", true).Enable();
        void OnDisable() => actions.FindActionMap("Player", true).Disable();

        void Update()
        {
            Vector2 input = _move.ReadValue<Vector2>();
            Vector3 forward = cameraTransform ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 direction = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);

            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);

            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            if (_controller.isGrounded && _jump.WasPressedThisFrame()) _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _verticalVelocity += gravity * Time.deltaTime;

            float speed = _sprint != null && _sprint.IsPressed() ? sprintSpeed : walkSpeed;
            _controller.Move((direction * speed + Vector3.up * _verticalVelocity) * Time.deltaTime);

            if (transform.position.y < respawnBelowY) Respawn();
        }

        void Respawn()
        {
            _controller.enabled = false;
            transform.position = _spawnPosition;
            _controller.enabled = true;
            _verticalVelocity = 0f;
        }
    }
}

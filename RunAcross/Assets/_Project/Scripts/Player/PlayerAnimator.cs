using RunAcross.Audio;
using UnityEngine;

namespace RunAcross.Player
{
    /// <summary>
    /// Feeds the courier's Humanoid Animator from <see cref="PlayerMotor"/> (locomotion blend, jump, fall,
    /// land) and raises the right arm along the aim for a moment after each shot (Animator IK).
    /// Lives on the model object next to the Animator, because OnAnimatorIK and the Starter Assets clips'
    /// animation events (OnFootstep / OnLand) are delivered there.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int JumpId = Animator.StringToHash("Jump");
        static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");
        static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");

        [Tooltip("Damping on the Speed parameter so walk/run blends ease instead of snapping.")]
        public float speedDamp = 0.08f;
        [Tooltip("How long the arm stays raised after a shot.")]
        public float aimHoldSeconds = 0.45f;
        public float aimBlendSpeed = 14f;
        public float footstepVolume = 0.12f;
        [Tooltip("Pace of the run clip in m/s. Faster than this (sprinting) plays the run faster so feet don't slide.")]
        public float runClipSpeed = 5.335f;

        Animator _animator;
        PlayerMotor _motor;
        PlayerShooter _shooter;
        Transform _shoulder;
        Vector3 _aimDir = Vector3.forward;
        float _aimUntil = -1f, _aimWeight;

        void Awake()
        {
            _animator = GetComponent<Animator>();
            _animator.applyRootMotion = false; // the CharacterController moves the courier
            _motor = GetComponentInParent<PlayerMotor>();
            _shooter = GetComponentInParent<PlayerShooter>();
            _shoulder = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }

        void OnEnable()
        {
            if (_motor) _motor.Jumped += OnJumped;
            if (_shooter) _shooter.Fired += OnFired;
        }

        void OnDisable()
        {
            if (_motor) _motor.Jumped -= OnJumped;
            if (_shooter) _shooter.Fired -= OnFired;
        }

        void OnJumped() => _animator.SetTrigger(JumpId);

        void OnFired(Vector3 dir)
        {
            _aimDir = dir;
            _aimUntil = Time.time + aimHoldSeconds;
            _aimWeight = 1f; // snap up so the arm is already raised the frame the tracer draws
        }

        void Update()
        {
            if (!_motor) return;
            _animator.SetFloat(SpeedId, _motor.PlanarSpeed, speedDamp, Time.deltaTime);
            _animator.SetFloat(MotionSpeedId, Mathf.Max(1f, _motor.PlanarSpeed / runClipSpeed));
            _animator.SetBool(GroundedId, _motor.IsGrounded);
            _animator.SetFloat(VerticalSpeedId, _motor.VerticalVelocity);
            float target = Time.time < _aimUntil ? 1f : 0f;
            _aimWeight = Mathf.MoveTowards(_aimWeight, target, aimBlendSpeed * Time.deltaTime * 0.25f);
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (_aimWeight <= 0.001f || !_shoulder) return;
            float w = Mathf.SmoothStep(0f, 1f, _aimWeight);
            Vector3 hand = _shoulder.position + _aimDir * (_shooter ? _shooter.armReach : 0.42f);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, w);
            _animator.SetIKPosition(AvatarIKGoal.RightHand, hand);
            _animator.SetLookAtWeight(w, 0.15f, 0.7f, 0f, 0.6f);
            _animator.SetLookAtPosition(_shoulder.position + _aimDir * 10f);
        }

        // ---- animation events baked into the Starter Assets clips (they error if nobody receives them)

        void OnFootstep(AnimationEvent e)
        {
            // locomotion is a blend tree, so only the dominant clip's events count (as in Starter Assets)
            if (e.animatorClipInfo.weight > 0.5f && _motor && _motor.IsGrounded && _motor.PlanarSpeed > 0.1f)
                GameAudio.PlayAt(Sfx.Land, transform.position, footstepVolume, 0.25f);
        }

        void OnLand(AnimationEvent e) { } // PlayerMotor already plays the landing sound
    }
}

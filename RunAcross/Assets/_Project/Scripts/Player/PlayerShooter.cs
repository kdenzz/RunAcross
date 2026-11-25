using System;
using RunAcross.Audio;
using RunAcross.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunAcross.Player
{
    /// <summary>
    /// Hitscan neon shot along the centre of the screen (where the crosshair is), drawn as a tracer
    /// from the courier's hand. Hitting a <see cref="ShootTarget"/> activates it.
    /// The player, gates and boundary walls sit on the Ignore Raycast layer, so shots pass through them.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerShooter : MonoBehaviour
    {
        public InputActionAsset actions;
        public Transform aimCamera;
        public Transform muzzle;
        public Material tracerMaterial;
        public Material sparkMaterial;
        public Color flashColor = new Color(0.1f, 0.7f, 1f);

        public float cooldown = 0.22f;
        public float range = 90f;
        public float tracerLife = 0.09f;

        public event Action<ShootTarget> HitTarget;

        PlayerMotor _motor;
        InputAction _fire;
        float _nextShot;
        LineRenderer[] _tracers;
        float[] _tracerEnd;
        int _nextTracer;
        ParticleSystem _sparks;
        Light _flash;
        float _flashEnd;

        void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _fire = actions.FindActionMap("Player", true).FindAction("Attack", true);
            if (aimCamera == null && Camera.main != null) aimCamera = Camera.main.transform;
            BuildFx();
        }

        void OnEnable() => _nextShot = Time.time + 0.25f; // the click that pressed Play/Resume must not fire

        void Update()
        {
            float now = Time.time;
            for (int i = 0; i < _tracers.Length; i++)
            {
                if (!_tracers[i].enabled) continue;
                float k = (_tracerEnd[i] - now) / tracerLife;
                if (k <= 0f) { _tracers[i].enabled = false; continue; }
                _tracers[i].widthMultiplier = 0.05f * k;
            }
            if (_flash.enabled)
            {
                float k = (_flashEnd - now) / 0.12f;
                _flash.intensity = 6f * Mathf.Max(0f, k);
                if (k <= 0f) _flash.enabled = false;
            }

            if (!_motor.InputEnabled || !_fire.WasPressedThisFrame() || now < _nextShot) return;
            _nextShot = now + cooldown;
            Fire();
        }

        void Fire()
        {
            Vector3 dir = aimCamera.forward;
            // Start the ray level with the player so nothing between the camera and the courier gets hit.
            float toPlayer = Vector3.Dot(transform.position - aimCamera.position, dir);
            Vector3 origin = aimCamera.position + dir * Mathf.Max(0f, toPlayer);
            Vector3 end = origin + dir * range;
            ShootTarget target = null;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                end = hit.point;
                target = hit.collider.GetComponentInParent<ShootTarget>();
                if (target == null && hit.collider.isTrigger)
                {
                    // ignore gameplay trigger volumes (checkpoints, goal): re-cast solid-only
                    if (Physics.Raycast(origin, dir, out hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        end = hit.point;
                    else end = origin + dir * range;
                }
                Spark(end, hit.normal);
            }

            _motor.FaceTowards(dir);
            Vector3 from = muzzle ? muzzle.position : transform.position + Vector3.up;
            var lr = _tracers[_nextTracer];
            _tracerEnd[_nextTracer] = Time.time + tracerLife;
            _nextTracer = (_nextTracer + 1) % _tracers.Length;
            lr.SetPosition(0, from);
            lr.SetPosition(1, end);
            lr.widthMultiplier = 0.05f;
            lr.enabled = true;

            GameAudio.PlayAt(Sfx.Shoot, from, 0.55f, 0.06f);

            if (target != null && target.Hit())
                HitTarget?.Invoke(target);
        }

        void Spark(Vector3 point, Vector3 normal)
        {
            _sparks.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
            _sparks.Emit(14);
            _flash.transform.position = point + normal * 0.2f;
            _flash.enabled = true;
            _flash.intensity = 6f;
            _flashEnd = Time.time + 0.12f;
        }

        void BuildFx()
        {
            var fx = new GameObject("ShotFx").transform;
            _tracers = new LineRenderer[4];
            _tracerEnd = new float[4];
            for (int i = 0; i < _tracers.Length; i++)
            {
                var lr = new GameObject("Tracer" + i).AddComponent<LineRenderer>();
                lr.transform.SetParent(fx, false);
                lr.sharedMaterial = tracerMaterial;
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1f));
                lr.enabled = false;
                _tracers[i] = lr;
            }

            var sparksGo = new GameObject("Sparks");
            sparksGo.transform.SetParent(fx, false);
            _sparks = sparksGo.AddComponent<ParticleSystem>();
            _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _sparks.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 128;
            var emission = _sparks.emission;
            emission.enabled = false;
            var shape = _sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 50f;
            shape.radius = 0.02f;
            var size = _sparks.sizeOverLifetime; // the material is opaque emissive, so sparks shrink rather than fade
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var psr = sparksGo.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = sparkMaterial;
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.velocityScale = 0.04f;
            psr.lengthScale = 1f;
            _sparks.Play();

            _flash = new GameObject("HitFlash").AddComponent<Light>();
            _flash.transform.SetParent(fx, false);
            _flash.type = LightType.Point;
            _flash.color = flashColor;
            _flash.range = 3f;
            _flash.enabled = false;
        }
    }
}

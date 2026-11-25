using System;
using System.Collections;
using RunAcross.Audio;
using UnityEngine;

namespace RunAcross.Gameplay
{
    /// <summary>
    /// Opens a path once every linked <see cref="ShootTarget"/> is hit: slides this transform by
    /// <see cref="openOffset"/> (a laser gate sinks into the floor, a bridge rises out of the fog).
    /// </summary>
    public class PathOpener : MonoBehaviour
    {
        public ShootTarget[] targets;
        public Vector3 openOffset = new Vector3(0f, -3f, 0f);
        public float duration = 1.4f;
        public float delay = 0.25f;
        [Tooltip("Colliders switched off the moment the path opens (a gate's blocking wall).")]
        public Collider[] disableOnOpen;
        [Tooltip("Renderers hidden once the move has finished.")]
        public bool hideWhenOpen;

        [Tooltip("0 = no limit. Otherwise every target must be hit within this many seconds of the first, or they all reset.")]
        public float timeLimit;

        [TextArea] public string objective = "Shoot the targets to open the gate";
        public string openedMessage = "Gate open";

        public event Action<PathOpener> Opened;
        public event Action<PathOpener> Progressed;
        public event Action<PathOpener> TimedOut;

        /// <summary>Seconds left on a running timed challenge, or -1 when no timer is running.</summary>
        public float TimeRemaining => _deadline > 0f && !IsOpen ? Mathf.Max(0f, _deadline - Time.time) : -1f;

        public bool IsOpen { get; private set; }
        public int HitCount { get; private set; }
        public int TargetCount => targets.Length;

        Vector3 _closedPosition;
        float _deadline = -1f;

        void Awake()
        {
            _closedPosition = transform.position;
            foreach (var t in targets) if (t) t.Activated += OnTargetHit;
        }

        void OnTargetHit(ShootTarget t)
        {
            if (IsOpen) return;
            if (HitCount == 0 && timeLimit > 0f) _deadline = Time.time + timeLimit;
            HitCount++;
            Progressed?.Invoke(this);
            if (HitCount >= targets.Length) Open();
        }

        void Update()
        {
            if (IsOpen || _deadline <= 0f || Time.time < _deadline) return;
            _deadline = -1f;
            HitCount = 0;
            foreach (var t in targets) if (t) t.ResetTarget();
            GameAudio.Play(Sfx.Respawn, 0.6f);
            TimedOut?.Invoke(this);
            Progressed?.Invoke(this);
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            _deadline = -1f;
            foreach (var c in disableOnOpen) if (c) c.enabled = false;
            StartCoroutine(Animate());
            Opened?.Invoke(this);
        }

        IEnumerator Animate()
        {
            yield return new WaitForSeconds(delay);
            GameAudio.PlayAt(Sfx.GateOpen, transform.position, 1f);
            Vector3 from = _closedPosition, to = _closedPosition + openOffset;
            for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
            {
                float k = t * t * (3f - 2f * t);
                transform.position = Vector3.LerpUnclamped(from, to, k);
                yield return null;
            }
            transform.position = to;
            if (hideWhenOpen)
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        }
    }
}

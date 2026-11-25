using System;
using RunAcross.Audio;
using RunAcross.Player;
using UnityEngine;

namespace RunAcross.Gameplay
{
    /// <summary>Trigger volume on a platform. Touching it makes it the respawn point and lights its pad.</summary>
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        public Transform spawnPoint;
        public Renderer pad;
        public Material activeMaterial;

        public static event Action<Checkpoint> Reached;
        public bool IsActive { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reached = null;

        void OnTriggerEnter(Collider other)
        {
            if (IsActive || !other.TryGetComponent(out PlayerMotor motor)) return;
            IsActive = true;
            var p = spawnPoint ? spawnPoint : transform;
            motor.SetCheckpoint(p.position, p.rotation);
            if (pad && activeMaterial) pad.sharedMaterial = activeMaterial;
            GameAudio.Play(Sfx.Checkpoint, 0.8f);
            Reached?.Invoke(this);
        }
    }
}

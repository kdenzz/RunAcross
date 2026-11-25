using System;
using RunAcross.Player;
using UnityEngine;

namespace RunAcross.Gameplay
{
    /// <summary>The beacon at the end of the course. Entering it finishes the run.</summary>
    [RequireComponent(typeof(Collider))]
    public class GoalZone : MonoBehaviour
    {
        public Transform spinner;
        public float spinSpeed = 30f;

        public static event Action Reached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reached = null;

        void Update()
        {
            if (spinner) spinner.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<PlayerMotor>()) Reached?.Invoke();
        }
    }
}

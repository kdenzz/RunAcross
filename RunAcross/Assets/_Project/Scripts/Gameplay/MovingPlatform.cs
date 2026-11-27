using RunAcross.Core;
using UnityEngine;

namespace RunAcross.Gameplay
{
    /// <summary>
    /// Eases back and forth along <see cref="travel"/> (slow at both ends, so they're easy to board).
    /// Runs before the player, which reads <see cref="Delta"/> to ride along.
    /// Holds still on the menu and through the start countdown, so every run starts from the same layout.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class MovingPlatform : MonoBehaviour
    {
        public Vector3 travel = new Vector3(0f, 0f, 6f);
        [Tooltip("Seconds for a full there-and-back trip.")]
        public float period = 7f;
        [Range(0f, 1f)] public float phaseOffset;

        public Vector3 Delta { get; private set; }

        Vector3 _start;
        float _time;

        void Awake()
        {
            _start = transform.localPosition;
            _time = phaseOffset * period;
            transform.localPosition = Evaluate(_time);
        }

        void Update()
        {
            var flow = GameFlow.Instance;
            if (flow && (flow.State == GameState.Menu || flow.State == GameState.Starting))
            {
                Delta = Vector3.zero;
                return;
            }

            Vector3 before = transform.position;
            _time += Time.deltaTime;
            transform.localPosition = Evaluate(_time);
            Delta = transform.position - before;
        }

        Vector3 Evaluate(float t)
        {
            float k = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t / period);
            return _start + travel * k;
        }
    }
}

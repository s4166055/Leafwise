using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// Flint-and-stone fire starting: hold a flint stone in each hand and
    /// strike them together HARD near the fire pit. A solid strike throws
    /// sparks; two or three good strikes in quick succession light the tinder.
    /// (The hand drill remains the alternative method.)
    /// </summary>
    public class FlintStone : MonoBehaviour
    {
        [Header("Tuning")]
        public float minStrikeSpeed = 1.3f;   // m/s relative impact speed for any sparks
        public float goodStrikeSpeed = 2.4f;  // a hard, fire-worthy strike
        public float sparkRangeToPit = 1.2f;  // strikes must happen near the pit

        XRGrabInteractable _grab;
        Rigidbody _rb;
        float _lastSparkTime;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            _rb = GetComponent<Rigidbody>();
        }

        void OnCollisionEnter(Collision collision)
        {
            var other = collision.collider.GetComponentInParent<FlintStone>();
            if (other == null) return;

            // let only one of the two stones process the strike
            if (GetInstanceID() > other.GetInstanceID()) return;
            if (Time.time - _lastSparkTime < 0.25f) return;

            // at least one stone must be held (no sparks from stones rolling on the ground)
            bool held = (_grab != null && _grab.isSelected) || (other._grab != null && other._grab.isSelected);
            if (!held) return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < minStrikeSpeed) return;
            _lastSparkTime = Time.time;

            Vector3 point = collision.GetContact(0).point;
            bool good = speed >= goodStrikeSpeed;
            FireVfx.SparkBurst(point, good ? 26 : 8);
            ProceduralAudio.PlayAt(point, ProceduralAudio.FlintClick(), good ? 1f : 0.5f);
            Haptics.Pulse(good ? 0.9f : 0.4f, 0.1f);

            var pit = FirePit.Instance;
            if (pit == null) return;

            if (Vector3.Distance(point, pit.transform.position) > sparkRangeToPit)
            {
                if (good) ForageEvents.RaiseHint("sparks-too-far-from-pit");
                return;
            }

            if (good)
            {
                // ~3 hard strikes reach ignition through the normal heat path,
                // so tinder/wood/wet-wood rules all still apply
                pit.AddHeat(40f);
            }
            else
            {
                ForageEvents.RaiseHint("strike-harder");
            }
        }
    }
}

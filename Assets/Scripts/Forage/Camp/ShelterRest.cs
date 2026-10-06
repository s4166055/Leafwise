using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Sleep in the finished shelter. At night, crouch down inside it and stay
    /// there for a few seconds: the screen fades, the night passes and you wake
    /// at dawn warm and rested (see <see cref="DayNightCycle.SkipToMorning"/>).
    ///
    /// "Crouching" is judged from the headset: the head has to drop below
    /// <see cref="crouchHeadHeight"/> above the ground, i.e. you physically crouch
    /// (or kneel / sit on the floor). At a desk, or with the XR simulator, hold
    /// <b>C</b> or <b>Left Ctrl</b> instead. The decision lives in
    /// the pure <see cref="Evaluate"/> function so it can be tested without a
    /// headset.
    /// </summary>
    public class ShelterRest : MonoBehaviour
    {
        public enum Reason { Ok, NotNight, ShelterIncomplete, TooFar, StandingUp, Busy }

        public float holdSeconds = 3.5f;
        public float maxDistance = 1.3f;
        public float crouchHeadHeight = 1.15f;

        /// <summary>Shelter-local point you rest at: under the thatch, in front of the ridge.</summary>
        public static readonly Vector3 RestLocal = new Vector3(0f, 0f, -0.4f);

        Shelter _shelter;
        DayNightCycle _dayNight;
        float _timer;
        bool _hinted;

        public float Progress01 => Mathf.Clamp01(_timer / Mathf.Max(0.01f, holdSeconds));
        public Reason LastReason { get; private set; } = Reason.NotNight;
        public Vector3 RestPoint => transform.TransformPoint(RestLocal);

        /// <summary>True while the desktop crouch key (C or Left Ctrl) is held.</summary>
        public static bool CrouchKeyHeld
        {
            get
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                return kb != null && (kb.cKey.isPressed || kb.leftCtrlKey.isPressed);
            }
        }

        public static Reason Evaluate(Vector3 head, Vector3 restPoint, float groundY, bool shelterComplete,
            bool isNight, bool busy, float maxDistance, float crouchHeadHeight, bool crouchKeyHeld = false)
        {
            if (busy) return Reason.Busy;
            if (!isNight) return Reason.NotNight;
            if (!shelterComplete) return Reason.ShelterIncomplete;
            var flat = new Vector2(head.x - restPoint.x, head.z - restPoint.z);
            if (flat.magnitude > maxDistance) return Reason.TooFar;
            if (!crouchKeyHeld && head.y - groundY > crouchHeadHeight) return Reason.StandingUp;
            return Reason.Ok;
        }

        public Reason Check(Vector3 head)
        {
            if (_shelter == null) _shelter = GetComponent<Shelter>();
            if (_dayNight == null) _dayNight = FindFirstObjectByType<DayNightCycle>();
            var forest = ForestGenerator.Instance;
            float ground = forest != null ? forest.HeightAt(head.x, head.z) : transform.position.y;
            return Evaluate(head, RestPoint, ground,
                _shelter != null && _shelter.IsComplete,
                _dayNight != null && _dayNight.isNight,
                _dayNight != null && _dayNight.IsSkipping,
                maxDistance, crouchHeadHeight, CrouchKeyHeld);
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.playerHead == null) return;

            LastReason = Check(gm.playerHead.position);
            if (LastReason == Reason.Ok)
            {
                if (!_hinted)
                {
                    _hinted = true;
                    ForageEvents.RaiseHint("sleep-ready");
                }
                _timer += Time.deltaTime;
                if (_timer >= holdSeconds)
                {
                    _timer = 0f;
                    _dayNight.SkipToMorning(true);
                }
            }
            else
            {
                _timer = Mathf.Max(0f, _timer - Time.deltaTime * 2f);
                if (LastReason == Reason.NotNight) _hinted = false;   // ready to hint again next night
            }
        }
    }
}

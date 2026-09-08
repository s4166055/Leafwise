using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// Hand-drill fire starting: hold the drill stick, press its tip into the
    /// fireboard zone next to the fire pit, and move the hand vigorously
    /// (spin/scrub). Sustained motion builds heat in the FirePit; stopping
    /// lets it decay. Slow drilling triggers a Scout hint.
    /// </summary>
    public class FireDrill : MonoBehaviour
    {
        [Header("Wiring (set by CampsiteBuilder)")]
        public FirePit firePit;
        public Transform drillTip;          // tip of the drill stick
        public Transform boardCenter;       // where the tip must be pressed
        public float boardRadius = 0.14f;

        [Header("Tuning")]
        public float minSpeed = 0.35f;       // m/s of tip motion to count as drilling
        public float heatPerSecondAtFullSpeed = 16f;
        public float fullSpeed = 1.6f;

        XRGrabInteractable _grab;
        Vector3 _lastTipPos;
        float _smoothedSpeed;
        float _slowHintTimer;
        float _hapticTimer;
        ParticleSystem _drillSmoke;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
        }

        void Start()
        {
            if (drillTip == null)
            {
                var tip = transform.Find("DrillTip");
                drillTip = tip != null ? tip : transform;
            }
            _lastTipPos = drillTip.position;
        }

        void Update()
        {
            bool held = _grab != null && _grab.isSelected;
            bool onBoard = boardCenter != null &&
                           Vector3.Distance(drillTip.position, boardCenter.position) < boardRadius;

            float tipSpeed = (drillTip.position - _lastTipPos).magnitude / Mathf.Max(Time.deltaTime, 1e-5f);
            _lastTipPos = drillTip.position;
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, tipSpeed, 0.25f);

            if (held && onBoard && firePit != null && !firePit.IsLit)
            {
                if (_smoothedSpeed >= minSpeed)
                {
                    float t = Mathf.Clamp01(_smoothedSpeed / fullSpeed);
                    bool applied = firePit.AddHeat(heatPerSecondAtFullSpeed * t * Time.deltaTime);
                    _slowHintTimer = 0f;

                    if (applied)
                    {
                        UpdateDrillSmoke(t);
                        // friction rumble scaled by drill speed
                        _hapticTimer -= Time.deltaTime;
                        if (_hapticTimer <= 0f)
                        {
                            Haptics.Pulse(0.15f + t * 0.35f, 0.08f);
                            _hapticTimer = 0.09f;
                        }
                    }
                }
                else
                {
                    // pressed on the board but barely moving
                    _slowHintTimer += Time.deltaTime;
                    if (_slowHintTimer > 4f)
                    {
                        _slowHintTimer = 0f;
                        ForageEvents.RaiseHint("drill-too-slow");
                    }
                    UpdateDrillSmoke(0f);
                }
            }
            else
            {
                UpdateDrillSmoke(0f);
            }
        }

        void UpdateDrillSmoke(float intensity)
        {
            if (firePit == null) return;
            float heatT = firePit.heat / firePit.heatToFlame;
            if (_drillSmoke == null && intensity > 0f && heatT > 0.2f)
                _drillSmoke = FireVfx.Smoke(drillTip, 0f);

            if (_drillSmoke != null)
            {
                var emission = _drillSmoke.emission;
                emission.rateOverTime = intensity > 0f && heatT > 0.2f ? Mathf.Lerp(2f, 25f, heatT) : 0f;
            }
        }
    }
}

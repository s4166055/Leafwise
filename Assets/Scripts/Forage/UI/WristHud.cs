using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Hands;
using TMPro;

namespace Forage
{
    /// <summary>
    /// Small, subtle wrist HUD: four thin vitals bars, the current objective and
    /// the time of day on a translucent ~10cm panel. Built in code at Awake.
    ///
    /// It is also the watch: at night, hold your free (right) hand over it for
    /// a moment and the night is skipped (N does the same at a desk). Nothing
    /// on the panel is a button, because the scene has no UI event system.
    /// </summary>
    public class WristHud : MonoBehaviour
    {
        public PlayerVitals vitals;

        RectTransform _healthFill, _hydrationFill, _energyFill, _warmthFill;
        TextMeshProUGUI _objectiveText;
        TextMeshProUGUI _clockText;
        RectTransform _skipTrack, _skipFill;
        RectTransform _canvasRect;
        DayNightCycle _dayNight;
        float _holdTimer;
        bool _handNear;
        Unity.XR.CoreUtils.XROrigin _origin;
        static readonly List<XRHandSubsystem> _handSubsystems = new List<XRHandSubsystem>();

        /// <summary>How close (metres) the free hand has to be to the watch face.</summary>
        public const float WatchReach = 0.15f;
        /// <summary>Seconds to hold the hand over the watch to skip the night.</summary>
        public const float SkipHoldSeconds = 1.5f;
        static readonly Color ClockDay = new Color(1f, 0.93f, 0.7f, 0.95f);
        static readonly Color ClockNight = new Color(0.65f, 0.78f, 1f, 0.95f);

        /// <summary>The time shown on the watch face ("HH:MM"). Exposed for tests.</summary>
        public string ClockLabel => _clockText != null ? _clockText.text : null;

        /// <summary>Pure check used by the hold gesture (and the tests).</summary>
        public static bool HandNearWatch(Vector3 hand, Vector3 watch) =>
            Vector3.Distance(hand, watch) < WatchReach;

        static readonly Color Panel = new Color(0.07f, 0.1f, 0.06f, 0.55f);
        static readonly Color HealthCol = new Color(0.85f, 0.3f, 0.3f, 0.9f);
        static readonly Color HydrationCol = new Color(0.3f, 0.55f, 0.9f, 0.9f);
        static readonly Color EnergyCol = new Color(0.9f, 0.62f, 0.25f, 0.9f);
        static readonly Color WarmthCol = new Color(0.92f, 0.82f, 0.35f, 0.9f);

        void Awake()
        {
            BuildUi();
        }

        void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.ObjectivesChanged += RefreshObjective;
                RefreshObjective();
            }
        }

        void OnDestroy()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.ObjectivesChanged -= RefreshObjective;
        }

        void Update()
        {
            if (vitals != null)
            {
                SetFill(_healthFill, vitals.health);
                SetFill(_hydrationFill, vitals.hydration);
                SetFill(_energyFill, vitals.energy);
                SetFill(_warmthFill, vitals.warmth);
            }
            UpdateClockAndSkip();
        }

        void UpdateClockAndSkip()
        {
            if (_dayNight == null) _dayNight = FindFirstObjectByType<DayNightCycle>();
            if (_dayNight == null) return;

            bool night = _dayNight.isNight;
            if (_clockText != null)
            {
                _clockText.text = _dayNight.ClockString;
                _clockText.color = night ? ClockNight : ClockDay;
            }

            bool can = _dayNight.CanSkipNight;
            bool near = false;
            if (can && _canvasRect != null && TryGetRightHandPosition(out var hand))
                near = HandNearWatch(hand, _canvasRect.position);

            if (near) _holdTimer += Time.deltaTime;
            else _holdTimer = Mathf.Max(0f, _holdTimer - Time.deltaTime * 2f);

            if (near != _handNear)
            {
                _handNear = near;
                RefreshObjective();
            }

            if (_skipTrack != null) _skipTrack.gameObject.SetActive(can && _holdTimer > 0.02f);
            if (_skipFill != null) _skipFill.anchorMax = new Vector2(Mathf.Clamp01(_holdTimer / SkipHoldSeconds), 1f);

            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool key = kb != null && kb.nKey.wasPressedThisFrame;
            if (can && (key || _holdTimer >= SkipHoldSeconds))
            {
                _holdTimer = 0f;
                _dayNight.SkipToMorning(false);
            }
        }

        /// <summary>The right hand: the controller, or the tracked palm when hands are in use.</summary>
        bool TryGetRightHandPosition(out Vector3 pos)
        {
            var right = PlayerHands.Right;
            if (right != null && right.gameObject.activeInHierarchy)
            {
                pos = right.position;
                return true;
            }

            _handSubsystems.Clear();
            SubsystemManager.GetSubsystems(_handSubsystems);
            foreach (var sub in _handSubsystems)
            {
                if (!sub.running || !sub.rightHand.isTracked) continue;
                if (!sub.rightHand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose pose)) continue;
                if (_origin == null) _origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                var space = _origin != null && _origin.CameraFloorOffsetObject != null
                    ? _origin.CameraFloorOffsetObject.transform : null;
                pos = space != null ? space.TransformPoint(pose.position) : pose.position;
                return true;
            }
            pos = default;
            return false;
        }

        void RefreshObjective()
        {
            if (_objectiveText == null || GameManager.Instance == null) return;
            if (_handNear && _dayNight != null && _dayNight.CanSkipNight)
            {
                _objectiveText.text = "Keep your hand here to skip to morning";
                return;
            }
            var current = GameManager.Instance.CurrentObjective;
            _objectiveText.text = current != null ? current.title : "All objectives complete!";
        }

        static void SetFill(RectTransform fill, float value01to100)
        {
            if (fill == null) return;
            fill.anchorMax = new Vector2(Mathf.Clamp01(value01to100 / 100f), 1f);
        }

        void BuildUi()
        {
            var canvasGo = new GameObject("WristCanvas", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(230, 110);
            canvasRect.localScale = Vector3.one * 0.00045f; // ~10cm x 5cm — a wristwatch, not a billboard
            canvasRect.localPosition = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;

            MakeRect(canvasRect, "Background", Vector2.zero, Vector2.one, Panel);

            _canvasRect = canvasRect;

            _objectiveText = MakeText(canvasRect, "ObjectiveText", "—", 12f,
                new Vector2(0.05f, 0.62f), new Vector2(0.73f, 0.97f));
            _objectiveText.color = new Color(1f, 1f, 1f, 0.92f);

            _clockText = MakeText(canvasRect, "ClockText", "06:00", 13f,
                new Vector2(0.74f, 0.62f), new Vector2(0.97f, 0.97f));
            _clockText.alignment = TextAlignmentOptions.MidlineRight;
            _clockText.color = ClockDay;

            // skip-the-night progress, a thin bar between the objective and the vitals
            _skipTrack = MakeRect(canvasRect, "SkipTrack", new Vector2(0.05f, 0.575f), new Vector2(0.97f, 0.61f),
                new Color(0f, 0f, 0f, 0.45f));
            _skipFill = MakeRect(_skipTrack, "SkipFill", Vector2.zero, Vector2.one, ClockNight);
            _skipTrack.gameObject.SetActive(false);

            _healthFill = MakeBar(canvasRect, "HP", HealthCol, 0.46f);
            _hydrationFill = MakeBar(canvasRect, "H2O", HydrationCol, 0.33f);
            _energyFill = MakeBar(canvasRect, "Food", EnergyCol, 0.20f);
            _warmthFill = MakeBar(canvasRect, "Warm", WarmthCol, 0.07f);
        }

        RectTransform MakeBar(RectTransform parent, string label, Color color, float yMin)
        {
            float yMax = yMin + 0.10f;
            var text = MakeText(parent, label + "Label", label, 8.5f,
                new Vector2(0.05f, yMin - 0.015f), new Vector2(0.22f, yMax + 0.015f));
            text.color = new Color(1f, 1f, 1f, 0.75f);

            var track = MakeRect(parent, label + "Track", new Vector2(0.24f, yMin), new Vector2(0.96f, yMax),
                new Color(0f, 0f, 0f, 0.4f));

            var fill = MakeRect(track, label + "Fill", Vector2.zero, Vector2.one, color);
            fill.offsetMin = new Vector2(1, 1);
            fill.offsetMax = new Vector2(-1, -1);
            return fill;
        }

        static RectTransform MakeRect(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rect;
        }

        static TextMeshProUGUI MakeText(RectTransform parent, string name, string text, float size,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }
    }
}

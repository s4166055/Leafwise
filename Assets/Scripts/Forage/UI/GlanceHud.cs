using UnityEngine;
using UnityEngine.UI;

namespace Forage
{
    /// <summary>
    /// Minimal vitals strip that floats low in the player's view with a lazy
    /// follow (never head-locked). It fades in only when something needs
    /// attention — a vital is low, or just changed — and fades away otherwise.
    /// The wrist panel stays the detailed readout.
    /// </summary>
    public class GlanceHud : MonoBehaviour
    {
        public PlayerVitals vitals;

        [Header("Behavior")]
        public float lowThreshold = 35f;
        public float showAfterChange = 3.5f;
        public float introSeconds = 15f;   // always visible at session start so players learn it exists

        CanvasGroup _group;
        RectTransform[] _fills = new RectTransform[4];
        float[] _lastValues = new float[4];
        float _changeTimer;
        Transform _head;

        static readonly Color[] BarColors =
        {
            new Color(0.85f, 0.3f, 0.3f, 0.95f),   // health
            new Color(0.3f, 0.55f, 0.9f, 0.95f),   // hydration
            new Color(0.9f, 0.62f, 0.25f, 0.95f),  // food
            new Color(0.92f, 0.82f, 0.35f, 0.95f)  // warmth
        };

        void Awake()
        {
            BuildUi();
        }

        void BuildUi()
        {
            var canvasGo = new GameObject("GlanceCanvas", typeof(Canvas), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            _group = canvasGo.GetComponent<CanvasGroup>();

            var rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(300, 34);
            rect.localScale = Vector3.one * 0.00062f; // ~18.5cm x 2cm at arm's length

            // faint backdrop so it reads over any ground
            var bg = MakeRect(rect, "Bg", Vector2.zero, Vector2.one, new Color(0.05f, 0.07f, 0.05f, 0.45f));

            for (int i = 0; i < 4; i++)
            {
                float x0 = 0.02f + i * 0.245f;
                var track = MakeRect(rect, "Track" + i, new Vector2(x0, 0.2f), new Vector2(x0 + 0.225f, 0.8f),
                    new Color(0f, 0f, 0f, 0.5f));
                var fill = MakeRect(track, "Fill" + i, Vector2.zero, Vector2.one, BarColors[i]);
                fill.offsetMin = new Vector2(1, 1);
                fill.offsetMax = new Vector2(-1, -1);
                _fills[i] = fill;
            }
        }

        static RectTransform MakeRect(RectTransform parent, string name, Vector2 min, Vector2 max, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = false;
            return rect;
        }

        void LateUpdate()
        {
            if (vitals == null) return;
            if (_head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _head = cam.transform;
                _lastValues = new[] { vitals.health, vitals.hydration, vitals.energy, vitals.warmth };
            }

            // --- values + change detection ---
            float[] now = { vitals.health, vitals.hydration, vitals.energy, vitals.warmth };
            bool anyLow = false;
            for (int i = 0; i < 4; i++)
            {
                _fills[i].anchorMax = new Vector2(Mathf.Clamp01(now[i] / 100f), 1f);
                if (now[i] < lowThreshold) anyLow = true;
                if (Mathf.Abs(now[i] - _lastValues[i]) > 4f)
                {
                    _changeTimer = showAfterChange;
                    _lastValues[i] = now[i];
                }
            }
            _changeTimer -= Time.deltaTime;

            bool show = anyLow || _changeTimer > 0f || Time.timeSinceLevelLoad < introSeconds || vitals.isSick;
            _group.alpha = Mathf.MoveTowards(_group.alpha, show ? 0.95f : 0f, Time.deltaTime * 2.5f);

            // --- lazy follow: low and centered, drifting after the head ---
            Vector3 fwd = _head.forward;
            fwd.y = 0;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 targetPos = _head.position + fwd * 1.15f + Vector3.down * 0.52f;
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * 4f);
            var look = Quaternion.LookRotation(transform.position - _head.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 4f);
        }
    }
}

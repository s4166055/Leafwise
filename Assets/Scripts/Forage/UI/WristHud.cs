using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Forage
{
    /// <summary>
    /// Small, subtle wrist HUD: four thin vitals bars and the current
    /// objective on a translucent ~10cm panel. Built in code at Awake.
    /// </summary>
    public class WristHud : MonoBehaviour
    {
        public PlayerVitals vitals;

        RectTransform _healthFill, _hydrationFill, _energyFill, _warmthFill;
        TextMeshProUGUI _objectiveText;

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
            if (vitals == null) return;
            SetFill(_healthFill, vitals.health);
            SetFill(_hydrationFill, vitals.hydration);
            SetFill(_energyFill, vitals.energy);
            SetFill(_warmthFill, vitals.warmth);
        }

        void RefreshObjective()
        {
            if (_objectiveText == null || GameManager.Instance == null) return;
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

            _objectiveText = MakeText(canvasRect, "ObjectiveText", "—", 12.5f,
                new Vector2(0.05f, 0.62f), new Vector2(0.97f, 0.97f));
            _objectiveText.color = new Color(1f, 1f, 1f, 0.92f);

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

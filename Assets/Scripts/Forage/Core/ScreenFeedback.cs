using UnityEngine;
using UnityEngine.UI;

namespace Forage
{
    /// <summary>
    /// In-view feedback: a soft radial vignette that flashes red on damage,
    /// green when eating, blue when drinking, and pulses sickly green-yellow
    /// while poisoned. Attached near the camera; built in code.
    /// </summary>
    public class ScreenFeedback : MonoBehaviour
    {
        static ScreenFeedback _instance;
        public static ScreenFeedback Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<ScreenFeedback>();
                return _instance;
            }
        }

        Image _vignette;
        Color _flashColor;
        float _flashStrength;
        Transform _head;

        void Awake()
        {
            _instance = this;
            BuildUi();
        }

        void BuildUi()
        {
            var canvasGo = new GameObject("FeedbackCanvas", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(512, 512);
            rect.localScale = Vector3.one * 0.004f; // ~2m plane right in front of the eyes

            var imgGo = new GameObject("Vignette", typeof(Image));
            imgGo.transform.SetParent(rect, false);
            var imgRect = imgGo.GetComponent<RectTransform>();
            imgRect.anchorMin = Vector2.zero;
            imgRect.anchorMax = Vector2.one;
            imgRect.offsetMin = Vector2.zero;
            imgRect.offsetMax = Vector2.zero;
            _vignette = imgGo.GetComponent<Image>();
            _vignette.sprite = Sprite.Create(MakeRadialTexture(), new Rect(0, 0, 256, 256), Vector2.one * 0.5f);
            _vignette.color = Color.clear;
            _vignette.raycastTarget = false;
        }

        static Texture2D MakeRadialTexture()
        {
            int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size * 0.5f) / (size * 0.5f);
                    float dy = (y - size * 0.5f) / (size * 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // transparent center, strong edges
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.35f) / 0.65f));
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        public static void Flash(Color color, float strength = 0.8f)
        {
            var inst = Instance;
            if (inst == null) return;
            inst._flashColor = color;
            inst._flashStrength = Mathf.Max(inst._flashStrength, strength);
        }

        public static void Damage() => Flash(new Color(0.8f, 0.1f, 0.08f), 0.9f);
        public static void Eat() => Flash(new Color(0.35f, 0.7f, 0.25f), 0.5f);
        public static void Drink() => Flash(new Color(0.25f, 0.5f, 0.85f), 0.5f);

        void LateUpdate()
        {
            if (_head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _head = cam.transform;
            }

            // hover the canvas just in front of the eyes
            transform.position = _head.position + _head.forward * 0.5f;
            transform.rotation = _head.rotation;

            // decay flash
            _flashStrength = Mathf.Max(0f, _flashStrength - Time.deltaTime * 1.4f);

            // sickness pulse rides underneath flashes
            float sick = 0f;
            var gm = GameManager.Instance;
            if (gm != null && gm.vitals != null && gm.vitals.isSick)
                sick = 0.22f + Mathf.Sin(Time.time * 2.2f) * 0.08f;

            Color target;
            if (_flashStrength > 0.01f)
                target = new Color(_flashColor.r, _flashColor.g, _flashColor.b, _flashStrength * 0.6f);
            else if (sick > 0f)
                target = new Color(0.5f, 0.6f, 0.15f, sick);
            else
                target = Color.clear;

            _vignette.color = Color.Lerp(_vignette.color, target, Time.deltaTime * 8f);
        }
    }
}

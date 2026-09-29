using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Forage
{
    /// <summary>
    /// A floating educational card (mushroom facts, survival lessons).
    /// Crisp, compact, placed slightly below and to the side of the player's
    /// gaze so it never fills the view. One at a time; fades in and out.
    /// </summary>
    public class FactCard : MonoBehaviour
    {
        static FactCard _current;

        CanvasGroup _group;
        float _life;
        const float Lifetime = 8f;

        public static void Show(string title, string body, bool good)
        {
            if (_current != null) Destroy(_current.gameObject);

            var gm = GameManager.Instance;
            var head = gm != null && gm.playerHead != null ? gm.playerHead
                : Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;

            var go = new GameObject("FactCard", typeof(Canvas), typeof(CanvasGroup), typeof(FactCard));
            _current = go.GetComponent<FactCard>();
            _current._group = go.GetComponent<CanvasGroup>();
            _current._group.alpha = 0f;

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = go.GetComponent<RectTransform>();
            // high pixel density + small world size = crisp text
            rect.sizeDelta = new Vector2(760, 380);
            rect.localScale = Vector3.one * 0.00048f; // ~36cm x 18cm

            Vector3 fwd = new Vector3(head.forward.x, 0, head.forward.z).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            go.transform.position = head.position + fwd * 1.05f + right * 0.28f + Vector3.down * 0.28f;
            go.transform.rotation = Quaternion.LookRotation(go.transform.position - head.position);

            var accent = good ? new Color(0.45f, 0.72f, 0.35f) : new Color(0.88f, 0.36f, 0.3f);

            var border = MakeRect(rect, "Accent", accent);
            border.anchorMin = new Vector2(0, 0);
            border.anchorMax = new Vector2(0.012f, 1); // slim colored edge strip
            MakeRect(rect, "Background", new Color(0.08f, 0.1f, 0.07f, 0.88f))
                .SetSiblingIndex(0);

            var titleText = MakeText(rect, "Title", title, 40,
                new Vector2(0.05f, 0.74f), new Vector2(0.97f, 0.97f));
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = accent * 1.35f;
            MakeText(rect, "Body", body, 29, new Vector2(0.05f, 0.07f), new Vector2(0.97f, 0.72f));
        }

        void Update()
        {
            _life += Time.deltaTime;
            if (_group != null)
            {
                float fadeIn = Mathf.Clamp01(_life / 0.35f);
                float fadeOut = Mathf.Clamp01(Lifetime - _life);
                _group.alpha = Mathf.Min(fadeIn, fadeOut);
            }
            if (_life > Lifetime)
                Destroy(gameObject);

            var gm = GameManager.Instance;
            if (gm != null && gm.playerHead != null)
            {
                Vector3 to = transform.position - gm.playerHead.position;
                if (to.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(to), Time.deltaTime * 4f);
            }
        }

        internal static RectTransform MakeRect(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rect;
        }

        internal static TextMeshProUGUI MakeText(RectTransform parent, string name, string text, float size,
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
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Forage
{
    /// <summary>
    /// Scout — the visible AI helper from the pitch. A small glowing firefly
    /// bot that hovers by your shoulder, voices every gameplay hint in a
    /// speech bubble (escalating from subtle to direct on repeats), and
    /// answers on demand: press H (simulator) or a controller primary button
    /// to ask about the current objective.
    /// </summary>
    public class ScoutCompanion : MonoBehaviour
    {
        Transform _head;
        Transform _bubble;
        TextMeshProUGUI _bubbleText;
        CanvasGroup _bubbleGroup;
        float _bubbleTimer;
        float _askCooldown;
        float _bobPhase;
        readonly Dictionary<string, int> _hintCounts = new Dictionary<string, int>();

        static readonly Dictionary<string, string[]> Hints = new Dictionary<string, string[]>
        {
            // [0] subtle, [1] direct
            { "fire-no-tinder", new[]{ "Hmm, the pit looks empty. What catches a spark best?", "Drop a TINDER bundle into the stone ring first — dry fluff catches the spark." } },
            { "fire-no-wood", new[]{ "Tinder alone burns out fast…", "Add STICKS to the pit — tinder starts the flame, wood keeps it alive." } },
            { "wood-damp", new[]{ "That wood sounds… squishy.", "Sticks from near the pond are DAMP — they barely heat. Find dry wood away from water." } },
            { "drill-too-slow", new[]{ "A little more elbow grease!", "Scrub the drill FASTER — friction only makes heat past a certain speed." } },
            { "strike-harder", new[]{ "Tiny sparks… almost!", "Strike the stones together HARDER — a fast, sharp hit makes a real spark." } },
            { "sparks-too-far-from-pit", new[]{ "Nice spark! But it landed on dirt.", "Strike right OVER the fire pit so the sparks land in the tinder." } },
            { "boil-interrupted", new[]{ "The bubbles stopped…", "Keep the pot by the fire until it fully boils — half-boiled water is still unsafe." } },
            { "about-to-drink-dirty", new[]{ "Wait — did that water boil?", "STOP — that water is untreated! Boil it over the fire first, or you'll get sick." } },
            { "about-to-eat-suspicious-mushroom", new[]{ "Are you sure about that one?", "That mushroom looks DANGEROUS. Check the tag — when in doubt, never eat it." } },
            { "snake-freeze", new[]{ "Snake! Hold still…", "FREEZE! Snakes strike at movement. Stand still, then back away slowly." } },
            { "rabbit-scared", new[]{ "Too fast — you startled it.", "Move SLOWLY near animals. Walk calmly and the rabbit may come to you." } },
            { "deer-spooked", new[]{ "There it goes…", "Deer spook at speed and close approach. Watch from 5+ meters, moving gently." } },
            { "bear-dont-run", new[]{ "B-bear! Don't run. DON'T run.", "Face it, look BIG, and back away SLOWLY. Running triggers a chase you cannot win." } },
            { "shelter-branches-first", new[]{ "Leaves need something to rest on.", "Lean BRANCHES on the frame first — then cover them with leaf bundles." } },
        };

        static readonly Dictionary<string, string> ObjectiveTips = new Dictionary<string, string>
        {
            { "explore", "Have a wander! Get a feel for the clearing, the pond to the north-east, and the woods." },
            { "fire", "Gather tinder + dry sticks into the stone ring, then drill fast on the fireboard — or strike the two flint stones together hard, 2–3 times." },
            { "water", "Dip the pot in the pond, boil it by the fire until it bubbles clear, then drink." },
            { "forage", "Pick a mushroom and read its tag. Brown or golden ones are your friends. Pale ones… are not." },
            { "shelter", "Lean 4 branches against the shelter frame at camp, then pile 3 leaf bundles on top." },
            { "wildlife", "Approach the rabbit or deer slowly — or freeze when a snake rears up. Calm wins." },
            { "survive", "Night is coming. Keep the fire fed and the shelter ready — warmth is life out here." },
        };

        void Start()
        {
            BuildBody();
            ForageEvents.Hint += OnHint;
        }

        void OnDestroy() => ForageEvents.Hint -= OnHint;

        void BuildBody()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var glow = new Material(lit);
            glow.SetColor("_BaseColor", new Color(0.65f, 0.95f, 0.5f));
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(0.45f, 0.9f, 0.35f) * 1.4f);

            var body = new GameObject("ScoutBody");
            body.transform.SetParent(transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.05f, 2, 0.02f, 4242, Vector3.one);
            body.AddComponent<MeshRenderer>().sharedMaterial = glow;

            var light = body.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.6f, 1f, 0.5f);
            light.intensity = 0.7f;
            light.range = 1.6f;
            light.shadows = LightShadows.None;

            // speech bubble
            var canvasGo = new GameObject("Bubble", typeof(Canvas), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            _bubble = canvasGo.transform;
            _bubbleGroup = canvasGo.GetComponent<CanvasGroup>();
            _bubbleGroup.alpha = 0f;
            var rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(560, 170);
            rect.localScale = Vector3.one * 0.00055f;
            rect.localPosition = new Vector3(0, 0.16f, 0);

            var bgGo = new GameObject("Bg", typeof(Image));
            bgGo.transform.SetParent(rect, false);
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            bgGo.GetComponent<Image>().color = new Color(0.09f, 0.13f, 0.08f, 0.92f);

            var txtGo = new GameObject("Text", typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(rect, false);
            var txtRect = txtGo.GetComponent<RectTransform>();
            txtRect.anchorMin = new Vector2(0.05f, 0.08f); txtRect.anchorMax = new Vector2(0.95f, 0.92f);
            txtRect.offsetMin = Vector2.zero; txtRect.offsetMax = Vector2.zero;
            _bubbleText = txtGo.GetComponent<TextMeshProUGUI>();
            _bubbleText.fontSize = 30;
            _bubbleText.color = new Color(0.92f, 0.98f, 0.88f);
            _bubbleText.alignment = TextAlignmentOptions.Midline;
            _bubbleText.textWrappingMode = TextWrappingModes.Normal;
        }

        void OnHint(string id)
        {
            if (!Hints.TryGetValue(id, out var lines)) return;
            _hintCounts.TryGetValue(id, out int count);
            _hintCounts[id] = count + 1;
            Say(lines[count >= 2 ? 1 : 0]); // escalate to direct wording on repeats
        }

        public void Say(string text)
        {
            if (_bubbleText == null) return;
            _bubbleText.text = "Scout: " + text;
            _bubbleTimer = Mathf.Clamp(2.5f + text.Length * 0.045f, 3.5f, 8f);
            var gm = GameManager.Instance;
            if (gm != null)
                ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Chirp(3), 0.45f);
        }

        void AskScout()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var current = gm.CurrentObjective;
            if (current != null && ObjectiveTips.TryGetValue(current.id, out var tip)) Say(tip);
            else Say("You've done everything on the list — enjoy the forest, survivor!");
        }

        static bool AskPressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame) return true;
            var devices = new List<UnityEngine.XR.InputDevice>();
            foreach (var node in new[] { UnityEngine.XR.XRNode.LeftHand, UnityEngine.XR.XRNode.RightHand })
            {
                devices.Clear();
                UnityEngine.XR.InputDevices.GetDevicesAtXRNode(node, devices);
                foreach (var d in devices)
                    if (d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool pressed) && pressed)
                        return true;
            }
            return false;
        }

        void LateUpdate()
        {
            if (_head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _head = cam.transform;
            }

            // hover by the right shoulder with a lazy follow and a gentle bob
            _bobPhase += Time.deltaTime * 2.2f;
            Vector3 fwd = _head.forward; fwd.y = 0; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 target = _head.position + fwd * 0.55f + right * 0.42f +
                             Vector3.up * (-0.05f + Mathf.Sin(_bobPhase) * 0.03f);
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * 3.2f);

            if (_bubble != null)
            {
                Vector3 to = _bubble.position - _head.position;
                if (to.sqrMagnitude > 0.001f) _bubble.rotation = Quaternion.LookRotation(to);
                _bubbleTimer -= Time.deltaTime;
                _bubbleGroup.alpha = Mathf.MoveTowards(_bubbleGroup.alpha, _bubbleTimer > 0 ? 1f : 0f, Time.deltaTime * 4f);
            }

            _askCooldown -= Time.deltaTime;
            if (_askCooldown <= 0f && AskPressed())
            {
                _askCooldown = 2.5f;
                AskScout();
            }
        }
    }
}

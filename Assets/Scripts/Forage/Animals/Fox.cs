using UnityEngine;
using System.Linq;

namespace Forage
{
    /// <summary>
    /// The camp-discipline teacher: a fox that prowls the woods and steals any
    /// food left lying on the ground — unless it is near your burning fire or
    /// finished shelter. Lesson: secure your food.
    /// </summary>
    public class Fox : Animal
    {
        public enum State { Prowl, Stalk, Steal, Flee }

        [Header("State (read-only)")]
        public State state = State.Prowl;

        SurvivalItem _target;
        float _stateTimer;
        float _stealCooldown;
        Transform _tail;

        void Start()
        {
            BuildBody();
            agent.speed = 1.9f;
            agent.angularSpeed = 540f;
            agent.radius = 0.22f;
            agent.height = 0.6f;
            agent.SetDestination(RandomPoint(transform.position, 16f));
        }

        void BuildBody()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var rust = new Material(lit); rust.SetColor("_BaseColor", new Color(0.72f, 0.36f, 0.16f));
            var cream = new Material(lit); cream.SetColor("_BaseColor", new Color(0.9f, 0.85f, 0.75f));
            var dark = new Material(lit); dark.SetColor("_BaseColor", new Color(0.2f, 0.15f, 0.1f));

            var root = new GameObject("Body").transform;
            root.SetParent(transform, false);
            SetBody(root);

            var torso = new GameObject("Torso");
            torso.transform.SetParent(root, false);
            torso.transform.localPosition = new Vector3(0, 0.28f, 0);
            torso.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.17f, 1, 0.05f, GetInstanceID(), new Vector3(0.75f, 0.75f, 1.6f));
            torso.AddComponent<MeshRenderer>().sharedMaterial = rust;

            var head = new GameObject("Head");
            head.transform.SetParent(root, false);
            head.transform.localPosition = new Vector3(0, 0.4f, 0.28f);
            head.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.1f, 1, 0.05f, GetInstanceID() + 1, new Vector3(0.85f, 0.8f, 1.1f));
            head.AddComponent<MeshRenderer>().sharedMaterial = rust;

            var snout = new GameObject("Snout");
            snout.transform.SetParent(head.transform, false);
            snout.transform.localPosition = new Vector3(0, -0.02f, 0.1f);
            snout.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            snout.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(0.045f, 0.12f, 6, 0.015f);
            snout.AddComponent<MeshRenderer>().sharedMaterial = cream;

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var ear = new GameObject("Ear");
                ear.transform.SetParent(head.transform, false);
                ear.transform.localPosition = new Vector3(side * 0.06f, 0.09f, 0f);
                ear.transform.localRotation = Quaternion.Euler(-12f, 0, side * 14f);
                ear.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(0.035f, 0.09f, 4, 0.008f);
                ear.AddComponent<MeshRenderer>().sharedMaterial = dark;
            }

            // the trademark bushy tail
            _tail = new GameObject("Tail").transform;
            _tail.SetParent(root, false);
            _tail.localPosition = new Vector3(0, 0.32f, -0.3f);
            _tail.localRotation = Quaternion.Euler(-35f, 0, 0);
            var tailMesh = new GameObject("TailMesh");
            tailMesh.transform.SetParent(_tail, false);
            tailMesh.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.09f, 1, 0.08f, GetInstanceID() + 2, new Vector3(0.7f, 0.7f, 2.2f));
            tailMesh.AddComponent<MeshRenderer>().sharedMaterial = rust;
            var tip = new GameObject("Tip");
            tip.transform.SetParent(_tail, false);
            tip.transform.localPosition = new Vector3(0, 0, -0.22f);
            tip.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.05f, 1, 0.06f, GetInstanceID() + 3, Vector3.one);
            tip.AddComponent<MeshRenderer>().sharedMaterial = cream;

            for (int i = 0; i < 4; i++)
            {
                var leg = new GameObject("Leg" + i);
                leg.transform.SetParent(root, false);
                leg.transform.localPosition = new Vector3(i % 2 == 0 ? -0.08f : 0.08f, 0.24f, i < 2 ? 0.15f : -0.16f);
                leg.transform.localRotation = Quaternion.Euler(180f, 0, 0);
                leg.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(0.026f, 0.018f, 0.24f, 4, 1, 0.005f, GetInstanceID() + 4 + i, 1f);
                leg.AddComponent<MeshRenderer>().sharedMaterial = dark;
            }
        }

        static bool IsFood(SurvivalItem item) =>
            item != null && (item.kind == ItemKind.Mushroom || item.kind == ItemKind.Fish);

        static bool IsSecured(Vector3 pos)
        {
            var fire = FirePit.Instance;
            if (fire != null && fire.IsLit && Vector3.Distance(pos, fire.transform.position) < 4f) return true;
            var shelter = Shelter.Instance;
            if (shelter != null && shelter.IsComplete && Vector3.Distance(pos, shelter.transform.position) < 3.5f) return true;
            return false;
        }

        void Update()
        {
            _stateTimer += Time.deltaTime;
            _stealCooldown -= Time.deltaTime;

            switch (state)
            {
                case State.Prowl:
                    if (agent.enabled && agent.remainingDistance < 1f)
                        agent.SetDestination(RandomPoint(transform.position, 16f));
                    if (_stealCooldown <= 0f && _stateTimer > 2f)
                    {
                        _target = FindObjectsByType<SurvivalItem>(FindObjectsSortMode.None)
                            .Where(IsFood)
                            .Where(x => !IsSecured(x.transform.position))
                            .Where(x =>
                            {
                                var grab = x.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                                return grab == null || (!grab.isSelected && grab.enabled);
                            })
                            .OrderBy(x => (x.transform.position - transform.position).sqrMagnitude)
                            .FirstOrDefault(x => (x.transform.position - transform.position).sqrMagnitude < 18f * 18f);
                        if (_target != null) Set(State.Stalk);
                    }
                    break;

                case State.Stalk:
                    if (_target == null || IsSecured(_target.transform.position)) { Set(State.Prowl); break; }
                    agent.speed = 3.4f;
                    agent.SetDestination(_target.transform.position);
                    if (Vector3.Distance(transform.position, _target.transform.position) < 0.7f)
                        Set(State.Steal);
                    if (_stateTimer > 14f) { agent.speed = 1.9f; Set(State.Prowl); }
                    break;

                case State.Steal:
                    if (_target != null)
                    {
                        ForageEvents.RaiseSignal("fox-stole-food");
                        FactCard.Show("A fox stole your food!",
                            "Food left on open ground attracts scavengers. Keep your finds close to the campfire " +
                            "or inside your shelter — animals avoid flames and enclosed spaces.", good: false);
                        Destroy(_target.gameObject);
                        _target = null;
                    }
                    _stealCooldown = 45f;
                    Set(State.Flee);
                    break;

                case State.Flee:
                    if (_stateTimer < 0.05f || (agent.enabled && agent.remainingDistance < 1f))
                    {
                        Vector3 away = GameManager.Instance != null
                            ? (transform.position - GameManager.Instance.PlayerPosition).normalized
                            : Random.onUnitSphere;
                        away.y = 0;
                        agent.speed = 4.2f;
                        agent.SetDestination(RandomPoint(transform.position + away * 18f, 5f));
                    }
                    if (_stateTimer > 6f) { agent.speed = 1.9f; Set(State.Prowl); }
                    break;
            }

            // tail sway
            if (_tail != null)
                _tail.localRotation = Quaternion.Euler(-35f + Mathf.Sin(Time.time * 3f) * 8f,
                    Mathf.Sin(Time.time * 2.2f) * 14f, 0);

            AnimateGait(0.05f, 13f);
        }

        public void ForceState(State s) => Set(s);

        void Set(State s)
        {
            state = s;
            _stateTimer = 0f;
        }
    }
}

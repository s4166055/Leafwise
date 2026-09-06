using UnityEngine;

namespace Forage
{
    /// <summary>
    /// The observation teacher: a deer grazing in the meadows. Move slowly and
    /// keep a respectful distance to watch it — rush it and it bolts.
    /// Lesson: quiet, patient observation.
    /// </summary>
    public class Deer : Animal
    {
        public enum State { Graze, Watch, Bolt, Calm }

        [Header("State (read-only)")]
        public State state = State.Graze;

        Transform _neck;
        float _stateTimer;
        float _observeTimer;
        bool _rewarded;

        void Start()
        {
            BuildBody();
            agent.speed = 1.1f;
            agent.angularSpeed = 300f;
            agent.radius = 0.35f;
            agent.height = 1.6f;
            noticeDistance = 10f;
            scarySpeed = 1.2f;
            agent.SetDestination(RandomPoint(transform.position, 12f));
        }

        void BuildBody()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var hide = new Material(lit); hide.SetColor("_BaseColor", new Color(0.55f, 0.42f, 0.28f));
            var dark = new Material(lit); dark.SetColor("_BaseColor", new Color(0.35f, 0.26f, 0.17f));

            var root = new GameObject("Body").transform;
            root.SetParent(transform, false);
            SetBody(root);

            // torso
            var torso = new GameObject("Torso");
            torso.transform.SetParent(root, false);
            torso.transform.localPosition = new Vector3(0, 0.85f, 0);
            torso.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.34f, 1, 0.05f, GetInstanceID(), new Vector3(0.75f, 0.8f, 1.5f));
            torso.AddComponent<MeshRenderer>().sharedMaterial = hide;

            // legs
            for (int i = 0; i < 4; i++)
            {
                var leg = new GameObject("Leg" + i);
                leg.transform.SetParent(root, false);
                leg.transform.localPosition = new Vector3(i % 2 == 0 ? -0.14f : 0.14f, 0.8f, i < 2 ? 0.32f : -0.34f);
                leg.transform.localRotation = Quaternion.Euler(180f, 0, 0);
                leg.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(0.045f, 0.03f, 0.8f, 5, 1, 0.01f, GetInstanceID() + i, 1f);
                leg.AddComponent<MeshRenderer>().sharedMaterial = dark;
            }

            // neck + head (neck pivots for graze/alert poses)
            _neck = new GameObject("Neck").transform;
            _neck.SetParent(root, false);
            _neck.localPosition = new Vector3(0, 1.02f, 0.45f);
            var neckMesh = new GameObject("NeckMesh");
            neckMesh.transform.SetParent(_neck, false);
            neckMesh.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(0.09f, 0.07f, 0.5f, 6, 1, 0.03f, GetInstanceID() + 9, 1f);
            neckMesh.AddComponent<MeshRenderer>().sharedMaterial = hide;

            var head = new GameObject("Head");
            head.transform.SetParent(_neck, false);
            head.transform.localPosition = new Vector3(0, 0.52f, 0.05f);
            head.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.12f, 1, 0.05f, GetInstanceID() + 10, new Vector3(0.7f, 0.75f, 1.35f));
            head.AddComponent<MeshRenderer>().sharedMaterial = hide;

            // ears + simple antlers
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var ear = new GameObject("Ear");
                ear.transform.SetParent(head.transform, false);
                ear.transform.localPosition = new Vector3(side * 0.09f, 0.1f, -0.02f);
                ear.transform.localRotation = Quaternion.Euler(-20f, 0, side * 25f);
                ear.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(0.03f, 0.1f, 5, 0.01f);
                ear.AddComponent<MeshRenderer>().sharedMaterial = hide;

                var antler = new GameObject("Antler");
                antler.transform.SetParent(head.transform, false);
                antler.transform.localPosition = new Vector3(side * 0.05f, 0.12f, 0.02f);
                antler.transform.localRotation = Quaternion.Euler(-25f, 0, side * 30f);
                antler.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(0.018f, 0.008f, 0.32f, 4, 2, 0.08f, GetInstanceID() + 20 + i, 1f);
                antler.AddComponent<MeshRenderer>().sharedMaterial = dark;
                var prong = new GameObject("Prong");
                prong.transform.SetParent(antler.transform, false);
                prong.transform.localPosition = new Vector3(0, 0.18f, 0);
                prong.transform.localRotation = Quaternion.Euler(0, 0, side * 45f);
                prong.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(0.012f, 0.006f, 0.16f, 4, 1, 0.03f, GetInstanceID() + 30 + i, 1f);
                prong.AddComponent<MeshRenderer>().sharedMaterial = dark;
            }
        }

        void Update()
        {
            _stateTimer += Time.deltaTime;
            float dist = PlayerDistance;

            switch (state)
            {
                case State.Graze:
                    PoseNeck(grazing: true);
                    if (agent.enabled && agent.remainingDistance < 0.8f && _stateTimer > 4f)
                    {
                        agent.SetDestination(RandomPoint(transform.position, 12f));
                        _stateTimer = 0f;
                    }
                    if (dist < noticeDistance) Set(State.Watch);
                    break;

                case State.Watch:
                    PoseNeck(grazing: false);
                    agent.SetDestination(transform.position);
                    FacePlayer();
                    if (dist > noticeDistance + 4f) { Set(State.Graze); break; }
                    if (PlayerSpeed > scarySpeed || dist < 3.5f) { Bolt(); break; }

                    // quiet observation at a respectful distance earns the lesson
                    if (dist > 4f && dist < 9.5f && PlayerSpeed < 0.8f)
                    {
                        _observeTimer += Time.deltaTime;
                        if (_observeTimer > 6f && !_rewarded)
                        {
                            _rewarded = true;
                            GameManager.Instance.CompleteObjective("wildlife");
                            FactCard.Show("Wildlife observed!",
                                "You watched the deer quietly, from a distance it could accept. That is exactly how " +
                                "wildlife observation works: slow movement, no direct rush, and patience.", good: true);
                            Set(State.Calm);
                        }
                    }
                    else _observeTimer = Mathf.Max(0f, _observeTimer - Time.deltaTime);
                    break;

                case State.Calm:
                    PoseNeck(grazing: _stateTimer % 6f > 3f);
                    if (PlayerSpeed > scarySpeed || dist < 3f) { Bolt(); break; }
                    if (_stateTimer > 10f) Set(State.Graze);
                    break;

                case State.Bolt:
                    PoseNeck(grazing: false);
                    if (_stateTimer < 0.05f || (agent.enabled && agent.remainingDistance < 1f))
                    {
                        Vector3 away = (transform.position - GameManager.Instance.PlayerPosition).normalized;
                        away.y = 0;
                        agent.speed = 7f;
                        agent.SetDestination(RandomPoint(transform.position + away * 22f, 6f));
                    }
                    if (_stateTimer > 6f) { agent.speed = 1.1f; Set(State.Graze); }
                    break;
            }

            AnimateGait(0.05f, 10f);
        }

        public void Bolt()
        {
            if (state != State.Bolt) ForageEvents.RaiseHint("deer-spooked");
            Set(State.Bolt);
        }

        public void ForceState(State s) => Set(s);

        void Set(State s)
        {
            state = s;
            _stateTimer = 0f;
            if (s != State.Watch) _observeTimer = 0f;
        }

        void PoseNeck(bool grazing)
        {
            if (_neck == null) return;
            var target = grazing ? Quaternion.Euler(105f, 0, 0) : Quaternion.Euler(18f, 0, 0);
            _neck.localRotation = Quaternion.Slerp(_neck.localRotation, target, Time.deltaTime * 4f);
        }
    }
}

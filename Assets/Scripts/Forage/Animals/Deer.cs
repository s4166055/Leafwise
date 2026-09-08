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

        /// <summary>Small solid-colour blob helper for eyes and detail parts.</summary>
        static void Blob(Transform parent, string name, float radius, Color color, Vector3 localPos, int seed)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(radius, 1, 0.03f, seed, Vector3.one);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void BuildBody()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var hide = new Material(lit); hide.SetColor("_BaseColor", new Color(0.55f, 0.42f, 0.28f));
            var dark = new Material(lit); dark.SetColor("_BaseColor", new Color(0.35f, 0.26f, 0.17f));

            var root = new GameObject("Body").transform;
            root.SetParent(transform, false);
            SetBody(root);

            var pale = new Material(lit); pale.SetColor("_BaseColor", new Color(0.82f, 0.76f, 0.66f));

            // deep chest tapering to a narrower rump, with a visible shoulder
            var torso = new GameObject("Torso");
            torso.transform.SetParent(root, false);
            torso.transform.localPosition = new Vector3(0, 0.88f, 0.02f);
            torso.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.3f, 2, 0.04f, GetInstanceID(), new Vector3(0.72f, 0.9f, 1.6f));
            torso.AddComponent<MeshRenderer>().sharedMaterial = hide;

            var shoulder = new GameObject("Shoulder");
            shoulder.transform.SetParent(root, false);
            shoulder.transform.localPosition = new Vector3(0, 0.95f, 0.28f);
            shoulder.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.24f, 1, 0.04f, GetInstanceID() + 40, new Vector3(0.85f, 0.9f, 0.95f));
            shoulder.AddComponent<MeshRenderer>().sharedMaterial = hide;

            var rump = new GameObject("Rump");
            rump.transform.SetParent(root, false);
            rump.transform.localPosition = new Vector3(0, 0.93f, -0.3f);
            rump.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.23f, 1, 0.04f, GetInstanceID() + 41, new Vector3(0.85f, 0.95f, 0.9f));
            rump.AddComponent<MeshRenderer>().sharedMaterial = hide;

            var belly = new GameObject("Belly");
            belly.transform.SetParent(root, false);
            belly.transform.localPosition = new Vector3(0, 0.75f, 0.02f);
            belly.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.21f, 1, 0.04f, GetInstanceID() + 42, new Vector3(0.7f, 0.5f, 1.5f));
            belly.AddComponent<MeshRenderer>().sharedMaterial = pale;

            // white scut tail
            var tail = new GameObject("Tail");
            tail.transform.SetParent(root, false);
            tail.transform.localPosition = new Vector3(0, 1.0f, -0.46f);
            tail.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.06f, 1, 0.06f, GetInstanceID() + 43, new Vector3(0.8f, 1.3f, 0.6f));
            tail.AddComponent<MeshRenderer>().sharedMaterial = pale;

            // legs: thigh + slender cannon bone + dark hoof, angled like a deer's
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                float side = i % 2 == 0 ? -1f : 1f;
                float zPos = front ? 0.3f : -0.32f;

                var thigh = new GameObject("Thigh" + i);
                thigh.transform.SetParent(root, false);
                thigh.transform.localPosition = new Vector3(side * 0.13f, 0.62f, zPos);
                thigh.AddComponent<MeshFilter>().sharedMesh =
                    NatureFactory.SmoothBlob(0.085f, 1, 0.04f, GetInstanceID() + 50 + i, new Vector3(0.6f, 1.5f, 0.85f));
                thigh.AddComponent<MeshRenderer>().sharedMaterial = hide;

                var cannon = new GameObject("Cannon" + i);
                cannon.transform.SetParent(root, false);
                cannon.transform.localPosition = new Vector3(side * 0.13f, 0.46f, zPos + (front ? -0.02f : 0.03f));
                cannon.transform.localRotation = Quaternion.Euler(180f, 0, 0);
                cannon.AddComponent<MeshFilter>().sharedMesh =
                    NatureFactory.SmoothTube(0.028f, 0.02f, 0.42f, 5, 1, 0.01f, GetInstanceID() + i, 1f);
                cannon.AddComponent<MeshRenderer>().sharedMaterial = hide;

                var hoof = new GameObject("Hoof" + i);
                hoof.transform.SetParent(root, false);
                hoof.transform.localPosition = new Vector3(side * 0.13f, 0.035f, zPos + (front ? -0.02f : 0.03f));
                hoof.AddComponent<MeshFilter>().sharedMesh =
                    NatureFactory.SmoothBlob(0.032f, 1, 0.03f, GetInstanceID() + 60 + i, new Vector3(0.8f, 1f, 1.1f));
                hoof.AddComponent<MeshRenderer>().sharedMaterial = dark;
            }

            // Neck rig: the pivot sits at the shoulder and carries a neck that
            // already leans forward, with the head at its tip. Rotating the
            // pivot alone swings the whole head cleanly between grazing and
            // alert — the previous rig rotated the mesh about its own base and
            // drove the head through the body into the ground.
            _neck = new GameObject("NeckPivot").transform;
            _neck.SetParent(root, false);
            _neck.localPosition = new Vector3(0, 1.02f, 0.34f);

            var neckMesh = new GameObject("NeckMesh");
            neckMesh.transform.SetParent(_neck, false);
            neckMesh.transform.localRotation = Quaternion.Euler(22f, 0, 0); // leans forward from the shoulder
            neckMesh.AddComponent<MeshFilter>().sharedMesh =
                NatureFactory.SmoothTube(0.085f, 0.062f, 0.46f, 6, 2, 0.02f, GetInstanceID() + 9, 1f);
            neckMesh.AddComponent<MeshRenderer>().sharedMaterial = hide;

            // head rides at the tip of that lean
            var head = new GameObject("Head");
            head.transform.SetParent(_neck, false);
            head.transform.localPosition = new Vector3(0, 0.43f, 0.19f);
            head.transform.localRotation = Quaternion.Euler(18f, 0, 0);
            head.AddComponent<MeshFilter>().sharedMesh =
                NatureFactory.SmoothBlob(0.105f, 1, 0.04f, GetInstanceID() + 10, new Vector3(0.72f, 0.75f, 1.4f));
            head.AddComponent<MeshRenderer>().sharedMaterial = hide;

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(head.transform, false);
            muzzle.transform.localPosition = new Vector3(0, -0.03f, 0.11f);
            muzzle.AddComponent<MeshFilter>().sharedMesh =
                NatureFactory.SmoothBlob(0.055f, 1, 0.04f, GetInstanceID() + 44, new Vector3(0.8f, 0.7f, 1.2f));
            muzzle.AddComponent<MeshRenderer>().sharedMaterial = dark;

            for (int e = 0; e < 2; e++)
                Blob(head.transform, "Eye", 0.014f, new Color(0.06f, 0.05f, 0.04f),
                    new Vector3((e == 0 ? -1f : 1f) * 0.072f, 0.028f, 0.03f), GetInstanceID() + 45 + e);

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

        /// <summary>Head down to the grass, or up and watchful.</summary>
        void PoseNeck(bool grazing)
        {
            if (_neck == null) return;
            var target = grazing ? Quaternion.Euler(58f, 0, 0) : Quaternion.Euler(-12f, 0, 0);
            _neck.localRotation = Quaternion.Slerp(_neck.localRotation, target, Time.deltaTime * 3.5f);
        }
    }
}

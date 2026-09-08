using UnityEngine;

namespace Forage
{
    /// <summary>
    /// The most important lesson in the forest: a bear roams the far woods.
    /// If it notices you it stands tall and growls — BACK AWAY SLOWLY.
    /// Run, and it charges. A burning campfire keeps it away entirely.
    /// </summary>
    public class Bear : Animal
    {
        public enum State { Roam, Stand, Charge, Leave }

        [Header("State (read-only)")]
        public State state = State.Roam;

        Transform _torso;
        float _stateTimer;
        float _retreatTimer;
        float _lastDist;
        bool _charged;

        void Start()
        {
            BuildBody();
            agent.speed = 1.0f;
            agent.angularSpeed = 200f;
            agent.radius = 0.55f;
            agent.height = 1.6f;
            noticeDistance = 13f;
            scarySpeed = 1.5f;
            agent.SetDestination(RandomPoint(transform.position, 18f));
        }

        void BuildBody()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var fur = new Material(lit); fur.SetColor("_BaseColor", new Color(0.28f, 0.2f, 0.14f));
            var muzzleMat = new Material(lit); muzzleMat.SetColor("_BaseColor", new Color(0.5f, 0.38f, 0.26f));

            var root = new GameObject("Body").transform;
            root.SetParent(transform, false);
            SetBody(root);

            _torso = new GameObject("Torso").transform;
            _torso.SetParent(root, false);
            _torso.localPosition = new Vector3(0, 0.85f, 0);
            var torsoMesh = new GameObject("TorsoMesh");
            torsoMesh.transform.SetParent(_torso, false);
            torsoMesh.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.55f, 1, 0.06f, GetInstanceID(), new Vector3(0.85f, 0.85f, 1.35f));
            torsoMesh.AddComponent<MeshRenderer>().sharedMaterial = fur;

            var head = new GameObject("Head");
            head.transform.SetParent(_torso, false);
            head.transform.localPosition = new Vector3(0, 0.42f, 0.62f);
            head.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.24f, 1, 0.05f, GetInstanceID() + 1, new Vector3(0.9f, 0.85f, 1.05f));
            head.AddComponent<MeshRenderer>().sharedMaterial = fur;

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(head.transform, false);
            muzzle.transform.localPosition = new Vector3(0, -0.05f, 0.2f);
            muzzle.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.1f, 1, 0.04f, GetInstanceID() + 2, new Vector3(0.85f, 0.7f, 1.2f));
            muzzle.AddComponent<MeshRenderer>().sharedMaterial = muzzleMat;

            for (int i = 0; i < 2; i++)
            {
                var ear = new GameObject("Ear");
                ear.transform.SetParent(head.transform, false);
                ear.transform.localPosition = new Vector3(i == 0 ? -0.15f : 0.15f, 0.2f, -0.02f);
                ear.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.07f, 1, 0.04f, GetInstanceID() + 3 + i, new Vector3(1f, 1f, 0.5f));
                ear.AddComponent<MeshRenderer>().sharedMaterial = fur;
            }

            // the shoulder hump — the single most recognisable bear feature
            var hump = new GameObject("Hump");
            hump.transform.SetParent(_torso, false);
            hump.transform.localPosition = new Vector3(0, 0.36f, 0.2f);
            hump.AddComponent<MeshFilter>().sharedMesh =
                NatureFactory.SmoothBlob(0.3f, 1, 0.05f, GetInstanceID() + 70, new Vector3(0.85f, 0.7f, 0.9f));
            hump.AddComponent<MeshRenderer>().sharedMaterial = fur;

            var rump = new GameObject("Rump");
            rump.transform.SetParent(_torso, false);
            rump.transform.localPosition = new Vector3(0, 0.02f, -0.5f);
            rump.AddComponent<MeshFilter>().sharedMesh =
                NatureFactory.SmoothBlob(0.42f, 1, 0.05f, GetInstanceID() + 71, new Vector3(0.9f, 0.9f, 0.8f));
            rump.AddComponent<MeshRenderer>().sharedMaterial = fur;

            // heavy limbs: thick upper leg, shorter lower leg, broad flat paw
            var clawMat = new Material(lit); clawMat.SetColor("_BaseColor", new Color(0.12f, 0.1f, 0.09f));
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                float side = i % 2 == 0 ? -1f : 1f;
                float zPos = front ? 0.4f : -0.42f;

                var upper = new GameObject("UpperLeg" + i);
                upper.transform.SetParent(root, false);
                upper.transform.localPosition = new Vector3(side * 0.28f, 0.62f, zPos);
                upper.AddComponent<MeshFilter>().sharedMesh =
                    NatureFactory.SmoothBlob(0.19f, 1, 0.05f, GetInstanceID() + 80 + i, new Vector3(0.75f, 1.25f, 0.9f));
                upper.AddComponent<MeshRenderer>().sharedMaterial = fur;

                var lower = new GameObject("LowerLeg" + i);
                lower.transform.SetParent(root, false);
                lower.transform.localPosition = new Vector3(side * 0.28f, 0.3f, zPos);
                lower.transform.localRotation = Quaternion.Euler(180f, 0, 0);
                lower.AddComponent<MeshFilter>().sharedMesh =
                    NatureFactory.SmoothTube(0.115f, 0.1f, 0.28f, 6, 1, 0.01f, GetInstanceID() + 5 + i, 1f);
                lower.AddComponent<MeshRenderer>().sharedMaterial = fur;

                var paw = new GameObject("Paw" + i);
                paw.transform.SetParent(root, false);
                paw.transform.localPosition = new Vector3(side * 0.28f, 0.06f, zPos + 0.05f);
                paw.AddComponent<MeshFilter>().sharedMesh =
                    NatureFactory.SmoothBlob(0.13f, 1, 0.04f, GetInstanceID() + 90 + i, new Vector3(0.9f, 0.5f, 1.25f));
                paw.AddComponent<MeshRenderer>().sharedMaterial = fur;

                for (int c = 0; c < 3; c++)
                {
                    var claw = new GameObject("Claw");
                    claw.transform.SetParent(paw.transform, false);
                    claw.transform.localPosition = new Vector3((c - 1) * 0.055f, -0.01f, 0.14f);
                    claw.transform.localRotation = Quaternion.Euler(75f, 0, 0);
                    claw.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(0.016f, 0.06f, 4);
                    claw.AddComponent<MeshRenderer>().sharedMaterial = clawMat;
                }
            }
        }

        void Update()
        {
            _stateTimer += Time.deltaTime;
            float dist = PlayerDistance;

            // a burning fire keeps the bear away from camp
            var fire = FirePit.Instance;
            bool nearLitFire = fire != null && fire.IsLit &&
                Vector3.Distance(transform.position, fire.transform.position) < 10f;

            switch (state)
            {
                case State.Roam:
                    PoseStand(false);
                    if (agent.enabled && agent.remainingDistance < 1.2f)
                        agent.SetDestination(RandomPoint(transform.position, 18f));
                    if (nearLitFire) { Set(State.Leave); break; }
                    if (dist < noticeDistance) BeginStand();
                    break;

                case State.Stand:
                    agent.SetDestination(transform.position);
                    FacePlayer();
                    PoseStand(true);

                    if (nearLitFire || dist > noticeDistance + 6f) { Set(State.Leave); break; }

                    if (PlayerSpeed > scarySpeed && dist < 11f && !_charged)
                    {
                        Set(State.Charge);
                        break;
                    }

                    // backing away slowly = the correct response
                    if (dist > _lastDist + 0.005f && PlayerSpeed < 1.2f)
                        _retreatTimer += Time.deltaTime;
                    else
                        _retreatTimer = Mathf.Max(0f, _retreatTimer - Time.deltaTime * 0.5f);

                    if (_retreatTimer > 4f)
                    {
                        GameManager.Instance.CompleteObjective("wildlife");
                        FactCard.Show("You backed away — exactly right.",
                            "Face the bear, look big, and back away slowly. NEVER run: running triggers a chase " +
                            "no human can win. A burning campfire also keeps bears away.", good: true);
                        Set(State.Leave);
                    }
                    _lastDist = dist;
                    break;

                case State.Charge:
                    PoseStand(false);
                    var gm = GameManager.Instance;
                    agent.speed = 6.5f;
                    agent.SetDestination(gm.PlayerPosition);
                    if (!_charged && dist < 1.6f)
                    {
                        _charged = true;
                        gm.vitals.Damage(20f, "bear-charge");
                        Haptics.Pulse(1f, 0.6f);
                        FactCard.Show("The bear charged!",
                            "You ran — and a bear can outrun any human. The right response: freeze, look big, " +
                            "back away slowly. Keep a fire burning to keep bears out of camp.", good: false);
                        Set(State.Leave);
                    }
                    if (_stateTimer > 4f) Set(State.Leave);
                    break;

                case State.Leave:
                    PoseStand(false);
                    if (_stateTimer < 0.05f || (agent.enabled && agent.remainingDistance < 1.5f))
                    {
                        Vector3 away = GameManager.Instance != null
                            ? (transform.position - GameManager.Instance.PlayerPosition).normalized
                            : Random.onUnitSphere;
                        away.y = 0;
                        agent.speed = 2.6f;
                        agent.SetDestination(RandomPoint(transform.position + away * 25f, 8f));
                    }
                    if (_stateTimer > 14f)
                    {
                        _charged = false;
                        agent.speed = 1.0f;
                        Set(State.Roam);
                    }
                    break;
            }

            AnimateGait(0.05f, 7f);
        }

        void BeginStand()
        {
            Set(State.Stand);
            _retreatTimer = 0f;
            _lastDist = PlayerDistance;
            ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Growl(), 1f);
            Haptics.Pulse(0.7f, 0.5f);
            ForageEvents.RaiseHint("bear-dont-run");
        }

        public void ForceState(State s)
        {
            if (s == State.Stand) BeginStand();
            else Set(s);
        }

        void Set(State s)
        {
            state = s;
            _stateTimer = 0f;
        }

        void PoseStand(bool up)
        {
            if (_torso == null) return;
            var pos = up ? new Vector3(0, 1.25f, -0.15f) : new Vector3(0, 0.85f, 0);
            var rot = up ? Quaternion.Euler(-55f, 0, 0) : Quaternion.identity;
            _torso.localPosition = Vector3.Lerp(_torso.localPosition, pos, Time.deltaTime * 4f);
            _torso.localRotation = Quaternion.Slerp(_torso.localRotation, rot, Time.deltaTime * 4f);
        }
    }
}

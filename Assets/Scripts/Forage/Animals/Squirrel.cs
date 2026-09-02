using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Ambient squirrel (Furry Squirrel model): wanders the forest floor,
    /// then scampers up a nearby tree trunk, pauses, and comes back down.
    /// </summary>
    public class Squirrel : Animal
    {
        public enum State { Wander, ToTree, ClimbUp, TreeIdle, ClimbDown }

        [Header("State (read-only)")]
        public State state = State.Wander;

        Animator _animator;
        string _speedParam;
        Vector3 _treeBase;
        float _climbHeight;
        float _climbT;
        float _stateTimer;
        float _nextTreeTime;

        public void SetVisual(GameObject model, RuntimeAnimatorController controller, float scale,
            Texture2D albedo = null, Texture2D normal = null)
        {
            model.transform.SetParent(transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale = Vector3.one * scale;
            SetBody(model.transform);

            // the asset's own material uses a fur shader we exclude on Quest —
            // rebuild a plain URP Lit material from its textures
            if (albedo != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetTexture("_BaseMap", albedo);
                mat.SetFloat("_Smoothness", 0.15f);
                if (normal != null)
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                }
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                }
            }

            _animator = model.GetComponentInChildren<Animator>();
            if (_animator == null) _animator = model.AddComponent<Animator>();
            if (controller != null) _animator.runtimeAnimatorController = controller;

            // find any float parameter to drive as movement speed
            foreach (var p in _animator.parameters)
                if (p.type == AnimatorControllerParameterType.Float) { _speedParam = p.name; break; }
        }

        void Start()
        {
            agent.speed = 2.2f;
            agent.angularSpeed = 720f;
            agent.acceleration = 16f;
            agent.radius = 0.15f;
            agent.height = 0.3f;
            _nextTreeTime = Time.time + Random.Range(8f, 20f);
            agent.SetDestination(RandomPoint(transform.position, 10f));
        }

        void Update()
        {
            _stateTimer += Time.deltaTime;

            switch (state)
            {
                case State.Wander:
                    if (agent.enabled && agent.remainingDistance < 0.6f)
                        agent.SetDestination(RandomPoint(transform.position, 10f));
                    if (Time.time > _nextTreeTime && AnimalManager.Instance != null &&
                        AnimalManager.Instance.TryGetNearbyTree(transform.position, 14f, out _treeBase, out _climbHeight))
                    {
                        agent.SetDestination(_treeBase);
                        Set(State.ToTree);
                    }
                    break;

                case State.ToTree:
                    if (agent.enabled && !agent.pathPending && agent.remainingDistance < 0.5f)
                    {
                        agent.enabled = false;
                        _climbT = 0f;
                        Set(State.ClimbUp);
                    }
                    if (_stateTimer > 15f) { Set(State.Wander); _nextTreeTime = Time.time + 15f; }
                    break;

                case State.ClimbUp:
                    _climbT = Mathf.MoveTowards(_climbT, 1f, Time.deltaTime / 1.8f);
                    ApplyClimb(_climbT, facingUp: true);
                    if (_climbT >= 1f) Set(State.TreeIdle);
                    break;

                case State.TreeIdle:
                    if (_stateTimer > Random.Range(4f, 8f)) Set(State.ClimbDown);
                    break;

                case State.ClimbDown:
                    _climbT = Mathf.MoveTowards(_climbT, 0f, Time.deltaTime / 1.8f);
                    ApplyClimb(_climbT, facingUp: false);
                    if (_climbT <= 0f)
                    {
                        agent.enabled = true;
                        if (agent.isOnNavMesh) agent.SetDestination(RandomPoint(transform.position, 10f));
                        _nextTreeTime = Time.time + Random.Range(15f, 35f);
                        Set(State.Wander);
                    }
                    break;
            }

            // drive the model's own animation by movement speed
            if (_animator != null && !string.IsNullOrEmpty(_speedParam))
            {
                float moveSpeed = state == State.ClimbUp || state == State.ClimbDown
                    ? 1.6f
                    : (agent.enabled ? agent.velocity.magnitude : 0f);
                _animator.SetFloat(_speedParam, moveSpeed);
            }
        }

        /// <summary>Procedural stand-in body if the FBX is missing.</summary>
        public void SetVisualFallback()
        {
            SetBody(AnimalFactory.RabbitBody(transform, GetInstanceID() + 7));
            body.localScale = Vector3.one * 0.6f;
        }

        /// <summary>Tester panel hook: climb the nearest tree immediately.</summary>
        public void ForceClimb()
        {
            if (AnimalManager.Instance != null &&
                AnimalManager.Instance.TryGetNearbyTree(transform.position, 25f, out _treeBase, out _climbHeight))
            {
                if (agent.enabled) agent.SetDestination(_treeBase);
                Set(State.ToTree);
            }
        }

        void ApplyClimb(float t, bool facingUp)
        {
            Vector3 pos = _treeBase + Vector3.up * (t * _climbHeight);
            transform.position = pos;
            // hug the trunk, nose up or down
            Vector3 toTrunk = (_treeBase - transform.position);
            toTrunk.y = 0;
            var yaw = toTrunk.sqrMagnitude > 0.001f ? Quaternion.LookRotation(toTrunk.normalized) : transform.rotation;
            transform.rotation = yaw * Quaternion.Euler(facingUp ? -70f : 70f, 0, 0);
        }

        void Set(State s)
        {
            state = s;
            _stateTimer = 0f;
        }
    }
}

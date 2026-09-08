using UnityEngine;

namespace Forage
{
    /// <summary>Species definition for a snake encounter.</summary>
    [System.Serializable]
    public class SnakeSpecies
    {
        public string name;
        public bool venomous;
        public bool aggressive;   // aggressive = freeze-or-strike; shy = flees from you
        public float scale;       // body size multiplier
        public float damage;
        public Color baseColor;
        public Color bandColor;
        public string lesson;

        public static readonly SnakeSpecies[] All =
        {
            new SnakeSpecies
            {
                name = "Eastern Brown Snake", venomous = true, aggressive = true, scale = 1.35f, damage = 16f,
                baseColor = new Color(0.45f, 0.32f, 0.2f), bandColor = new Color(0.3f, 0.2f, 0.12f),
                lesson = "One of the most venomous snakes in the world. It strikes at fast movement — freeze, then back away slowly."
            },
            new SnakeSpecies
            {
                name = "Carpet Python", venomous = false, aggressive = true, scale = 1.7f, damage = 6f,
                baseColor = new Color(0.35f, 0.3f, 0.18f), bandColor = new Color(0.65f, 0.55f, 0.3f),
                lesson = "Pythons are NOT venomous, but a defensive bite still hurts. Big snake = big personal space."
            },
            new SnakeSpecies
            {
                name = "Green Tree Snake", venomous = false, aggressive = false, scale = 0.7f, damage = 0f,
                baseColor = new Color(0.2f, 0.5f, 0.25f), bandColor = new Color(0.35f, 0.65f, 0.3f),
                lesson = "Most snakes are harmless and shy — this one just wants to get away from you. Let it."
            },
        };
    }

    /// <summary>
    /// A snake encounter. Aggressive species rear up and hiss when you get
    /// close — freeze or get struck. Shy species simply flee. Venomous bites
    /// also poison you.
    /// </summary>
    public class Snake : Animal
    {
        public enum State { Patrol, Alert, Strike, Flee, Leave }

        [Header("State (read-only)")]
        public State state = State.Patrol;
        public string speciesName;
        public bool venomous;

        SnakeSpecies _species;
        Transform[] _segments;
        Transform _tongue;
        float _stateTimer;
        float _movingWhileAlert;
        float _stillWhileAlert;
        Vector3 _territory;
        bool _struck;
        static bool _shyCardShown;

        public void Configure(SnakeSpecies species)
        {
            _species = species;
            speciesName = species.name;
            venomous = species.venomous;
            var skin = AnimalFactory.SnakeSkin(species.baseColor, species.bandColor, GetInstanceID());
            _segments = AnimalFactory.SnakeBody(transform, GetInstanceID(), species.scale, skin);
            var t = _segments[0].Find("HeadMesh/Tongue");
            _tongue = t;
        }

        void Start()
        {
            if (_species == null) Configure(SnakeSpecies.All[0]);
            _territory = transform.position;
            agent.speed = 0.8f;
            agent.angularSpeed = 260f;
            agent.radius = 0.15f * _species.scale;
            agent.height = 0.3f;
            noticeDistance = 2.6f + _species.scale * 0.8f;
            scarySpeed = 1.1f;
        }

        void Update()
        {
            _stateTimer += Time.deltaTime;
            float dist = PlayerDistance;

            switch (state)
            {
                case State.Patrol:
                    if (agent.enabled && agent.remainingDistance < 0.4f)
                        agent.SetDestination(RandomPoint(_territory, 4f));
                    if (dist < noticeDistance)
                    {
                        if (_species.aggressive) BeginAlert();
                        else BeginShyFlee();
                    }
                    break;

                case State.Alert:
                    agent.SetDestination(transform.position);
                    FacePlayer();
                    RearHead(true);

                    if (dist > noticeDistance + 2.5f) { RearHead(false); Set(State.Patrol); break; }

                    if (PlayerSpeed > scarySpeed)
                    {
                        _movingWhileAlert += Time.deltaTime;
                        _stillWhileAlert = 0f;
                        if (_movingWhileAlert > 0.9f) Set(State.Strike);
                    }
                    else
                    {
                        _stillWhileAlert += Time.deltaTime;
                        if (_stillWhileAlert > 4.5f)
                        {
                            RearHead(false);
                            GameManager.Instance.CompleteObjective("wildlife");
                            FactCard.Show("You stayed still — well done!",
                                _species.name + ": " + _species.lesson + " Because you froze, it lost interest.",
                                good: true);
                            Set(State.Leave);
                        }
                    }
                    break;

                case State.Strike:
                    var gm = GameManager.Instance;
                    agent.speed = 4.5f + _species.scale;
                    agent.SetDestination(gm.PlayerPosition);
                    if (!_struck && dist < 0.9f + _species.scale * 0.4f)
                    {
                        _struck = true;
                        gm.vitals.Damage(_species.damage, "snake-strike");
                        if (venomous) gm.vitals.MakeSick(75f);
                        ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Hiss(), 1f);
                        Haptics.Pulse(1f, 0.4f);
                        FactCard.Show(_species.name + " bite!" + (venomous ? " (VENOMOUS)" : ""),
                            "It warned you, but you kept moving. " + _species.lesson, good: false);
                        Set(State.Leave);
                    }
                    if (_stateTimer > 3f) Set(State.Leave);
                    break;

                case State.Flee: // shy species
                case State.Leave:
                    RearHead(false);
                    if (_stateTimer < 0.05f || (agent.enabled && agent.remainingDistance < 0.6f))
                    {
                        agent.speed = 2f + _species.scale;
                        Vector3 away = GameManager.Instance != null
                            ? (transform.position - GameManager.Instance.PlayerPosition).normalized
                            : Random.onUnitSphere;
                        away.y = 0;
                        agent.SetDestination(RandomPoint(transform.position + away * 14f, 4f));
                    }
                    if (_stateTimer > 10f)
                    {
                        _territory = transform.position;
                        _struck = false;
                        agent.speed = 0.8f;
                        Set(State.Patrol);
                    }
                    break;
            }

            Slither();
            FlickTongue();
        }

        void BeginAlert()
        {
            Set(State.Alert);
            _movingWhileAlert = 0f;
            _stillWhileAlert = 0f;
            ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Hiss(), 0.9f);
            Haptics.Pulse(0.5f, 0.35f);
            ForageEvents.RaiseHint("snake-freeze");
        }

        void BeginShyFlee()
        {
            Set(State.Flee);
            if (!_shyCardShown)
            {
                _shyCardShown = true;
                FactCard.Show(_species.name,
                    _species.lesson, good: true);
            }
        }

        public void ForceState(State s)
        {
            if (s == State.Alert) BeginAlert();
            else Set(s);
        }

        void Set(State s)
        {
            state = s;
            _stateTimer = 0f;
        }

        void RearHead(bool up)
        {
            if (_segments == null || _segments.Length == 0) return;
            var head = _segments[0];
            float s = _species.scale;
            var target = up ? new Vector3(0, 0.2f * s, 0.06f * s) : new Vector3(0, 0.05f * s, 0);
            head.localPosition = Vector3.Lerp(head.localPosition, target, Time.deltaTime * 6f);
        }

        void FlickTongue()
        {
            if (_tongue == null) return;
            // quick flicks every couple of seconds, faster while alert
            float rate = state == State.Alert || state == State.Strike ? 2.5f : 0.8f;
            bool outNow = Mathf.PingPong(Time.time * rate + GetInstanceID() * 0.13f, 1f) > 0.75f;
            _tongue.gameObject.SetActive(outNow);
        }

        void Slither()
        {
            if (_segments == null) return;
            float s = _species.scale;
            float spacing = AnimalFactory.SegmentSpacing(s);
            float groundY = transform.position.y + 0.04f * s;
            float speed01 = agent.enabled ? Mathf.Clamp01(agent.velocity.magnitude / 2f) : 0f;
            for (int i = 1; i < _segments.Length; i++)
            {
                var prev = _segments[i - 1];

                // distance-constrained follow chain: stay exactly one spacing behind
                Vector3 toPrev = prev.position - _segments[i].position;
                if (toPrev.sqrMagnitude < 1e-6f) toPrev = -transform.forward;
                float gap = i == 1 ? spacing * 0.55f : spacing; // neck hugs the head
                Vector3 target = prev.position - toPrev.normalized * gap;

                float sway = Mathf.Sin(Time.time * 7f - i * 0.9f) * 0.03f * s * (0.3f + speed01);
                target += transform.right * sway;

                // body settles to the ground; only the neck rides the reared head
                float neck = Mathf.Clamp01(1f - (i - 1) / 4f);
                target.y = Mathf.Lerp(groundY, Mathf.Max(target.y, groundY), neck);

                _segments[i].position = Vector3.Lerp(_segments[i].position, target, Time.deltaTime * 22f);
                _segments[i].LookAt(prev.position);
            }
        }
    }
}

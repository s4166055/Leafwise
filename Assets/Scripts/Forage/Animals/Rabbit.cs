using UnityEngine;

namespace Forage
{
    /// <summary>
    /// The friendly teacher: approach slowly and the rabbit trusts you and may
    /// lead you to a small gift (dry wood). Move fast and it bolts.
    /// Lesson: move calmly around wildlife.
    /// </summary>
    public class Rabbit : Animal
    {
        public enum State { Wander, Watch, Approach, Gift, Flee }

        [Header("State (read-only)")]
        public State state = State.Wander;

        float _stateTimer;
        float _calmTimer;
        float _giftCooldown;

        void Start()
        {
            SetBody(AnimalFactory.RabbitBody(transform, GetInstanceID()));
            agent.speed = 1.4f;
            agent.angularSpeed = 540f;
            agent.acceleration = 12f;
            agent.radius = 0.2f;
            agent.height = 0.5f;
            agent.SetDestination(RandomPoint(transform.position, 8f));
        }

        void Update()
        {
            _stateTimer += Time.deltaTime;
            _giftCooldown -= Time.deltaTime;
            float dist = PlayerDistance;

            switch (state)
            {
                case State.Wander:
                    if (agent.enabled && agent.remainingDistance < 0.6f)
                        agent.SetDestination(RandomPoint(transform.position, 8f));
                    if (dist < noticeDistance) Set(State.Watch);
                    break;

                case State.Watch:
                    agent.SetDestination(transform.position); // hold still
                    FacePlayer();
                    if (dist > noticeDistance + 3f) { Set(State.Wander); break; }
                    if (PlayerSpeed > scarySpeed) { Scare(); break; }

                    // player staying calm and close builds trust
                    _calmTimer += (dist < 5f && PlayerSpeed < 0.7f) ? Time.deltaTime : -Time.deltaTime * 0.5f;
                    _calmTimer = Mathf.Max(0, _calmTimer);
                    if (_calmTimer > 3f && _giftCooldown <= 0f) Set(State.Approach);
                    break;

                case State.Approach:
                    if (PlayerSpeed > scarySpeed) { Scare(); break; }
                    var gm = GameManager.Instance;
                    agent.SetDestination(gm.PlayerPosition);
                    if (dist < 1.6f) Set(State.Gift);
                    if (_stateTimer > 12f) Set(State.Wander);
                    break;

                case State.Gift:
                    DropGift();
                    Set(State.Flee); // hop away shyly after gifting
                    break;

                case State.Flee:
                    if (_stateTimer < 0.05f || (agent.enabled && agent.remainingDistance < 0.8f))
                    {
                        var gmf = GameManager.Instance;
                        Vector3 away = (transform.position - gmf.PlayerPosition).normalized;
                        agent.speed = 3.6f;
                        agent.SetDestination(RandomPoint(transform.position + away * 12f, 4f));
                    }
                    if (_stateTimer > 5f) { agent.speed = 1.4f; Set(State.Wander); }
                    break;
            }

            AnimateGait(0.09f, 14f); // hoppy bob
        }

        public void Scare()
        {
            if (state != State.Flee) ForageEvents.RaiseHint("rabbit-scared");
            Set(State.Flee);
        }

        /// <summary>Tester panel hook.</summary>
        public void ForceState(State s) => Set(s);

        void Set(State s)
        {
            state = s;
            _stateTimer = 0f;
            if (s != State.Watch) _calmTimer = 0f;
        }

        void DropGift()
        {
            _giftCooldown = 90f;
            var stick = ItemFactory.Stick(GetInstanceID() + Time.frameCount, wet: false);
            stick.transform.position = transform.position + Vector3.up * 0.25f;
            ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Chirp(2), 0.5f);
            ForageEvents.RaiseSignal("rabbit-gift");
            GameManager.Instance.CompleteObjective("wildlife");
            FactCard.Show("The rabbit trusts you!",
                "Because you approached slowly and quietly, the rabbit felt safe — and led you to some dry wood. " +
                "In the wild, calm movement keeps animals relaxed and lets you observe them.", good: true);
        }
    }
}

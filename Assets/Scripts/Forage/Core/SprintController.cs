using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

namespace Forage
{
    /// <summary>
    /// Walk/run control: hold Left Shift (simulator) or click either thumbstick
    /// (Quest controllers) to sprint. Scales the XRI continuous move provider.
    /// Remember: running near wildlife scares it — and provokes the snake.
    /// </summary>
    public class SprintController : MonoBehaviour
    {
        // Raised again after headset testing. Note that the real limiter on
        // felt pace was never this number: the CharacterController's default
        // 45 deg slope limit and 0.3 m step offset made the player snag on
        // every bank and root, so actual travel was a fraction of the setting.
        // Those are widened in ForageSceneBuilder; these are the honest speeds.
        public float walkSpeed = 16.0f;
        // Kept under 30: GameManager rejects head-speed samples >= 30 m/s as
        // teleport jumps, so a sprint of exactly 30 would drop the very samples
        // that tell wildlife you are running.
        public float runSpeed = 26.0f;

        [Header("Debug/testing")]
        public bool testForceSprint;

        // Every continuous provider on the rig, not just the first one found.
        // DynamicMoveProvider derives from ContinuousMoveProvider, and a rig can
        // carry more than one; setting only one leaves the other at its prefab
        // default and the pace depends on which happens to drive you.
        ContinuousMoveProvider[] _movers = System.Array.Empty<ContinuousMoveProvider>();
        float _lastApplied = -1f;
        static readonly List<InputDevice> _devices = new List<InputDevice>();

        void Start() => Acquire();

        void Acquire()
        {
            _movers = FindObjectsByType<ContinuousMoveProvider>(FindObjectsSortMode.None);
            if (_movers.Length == 0) return;
            _lastApplied = -1f;   // force a write on the next Update
            Debug.Log($"[Forage] Sprint: {_movers.Length} move provider(s) set to " +
                      $"walk {walkSpeed} / run {runSpeed} m/s " +
                      $"({string.Join(", ", System.Array.ConvertAll(_movers, m => m.GetType().Name))})");
        }

        // A destroyed provider compares equal to null; if the rig is rebuilt at
        // runtime we must find the replacement rather than skip it forever.
        bool NeedsAcquire()
        {
            if (_movers.Length == 0) return true;
            foreach (var m in _movers) if (m == null) return true;
            return false;
        }

        void Update()
        {
            if (NeedsAcquire())
            {
                Acquire();
                if (_movers.Length == 0) return;
            }

            bool sprint = testForceSprint || ShiftHeld() ||
                          StickClicked(XRNode.LeftHand) || StickClicked(XRNode.RightHand);
            float speed = sprint ? runSpeed : walkSpeed;
            if (Mathf.Approximately(speed, _lastApplied)) return;   // sprint state changes rarely

            _lastApplied = speed;
            foreach (var m in _movers) m.moveSpeed = speed;
        }

        static bool ShiftHeld()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.leftShiftKey.isPressed;
        }

        static bool StickClicked(XRNode node)
        {
            _devices.Clear();
            InputDevices.GetDevicesAtXRNode(node, _devices);
            foreach (var d in _devices)
                if (d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool clicked) && clicked)
                    return true;
            return false;
        }
    }
}

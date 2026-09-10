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
        public float runSpeed = 30.0f;

        [Header("Debug/testing")]
        public bool testForceSprint;

        // Every continuous provider on the rig, not just the first one found.
        // DynamicMoveProvider derives from ContinuousMoveProvider, and a rig can
        // carry more than one; setting only one leaves the other at its prefab
        // default and the pace depends on which happens to drive you.
        ContinuousMoveProvider[] _movers;
        bool _logged;
        static readonly List<InputDevice> _devices = new List<InputDevice>();

        void Start() => Acquire();

        void Acquire()
        {
            _movers = FindObjectsByType<ContinuousMoveProvider>(FindObjectsSortMode.None);
            if (_movers.Length == 0) return;
            foreach (var m in _movers) m.moveSpeed = walkSpeed;

            if (!_logged)
            {
                _logged = true;
                Debug.Log($"[Forage] Sprint: {_movers.Length} move provider(s) set to " +
                          $"walk {walkSpeed} / run {runSpeed} m/s " +
                          $"({string.Join(", ", System.Array.ConvertAll(_movers, m => m.GetType().Name))})");
            }
        }

        void Update()
        {
            if (_movers == null || _movers.Length == 0)
            {
                Acquire();
                if (_movers == null || _movers.Length == 0) return;
            }

            bool sprint = testForceSprint || ShiftHeld() ||
                          StickClicked(XRNode.LeftHand) || StickClicked(XRNode.RightHand);
            float speed = sprint ? runSpeed : walkSpeed;
            foreach (var m in _movers)
                if (m != null) m.moveSpeed = speed;
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

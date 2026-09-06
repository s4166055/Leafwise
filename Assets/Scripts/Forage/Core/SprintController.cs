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
        // 3x the previous pace — the forest is crossable without it feeling like a trudge
        public float walkSpeed = 12.0f;
        public float runSpeed = 22.0f;

        [Header("Debug/testing")]
        public bool testForceSprint;

        ContinuousMoveProvider _move;
        static readonly List<InputDevice> _devices = new List<InputDevice>();

        void Start()
        {
            _move = FindFirstObjectByType<ContinuousMoveProvider>();
            if (_move != null) _move.moveSpeed = walkSpeed;
        }

        void Update()
        {
            if (_move == null)
            {
                _move = FindFirstObjectByType<ContinuousMoveProvider>();
                if (_move == null) return;
            }

            bool sprint = testForceSprint || ShiftHeld() || StickClicked(XRNode.LeftHand) || StickClicked(XRNode.RightHand);
            _move.moveSpeed = sprint ? runSpeed : walkSpeed;
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

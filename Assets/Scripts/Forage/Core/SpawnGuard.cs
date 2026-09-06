using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Keeps the player on the map:
    /// 1. Head-ground clamp — head tracking has no collision, so when the
    ///    camera would sink into a hillside (simulator device-translate, or a
    ///    real player physically walking into a slope) the whole rig is raised
    ///    so the head glides over the terrain instead of clipping under it.
    /// 2. Fall catch — if the rig ends up far below the terrain it is reset
    ///    to the last safe standing position (camp at startup).
    /// </summary>
    public class SpawnGuard : MonoBehaviour
    {
        public Transform rig;
        public float fallMargin = 6f;
        /// <summary>Keep the head this far above the ground — adult eye height.</summary>
        public float headClearance = 1.5f;

        Transform _head;
        Vector3 _lastSafePos = new Vector3(0f, 1f, 0f);
        float _submergedSince = -1f;

        void LateUpdate()
        {
            var forest = ForestGenerator.Instance;
            if (rig == null || forest == null) return;

            if (_head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _head = cam.transform;
            }

            // 1) hard fall catch: rig far below the ground under it
            float rigGround = forest.HeightAt(rig.position.x, rig.position.z);
            if (rig.position.y < rigGround - fallMargin)
            {
                ResetTo(_lastSafePos);
                return;
            }

            // 2) head-ground clamp
            float headGround = forest.HeightAt(_head.position.x, _head.position.z);
            float sink = (headGround + headClearance) - _head.position.y;
            if (sink > 0f)
            {
                rig.position += Vector3.up * sink;

                if (_submergedSince < 0f) _submergedSince = Time.time;
                else if (Time.time - _submergedSince > 2f)
                    ResetTo(_lastSafePos); // still stuck after 2s -> bail out
            }
            else
            {
                _submergedSince = -1f;

                // remember a safe standing spot: head well above ground, rig resting near its own ground
                if (_head.position.y > headGround + 1.0f &&
                    Mathf.Abs(rig.position.y - rigGround) < 1.5f)
                {
                    _lastSafePos = new Vector3(rig.position.x, rigGround + 0.6f, rig.position.z);
                }
            }
        }

        void ResetTo(Vector3 pos)
        {
            var cc = rig.GetComponentInChildren<CharacterController>();
            if (cc != null) cc.enabled = false;
            rig.position = pos;
            if (cc != null) cc.enabled = true;
            _submergedSince = -1f;
            Debug.Log("[Forage] SpawnGuard: player left the map, reset to last safe spot.");
        }
    }
}

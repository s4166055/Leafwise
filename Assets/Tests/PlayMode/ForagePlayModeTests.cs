using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Forage.Testing;

namespace Forage.Tests
{
    /// <summary>
    /// Loads the real Forage scene, lets the world generate and the animals
    /// settle, then asserts every world invariant. The Test Framework drives
    /// play mode itself, so frames genuinely advance regardless of editor focus.
    ///
    /// The scene is heavy (~10k renderers, a runtime NavMesh bake), so it is
    /// loaded once and the report shared across the tests in this fixture.
    /// </summary>
    public class ForagePlayModeTests
    {
        const string SceneName = "Forage";
        const float SettleSeconds = 6f;

        static InvariantReport _report;
        static bool _loaded;

        [UnitySetUp]
        public IEnumerator LoadWorldOnce()
        {
            if (_loaded) yield break;

            var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, $"scene '{SceneName}' is not in Build Settings");
            yield return load;

            // Generation runs in Awake; the NavMesh bake, animal spawns, the
            // tracking-origin coroutine and Scout's first line all need frames.
            float until = Time.time + SettleSeconds;
            while (Time.time < until) yield return null;

            _report = WorldInvariants.Run();
            _loaded = true;
            Debug.Log(_report.ToText("Forage PlayMode invariants"));
        }

        [UnityTest]
        public IEnumerator World_AllInvariantsHold()
        {
            yield return null;
            // Report every failing invariant at once rather than stopping at the
            // first (this NUnit build has no Assert.Multiple).
            var failures = _report.Failures.Select(c => $"{c.Label}  [{c.Detail}]").ToList();
            Assert.That(failures, Is.Empty,
                $"{failures.Count} of {_report.Passed + _report.Failed} invariants failed:\n  " +
                string.Join("\n  ", failures));
        }

        [UnityTest]
        public IEnumerator Vr_RigIsRoomScale()
        {
            yield return null;
            var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            Assert.That(origin, Is.Not.Null);
            Assert.That(origin.RequestedTrackingOriginMode,
                Is.EqualTo(Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor));
        }

        [UnityTest]
        public IEnumerator Scout_OrbHidesWhenItStopsTalking()
        {
            // This is the behaviour the "white ball following me" fix actually
            // promises, tested as behaviour rather than as a snapshot: put Scout
            // into a known silent state, let one LateUpdate run, and the orb
            // must not be rendered.
            var scout = Object.FindFirstObjectByType<ScoutCompanion>();
            Assert.That(scout, Is.Not.Null);

            scout.Silence();
            yield return null;   // LateUpdate applies visibility
            yield return null;

            var visible = scout.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled).ToList();
            Assert.That(scout.IsSpeaking, Is.False, "Silence() should end speech");
            Assert.That(visible, Is.Empty,
                "orb renderer(s) still enabled while silent: " + string.Join(", ", visible.Select(v => v.name)));
        }

        [UnityTest]
        public IEnumerator Movement_SprintSpeedsAppliedToEveryProvider()
        {
            yield return null;
            var sprint = Object.FindFirstObjectByType<SprintController>();
            Assert.That(sprint, Is.Not.Null);

            var movers = Object.FindObjectsByType<
                UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.ContinuousMoveProvider>(FindObjectsSortMode.None);
            Assert.That(movers.Length, Is.GreaterThan(0), "no move provider on the rig");
            foreach (var m in movers)
                Assert.That(m.moveSpeed, Is.EqualTo(sprint.walkSpeed).Within(0.01f),
                    $"{m.GetType().Name} is at {m.moveSpeed} m/s, not the configured walk speed");
        }

        [UnityTest]
        public IEnumerator Animals_NoneBuriedOrFloating()
        {
            yield return null;
            var forest = Object.FindFirstObjectByType<ForestGenerator>();
            Assert.That(forest, Is.Not.Null);

            var offGround = Object.FindObjectsByType<Animal>(FindObjectsSortMode.None)
                .Select(a => (a, dy: a.transform.position.y - forest.HeightAt(a.transform.position.x, a.transform.position.z)))
                .Where(t => t.dy < -0.4f || t.dy > 1.6f)
                .Select(t => $"{t.a.GetType().Name} is {t.dy:F2} m from the ground")
                .ToList();
            Assert.That(offGround, Is.Empty, string.Join("\n", offGround));
        }
    }
}

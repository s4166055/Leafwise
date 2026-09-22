using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;
using Forage.EditorTools;

namespace Forage.Tests
{
    /// <summary>
    /// Fast checks that need no play mode: pure functions, generated
    /// materials, build configuration, and what is serialized in the scene.
    /// These run in a couple of seconds and are the first line of defence.
    /// </summary>
    public class ForageEditModeTests
    {
        const string ScenePath = "Assets/Scenes/Forage.unity";

        // ------------------------------------------------------------------
        // Terrain: pure static functions.
        // (Habitat zones need a live ForestGenerator for the density field, so
        // they are asserted in the PlayMode suite via WorldInvariants.)
        // ------------------------------------------------------------------

        [Test]
        public void Terrain_CampCentreSitsAtNaturalGroundLevel()
        {
            // The camp used to flatten toward y=0, sinking it ~3 m into a crater.
            // It must now flatten to the terrain's own height at the origin.
            const int seed = 20260902;
            float camp = ForestGenerator.HeightField(seed, 0f, 0f, 9f, new Vector2(24f, 16f), 8f, 1.4f);
            Assert.That(camp, Is.EqualTo(ForestGenerator.CampLevel(seed)).Within(0.05f));
        }

        [Test]
        public void Terrain_CampIsLevelWithSurroundingForest()
        {
            const int seed = 20260902;
            var pond = new Vector2(24f, 16f);
            float camp = ForestGenerator.HeightField(seed, 0f, 0f, 9f, pond, 8f, 1.4f);

            float ring = 0f;
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                ring += ForestGenerator.HeightField(seed, Mathf.Cos(a) * 20f, Mathf.Sin(a) * 20f, 9f, pond, 8f, 1.4f);
            }
            ring /= 24f;

            Assert.That(Mathf.Abs(camp - ring), Is.LessThan(2.5f),
                $"camp {camp:F2} m vs forest ring {ring:F2} m - that is a crater or a hill, not a clearing");
        }

        // ------------------------------------------------------------------
        // Generated materials
        // ------------------------------------------------------------------

        [Test]
        public void Fur_MaterialCarriesAlbedoAndNormalMap()
        {
            // The normal map is what makes procedural fur read as hair rather
            // than plasticine; a regression here is invisible to a compiler.
            var mat = AnimalFactory.FurMaterial("test-fur-" + System.Guid.NewGuid(),
                new Color(0.5f, 0.4f, 0.3f), new Color(0.3f, 0.23f, 0.16f), 1);

            Assert.That(mat.GetTexture("_BaseMap"), Is.Not.Null, "albedo missing");
            Assert.That(mat.GetTexture("_BumpMap"), Is.Not.Null, "normal map missing");
            Assert.That(mat.IsKeywordEnabled("_NORMALMAP"), Is.True, "normal map keyword not enabled");
            Assert.That(mat.GetFloat("_Smoothness"), Is.LessThan(0.4f), "fur should be matte, not plastic");
        }

        [Test]
        public void Fur_MaterialIsCachedByKey()
        {
            string key = "test-cache-" + System.Guid.NewGuid();
            var a = AnimalFactory.FurMaterial(key, Color.red, Color.black, 2);
            var b = AnimalFactory.FurMaterial(key, Color.red, Color.black, 2);
            Assert.That(b, Is.SameAs(a), "the same key must not bake a second texture set");
        }

        [Test]
        public void Eye_MaterialIsGlossy()
        {
            var eye = AnimalFactory.EyeMaterial(Color.black);
            Assert.That(eye.GetFloat("_Smoothness"), Is.GreaterThan(0.85f),
                "matte eyes are one of the strongest toy signals on a procedural animal");
        }

        // ------------------------------------------------------------------
        // Build configuration
        // ------------------------------------------------------------------

        [Test]
        public void QuestBuild_PreflightValidationPasses()
        {
            // Twelve checks: IL2CPP, ARM64, Vulkan, multi-view, min SDK, linear
            // colour, OpenXR loader, Meta Quest feature, controller profile, ...
            Assert.That(QuestBuild.Validate(), Is.True, "see the Console for the failing PASS/FAIL row");
        }

        [Test]
        public void QuestBuild_ForageIsTheOnlySceneInBuildSettings()
        {
            var enabled = System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled);
            Assert.That(enabled.Length, Is.EqualTo(1), "exactly one scene should ship");
            Assert.That(enabled[0].path, Is.EqualTo(ScenePath));
        }

        // ------------------------------------------------------------------
        // What is actually serialized in the scene
        // ------------------------------------------------------------------

        [Test]
        public void Scene_RigIsRoomScaleNotSeated()
        {
            // This is the exact defect the first headset test found: the scene
            // shipped with TrackingOriginMode.Device and a faked 1.7 m offset,
            // which made the game joystick-only. Assert on the saved asset.
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                XROrigin origin = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    origin = root.GetComponentInChildren<XROrigin>(true);
                    if (origin != null) break;
                }

                Assert.That(origin, Is.Not.Null, "no XROrigin in the scene");
                Assert.That(origin.RequestedTrackingOriginMode, Is.EqualTo(XROrigin.TrackingOriginMode.Floor),
                    "Device is the seated 3DOF origin; room-scale needs Floor");
                // XROrigin applies CameraYOffset only in Device/Unbounded mode
                // (including a mid-session drop from Floor) and zeroes the floor
                // offset itself in Floor mode - so this is the seated fallback and
                // must be a standing height, not zero.
                Assert.That(origin.CameraYOffset, Is.InRange(1.4f, 2.0f),
                    "seated fallback eye height missing: a drop to Device mode would put the eyes on the floor");
                Assert.That(origin.GetComponent<VrTrackingSetup>(), Is.Not.Null,
                    "VrTrackingSetup must verify what the runtime actually granted");

                var cc = origin.GetComponentInChildren<CharacterController>(true);
                Assert.That(cc, Is.Not.Null, "no CharacterController on the rig");
                Assert.That(cc.slopeLimit, Is.GreaterThanOrEqualTo(55f),
                    "the 45 deg default snags on every bank and reads as slow movement");
                Assert.That(cc.stepOffset, Is.GreaterThanOrEqualTo(0.5f),
                    "the 0.3 m default snags on roots and rocks");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void Scene_SimulatorNeverShipsToDevice()
        {
            // The XR Interaction Simulator takes over the HMD pose. If it ever
            // instantiated on a headset the view would stop following the head.
            var settings = AssetDatabase.LoadMainAssetAtPath("Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset");
            Assert.That(settings, Is.Not.Null, "simulator settings asset missing");
            var so = new SerializedObject(settings);
            Assert.That(so.FindProperty("m_AutomaticallyInstantiateInEditorOnly").boolValue, Is.True,
                "the simulator must be editor-only");

            // XRI's loader spawns the simulator on every editor Play with this on,
            // even with a real Quest connected over Link - and the camera then
            // follows the simulated HMD instead of the player's head. That was
            // the first headset test. SimulatorGuard spawns it only when no
            // headset is active, so the package flag must stay off.
            Assert.That(so.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue, Is.False,
                "XRI must not auto-spawn the simulator; SimulatorGuard decides at runtime");
            Assert.That(so.FindProperty("m_SimulatorPrefab").objectReferenceValue, Is.Not.Null,
                "the simulator prefab must stay assigned so SimulatorGuard can spawn it for desk testing");
        }
    }
}

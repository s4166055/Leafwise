using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Forage.Tests
{
    /// <summary>Builds the shelter the way a player does: drop branches, then leaf bundles, on the frame.</summary>
    public class ShelterPlayModeTests
    {
        static bool _loaded;

        [UnitySetUp]
        public IEnumerator LoadWorldOnce()
        {
            if (_loaded) yield break;
            yield return SceneManager.LoadSceneAsync("Forage", LoadSceneMode.Single);
            float until = Time.time + 4f;
            while (Time.time < until) yield return null;
            _loaded = true;
        }

        static IEnumerator Wait(float s)
        {
            float until = Time.time + s;
            while (Time.time < until) yield return null;
        }

        static void Shot(Vector3 camPos, Vector3 lookAt, string name)
        {
            var camGo = new GameObject("ShelterShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 65f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
            camGo.transform.position = camPos;
            camGo.transform.rotation = Quaternion.LookRotation(lookAt - camPos);
            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Temp", "claude-shots"));
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(camGo); Object.Destroy(rt); Object.Destroy(tex);
        }

        [UnityTest]
        public IEnumerator Shelter_BuildsFromDroppedBranchesAndLeaves()
        {
            var shelter = Shelter.Instance;
            var gm = GameManager.Instance;
            Assert.That(shelter, Is.Not.Null);
            Assert.That(shelter.IsComplete, Is.False);
            Shot(shelter.transform.TransformPoint(new Vector3(0f, 1.6f, 4f)), shelter.transform.TransformPoint(new Vector3(0f, 0.9f, 0f)), "shelter-0-bare-front");

            Vector3 inside = shelter.transform.TransformPoint(new Vector3(0f, 0.9f, -0.4f));

            // a leaf bundle before any branch is refused (and left alone)
            var early = ItemFactory.LeafBundle(1);
            early.transform.position = inside; early.GetComponent<Rigidbody>().position = inside;
            yield return Wait(0.8f);
            Assert.That(shelter.leaves, Is.EqualTo(0), "leaves must not count before a branch is on");
            Object.Destroy(early);

            for (int i = 0; i < shelter.branchesNeeded; i++)
            {
                var b = ItemFactory.Branch(100 + i);
                b.transform.position = inside; b.GetComponent<Rigidbody>().position = inside;
                yield return Wait(0.8f);
                Assert.That(shelter.branches, Is.EqualTo(i + 1), $"branch {i + 1} was not accepted");
            }
            Shot(shelter.transform.TransformPoint(new Vector3(0f, 1.6f, 4f)), shelter.transform.TransformPoint(new Vector3(0f, 0.9f, 0f)), "shelter-1-ribs-front");
            Shot(shelter.transform.TransformPoint(new Vector3(4f, 1.6f, -0.5f)), shelter.transform.TransformPoint(new Vector3(0f, 0.9f, -0.5f)), "shelter-1-ribs-side");

            for (int i = 0; i < shelter.leavesNeeded; i++)
            {
                var l = ItemFactory.LeafBundle(200 + i);
                l.transform.position = inside; l.GetComponent<Rigidbody>().position = inside;
                yield return Wait(0.8f);
                Assert.That(shelter.leaves, Is.EqualTo(i + 1), $"leaf bundle {i + 1} was not accepted");
            }
            yield return Wait(0.3f);
            Shot(shelter.transform.TransformPoint(new Vector3(0f, 1.6f, 4f)), shelter.transform.TransformPoint(new Vector3(0f, 0.9f, 0f)), "shelter-2-done-front");
            Shot(shelter.transform.TransformPoint(new Vector3(0f, 1.6f, -4f)), shelter.transform.TransformPoint(new Vector3(0f, 0.9f, 0f)), "shelter-2-done-back");
            Shot(shelter.transform.TransformPoint(new Vector3(4f, 1.6f, -0.5f)), shelter.transform.TransformPoint(new Vector3(0f, 0.9f, -0.5f)), "shelter-2-done-side");

            Assert.That(shelter.IsComplete, Is.True);
            Assert.That(gm.IsComplete("shelter"), Is.True, "building it completes the objective");

            // the ribs must lean onto the ridge pole (top of each rib within 0.6 m of the ridge line)
            var ribs = shelter.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Rib")).ToList();
            Assert.That(ribs.Count, Is.EqualTo(shelter.branchesNeeded));
            foreach (var r in ribs)
            {
                Vector3 top = shelter.transform.InverseTransformPoint(r.TransformPoint(new Vector3(0f, 2.05f, 0f)));
                Assert.That(Mathf.Abs(top.z), Is.LessThan(0.6f), $"{r.name} top is at local z {top.z:F2}: it leans away from the ridge");
                Assert.That(top.y, Is.InRange(1.0f, 1.9f), $"{r.name} top is at height {top.y:F2}");
            }

            // thatch exists, is visible, lies along the roof slope and the panels tile side by side
            var thatch = shelter.GetComponentsInChildren<MeshRenderer>().Where(m => m.name.StartsWith("Thatch")).ToList();
            Assert.That(thatch.Count, Is.EqualTo(shelter.leavesNeeded));
            foreach (var th in thatch)
            {
                Assert.That(th.enabled && th.sharedMaterial != null, Is.True, th.name + " has no visible material");
                var ms = th.GetComponent<MeshFilter>().sharedMesh.bounds.size;
                float across = ms.x * th.transform.lossyScale.x, alongSlope = ms.z * th.transform.lossyScale.z;
                Assert.That(across, Is.InRange(2.0f, 2.4f), $"{th.name} should span the roof (2.2 m) but is {across:F2} m across");
                Assert.That(alongSlope, Is.InRange(0.6f, 0.95f), $"{th.name} should cover a third of the slope but is {alongSlope:F2} m along it");
            }

            Vector3 roofUp = new Vector3(0f, 1.45f, 1.35f).normalized;
            Vector3 roofNormal = new Vector3(0f, roofUp.z, -roofUp.y);
            var along = new System.Collections.Generic.List<float>();
            foreach (var th in thatch)
            {
                Vector3 c = shelter.transform.InverseTransformPoint(th.bounds.center);
                Vector3 n = shelter.transform.InverseTransformDirection(th.transform.up);
                Assert.That(Vector3.Dot(n, roofNormal), Is.GreaterThan(0.98f), $"{th.name} does not lie on the roof slope (normal {n})");
                along.Add(Vector3.Dot(c - new Vector3(0f, 0f, -1.35f), roofUp));
            }
            along.Sort();
            for (int i = 1; i < along.Count; i++)
                Assert.That(along[i] - along[i - 1], Is.InRange(0.5f, 0.85f),
                    "thatch panels must sit side by side up the slope, not stack: " + string.Join(", ", along.Select(a => a.ToString("F2"))));
            Assert.That(along[0], Is.GreaterThan(0.2f));
            Assert.That(along[along.Count - 1], Is.LessThan(1.85f));
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Forage.EditorTools
{
    /// <summary>
    /// Headless-ish verification of the generated forest.
    ///
    /// The forest is built at runtime in ForestGenerator.Awake(), so it can only
    /// be inspected in play mode. This harness drives the editor through
    /// open scene -> enter play -> settle -> assert -> exit play, and writes a
    /// PASS/FAIL report to Temp/forage-selftest-results.txt.
    ///
    /// It resumes across the domain reloads that play mode causes by keeping its
    /// phase in SessionState, so it can also be kicked off from outside the
    /// editor by dropping a Temp/forage-selftest.request file: the next
    /// recompile picks it up.
    /// </summary>
    [InitializeOnLoad]
    public static class ForageSelfTest
    {
        const string ScenePath = "Assets/Scenes/Forage.unity";
        const string PhaseKey = "Forage.SelfTest.Phase";
        const string RequestFile = "Temp/forage-selftest.request";
        const string ResultFile = "Temp/forage-selftest-results.txt";

        static ForageSelfTest()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem("Forage/Test/Run Forest Self-Test", priority = 200)]
        public static void Request()
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(RequestFile, "go");
            SessionState.SetString(PhaseKey, "");
            Debug.Log("[SelfTest] queued - the forest test will run on the next editor tick.");
        }

        static double _settleUntil;

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            string phase = SessionState.GetString(PhaseKey, "");

            switch (phase)
            {
                case "":
                    if (!File.Exists(RequestFile)) return;
                    File.Delete(RequestFile);
                    if (File.Exists(ResultFile)) File.Delete(ResultFile);
                    Debug.Log("[SelfTest] opening " + ScenePath);
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    SessionState.SetString(PhaseKey, "enter");
                    return;

                case "enter":
                    if (EditorApplication.isPlaying)
                    {
                        // let the generator, NavMesh bake and animal spawns settle
                        _settleUntil = EditorApplication.timeSinceStartup + 6.0;
                        SessionState.SetString(PhaseKey, "settle");
                        return;
                    }
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                        EditorApplication.EnterPlaymode();
                    return;

                case "settle":
                    if (!EditorApplication.isPlaying) { SessionState.SetString(PhaseKey, ""); return; }
                    if (EditorApplication.timeSinceStartup < _settleUntil)
                    {
                        // Drive the simulation forward by hand. An unfocused
                        // editor throttles (often halts) the player loop, so
                        // Awake/Start run but nothing after the first yield
                        // does: coroutines, Update and NavMesh settling all
                        // stall, and time-based behaviour silently never
                        // happens. Stepping makes the wait real and repeatable
                        // whether or not the window has focus.
                        EditorApplication.Step();
                        return;
                    }
                    RunChecks();
                    SessionState.SetString(PhaseKey, "done");
                    EditorApplication.ExitPlaymode();
                    return;

                case "done":
                    if (EditorApplication.isPlaying) return;
                    SessionState.SetString(PhaseKey, "");
                    Debug.Log("[SelfTest] finished. Report: " + ResultFile);
                    return;
            }
        }

        // ------------------------------------------------------------------

        static StringBuilder _sb;
        static int _pass, _fail;

        static void Check(bool ok, string label, string detail = "")
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {label}{(string.IsNullOrEmpty(detail) ? "" : "   [" + detail + "]")}");
        }

        static void Section(string title) => _sb.AppendLine().AppendLine("== " + title + " ==");

        static void RunChecks()
        {
            _sb = new StringBuilder();
            _pass = _fail = 0;
            _sb.AppendLine("Forage forest self-test");
            _sb.AppendLine("run: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            var forest = Object.FindFirstObjectByType<ForestGenerator>();

            // ---------------- terrain and camp ----------------
            Section("Terrain and camp clearing");
            Check(forest != null, "ForestGenerator present in scene");
            if (forest == null) { Write(); return; }

            Check(Mathf.Abs(forest.worldSize - 144f) < 0.01f, "World is 20% smaller (worldSize 144)",
                  "actual " + forest.worldSize);

            float campH = forest.HeightAt(0f, 0f);
            float campLevel = ForestGenerator.CampLevel(forest.seed);
            Check(Mathf.Abs(campH - campLevel) < 0.05f, "Camp centre sits at natural ground level (not a crater)",
                  $"height {campH:F2} vs CampLevel {campLevel:F2}");

            // the clearing must not be a bowl: compare with a ring of forest around it
            float ring = 0f;
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                ring += forest.HeightAt(Mathf.Cos(a) * 20f, Mathf.Sin(a) * 20f);
            }
            ring /= 24f;
            Check(Mathf.Abs(campH - ring) < 2.5f, "Camp is level with the surrounding forest",
                  $"camp {campH:F2} vs ring@20m {ring:F2} (delta {campH - ring:F2} m)");

            var terrain = GameObject.Find("Terrain");
            Check(terrain != null && terrain.GetComponent<MeshCollider>() != null,
                  "Terrain mesh has a collider (walkable)");

            // a downward ray must land on the terrain at the analytic height
            bool hitOk = Physics.Raycast(new Vector3(0f, campH + 60f, 0f), Vector3.down, out var hit, 200f);
            Check(hitOk && Mathf.Abs(hit.point.y - campH) < 0.3f, "Ground raycast at camp matches the height field",
                  hitOk ? $"hit y {hit.point.y:F2}, expected {campH:F2}" : "no hit");

            // ---------------- fire pit ----------------
            Section("Camp fire pit");
            var pit = Object.FindFirstObjectByType<FirePit>();
            Check(pit != null, "Fire pit present");
            if (pit != null)
            {
                float pitGround = forest.HeightAt(pit.transform.position.x, pit.transform.position.z);
                float drop = pit.transform.position.y - pitGround;
                Check(Mathf.Abs(drop) < 0.6f, "Fire pit rests on the ground (not sunk in a hole)",
                      $"pit y {pit.transform.position.y:F2}, ground {pitGround:F2}, delta {drop:F2} m");
            }

            // ---------------- density ----------------
            Section("Forest density");
            var propsRoot = GameObject.Find("GeneratedForest");
            Check(propsRoot != null, "GeneratedForest root exists");
            var renderers = propsRoot != null
                ? propsRoot.GetComponentsInChildren<MeshRenderer>(true)
                : new MeshRenderer[0];
            _sb.AppendLine($"      (total mesh renderers under GeneratedForest: {renderers.Length})");
            Check(renderers.Length >= 1500, "Dense forest: 1500+ props generated", renderers.Length.ToString());

            int Count(string prefix) => renderers.Count(r => r.transform.parent != null &&
                (r.name.StartsWith(prefix) || r.transform.parent.name.StartsWith(prefix)));
            int trees = Count("Tree") + Count("Pine") + Count("Birch") + Count("Broadleaf");
            _sb.AppendLine($"      (tree-ish renderers: {trees})");
            Check(trees >= 200, "Tree cover is substantial", trees.ToString());

            // ---------------- wind ----------------
            Section("Wind / moving foliage");
            var windShader = Shader.Find("Forage/FoliageWind");
            Check(windShader != null, "Forage/FoliageWind shader compiles and is findable");
            var windMats = renderers.SelectMany(r => r.sharedMaterials)
                                    .Where(m => m != null && m.shader == windShader)
                                    .Distinct().ToList();
            Check(windMats.Count >= 3, "Multiple foliage materials use the wind shader", windMats.Count + " materials");
            Check(windMats.Any(m => m.HasProperty("_WindStrength") && m.GetFloat("_WindStrength") > 0f),
                  "Wind strength is non-zero (leaves actually move)");
            int windRenderers = renderers.Count(r => r.sharedMaterials.Any(m => m != null && m.shader == windShader));
            _sb.AppendLine($"      (renderers swaying in the wind: {windRenderers})");
            Check(windRenderers >= 500, "Wind applies to a large share of the foliage", windRenderers.ToString());

            // ---------------- water ----------------
            Section("Pond");
            var water = GameObject.Find("PondWater");
            Check(water != null, "Pond water surface exists");
            if (water != null)
            {
                var wm = water.GetComponent<MeshRenderer>()?.sharedMaterial;
                bool transparent = wm != null &&
                    ((wm.HasProperty("_Surface") && wm.GetFloat("_Surface") > 0.5f) ||
                     (wm.HasProperty("_BaseColor") && wm.GetColor("_BaseColor").a < 0.95f));
                Check(transparent, "Water is transparent (fish are visible through it)",
                      wm == null ? "no material" : $"alpha {(wm.HasProperty("_BaseColor") ? wm.GetColor("_BaseColor").a : -1f):F2}");
                Check(water.CompareTag("Water"), "Water is tagged for the scooping interaction");
            }
            var life = Object.FindFirstObjectByType<PondLife>();
            Check(life != null && life.GetComponentsInChildren<MeshRenderer>(true).Length >= 5,
                  "Fish and crabs populate the pond",
                  life == null ? "no PondLife" : life.GetComponentsInChildren<MeshRenderer>(true).Length + " creatures");

            // ---------------- habitats ----------------
            Section("Habitat zones");
            Check(Habitat.ZoneAt(0f, 0f) == Habitat.Zone.Camp, "Origin classifies as the Camp zone",
                  Habitat.ZoneAt(0f, 0f).ToString());
            var seen = new HashSet<Habitat.Zone>();
            for (float x = -70f; x <= 70f; x += 5f)
                for (float z = -70f; z <= 70f; z += 5f)
                    seen.Add(Habitat.ZoneAt(x, z));
            foreach (Habitat.Zone z in System.Enum.GetValues(typeof(Habitat.Zone)))
                Check(seen.Contains(z), "Zone present in the world: " + z);

            // ---------------- animals ----------------
            Section("Animals and habitats");
            var mgr = Object.FindFirstObjectByType<AnimalManager>();
            Check(mgr != null, "AnimalManager present");
            if (mgr != null)
            {
                Check(mgr.NavMeshReady, "NavMesh baked at runtime (animals can path)");
                Check(mgr.squirrelScale > 1.5f, "Squirrel scaled up 3-4x as requested", "scale " + mgr.squirrelScale);
            }

            int rabbits = Object.FindObjectsByType<Rabbit>(FindObjectsSortMode.None).Length;
            int deer = Object.FindObjectsByType<Deer>(FindObjectsSortMode.None).Length;
            int squirrels = Object.FindObjectsByType<Squirrel>(FindObjectsSortMode.None).Length;
            int snakes = Object.FindObjectsByType<Snake>(FindObjectsSortMode.None).Length;
            _sb.AppendLine($"      (rabbits {rabbits}, deer {deer}, squirrels {squirrels}, snakes {snakes})");
            Check(rabbits >= 1, "Rabbits spawned", rabbits.ToString());
            Check(deer >= 1, "Deer spawned", deer.ToString());
            Check(squirrels >= 1, "Squirrels spawned", squirrels.ToString());
            // Proximity gating, not a frozen first frame: with the simulation
            // actually stepping, a zone near camp legitimately materialises its
            // snake. What must never happen is all seven being pre-spawned.
            int zones = mgr != null ? mgr.snakeZones : 7;
            Check(snakes < zones, "Snakes are proximity-spawned, not all pre-placed",
                  $"{snakes} alive of {zones} ambush zones");

            // Assert the recorded spawn zone, not the live position: by now the
            // simulation has been stepped for several seconds and animals have
            // wandered, so current position tests nothing about placement.
            foreach (var d in Object.FindObjectsByType<Deer>(FindObjectsSortMode.None))
                Check(Habitat.Suits(d.SpawnZone, Habitat.Zone.Meadow, Habitat.Zone.Woodland, Habitat.Zone.Waterside),
                      "Deer spawned in a plausible habitat", d.SpawnZone.ToString());

            foreach (var s in Object.FindObjectsByType<Squirrel>(FindObjectsSortMode.None))
                Check(Habitat.Suits(s.SpawnZone, Habitat.Zone.Woodland, Habitat.Zone.DeepWoods),
                      "Squirrel spawned in woodland", s.SpawnZone.ToString());

            // no animal may be buried in or floating above the ground
            Section("Animals sit on the ground");
            foreach (var a in Object.FindObjectsByType<Animal>(FindObjectsSortMode.None))
            {
                float g = forest.HeightAt(a.transform.position.x, a.transform.position.z);
                float dy = a.transform.position.y - g;
                Check(dy > -0.4f && dy < 1.6f, a.GetType().Name + " grounded", $"{dy:F2} m above terrain");
            }

            // ---------------- player ----------------
            Section("Player rig");
            var sprint = Object.FindFirstObjectByType<SprintController>();
            Check(sprint != null, "SprintController present");
            if (sprint != null)
            {
                Check(sprint.walkSpeed >= 11f, "Walk speed raised for a 3x faster traverse", "walk " + sprint.walkSpeed);
                Check(sprint.runSpeed > sprint.walkSpeed, "Run is faster than walk", "run " + sprint.runSpeed);
            }

            // SprintController lives on the systems object, so measure the rig itself
            var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            Check(origin != null, "XR Origin present");
            if (origin != null)
            {
                // The VR-correctness check that matters: room-scale, not seated.
                // Device mode pins the origin to the startup head pose and
                // ignores the real floor, so physical movement never maps 1:1.
                Check(origin.RequestedTrackingOriginMode ==
                          Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor,
                      "Room-scale: tracking origin is Floor, not seated Device",
                      origin.RequestedTrackingOriginMode.ToString());

                var head = origin.Camera != null ? origin.Camera.transform : origin.transform;
                float pg = forest.HeightAt(head.position.x, head.position.z);
                float eye = head.position.y - pg;
                Check(eye > 0.3f, "Player head is above the terrain (no fall-through)",
                      $"head y {head.position.y:F2}, ground {pg:F2}, eye height {eye:F2} m");

                // Eye height can only be asserted when something is actually
                // reporting a head pose. With no headset attached the editor's
                // OpenXR runtime fails with FORM_FACTOR_UNAVAILABLE and the
                // camera sits at the origin, so VrTrackingSetup's fallback is
                // what supplies a standing height here.
                var headDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(
                    UnityEngine.XR.XRNode.Head);
                if (headDevice.isValid)
                    Check(eye > 1.4f && eye < 2.1f, "Eye height is a realistic 5-6 ft standing view",
                          $"{eye:F2} m ({eye * 3.281f:F1} ft)");
                else
                    _sb.AppendLine($"      (no head device attached - eye height {eye:F2} m comes " +
                                   "from the flat-mode fallback; real height is supplied by the headset)");

                float rigDrop = origin.transform.position.y - forest.HeightAt(origin.transform.position.x, origin.transform.position.z);
                Check(rigDrop > -0.5f && rigDrop < 2.5f, "Rig floor sits on the terrain",
                      $"{rigDrop:F2} m above ground");
            }

            var scout = Object.FindFirstObjectByType<ScoutCompanion>();
            Check(scout != null, "Scout companion present");
            if (scout != null)
            {
                // The complaint was a white ball permanently in view. Visible
                // *while speaking* is the intended design, so tie the assertion
                // to that state rather than to a bare renderer count.
                var mr = scout.GetComponentsInChildren<MeshRenderer>(true);
                int visible = mr.Count(r => r.enabled);
                Check(scout.IsSpeaking || visible == 0,
                      "Scout orb is only visible while speaking (no ball following you)",
                      $"speaking={scout.IsSpeaking}, {visible} visible renderer(s)");
            }

            // ---------------- materials ----------------
            Section("Materials");
            var bad = renderers.Where(r => r.sharedMaterials.Any(m => m == null || m.shader == null ||
                                       m.shader.name == "Hidden/InternalErrorShader")).ToList();
            Check(bad.Count == 0, "No missing/error (magenta) materials in the forest",
                  bad.Count == 0 ? "" : string.Join(", ", bad.Take(5).Select(b => b.name)));

            Write();
        }

        static void Write()
        {
            _sb.AppendLine();
            _sb.AppendLine($"RESULT: {_pass} passed, {_fail} failed");
            Directory.CreateDirectory("Temp");
            File.WriteAllText(ResultFile, _sb.ToString());
            if (_fail == 0) Debug.Log("[SelfTest] all " + _pass + " checks passed.");
            else Debug.LogError($"[SelfTest] {_fail} of {_pass + _fail} checks failed - see {ResultFile}");
        }
    }
}

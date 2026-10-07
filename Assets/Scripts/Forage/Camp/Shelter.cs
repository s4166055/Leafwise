using UnityEngine;
using UnityEngine.ProBuilder;

namespace Forage
{
    /// <summary>
    /// Lean-to shelter build site at camp. A bare frame (posts + ridge pole)
    /// waits for 4 branches and 3 leaf bundles — drop them onto the frame and
    /// the shelter assembles piece by piece (roof panels via ProBuilder).
    /// Complete: counts as warmth at night and secures food from the fox.
    /// </summary>
    public class Shelter : MonoBehaviour
    {
        public int branchesNeeded = 4;
        public int leavesNeeded = 3;

        [Header("State (read-only)")]
        public int branches;
        public int leaves;

        public bool IsComplete => branches >= branchesNeeded && leaves >= leavesNeeded;

        static Shelter _instance;
        public static Shelter Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<Shelter>();
                return _instance;
            }
        }

        Material _bark;
        Material _leafMat;
        bool _celebrated;

        void Awake() => _instance = this;

        public void Build(Material bark, Material leafCards)
        {
            _bark = bark;
            _leafMat = leafCards;

            // frame: two forked posts + ridge pole
            for (int i = 0; i < 2; i++)
            {
                var post = new GameObject("Post");
                post.transform.SetParent(transform, false);
                post.transform.localPosition = new Vector3(i == 0 ? -1.1f : 1.1f, 0, 0);
                post.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(0.06f, 0.045f, 1.5f, 6, 2, 0.05f, 100 + i, 2f);
                post.AddComponent<MeshRenderer>().sharedMaterial = bark;
            }
            var ridge = new GameObject("Ridge");
            ridge.transform.SetParent(transform, false);
            ridge.transform.localPosition = new Vector3(-1.15f, 1.45f, 0);
            ridge.transform.localRotation = Quaternion.Euler(0, 0, -90f);
            ridge.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(0.05f, 0.05f, 2.3f, 6, 2, 0.03f, 103, 2f);
            ridge.AddComponent<MeshRenderer>().sharedMaterial = bark;

            var trigger = gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.8f, 2.2f, 2.4f);
            trigger.center = new Vector3(0, 1f, -0.4f);
        }

        void OnTriggerEnter(Collider other) => TryAccept(other, hint: true);

        // Enter alone misses the natural VR move: carry the branch in, then let
        // go. It was held when it entered, so it was skipped, and releasing it
        // inside raises no second Enter. Stay picks it up once it is let go.
        void OnTriggerStay(Collider other) => TryAccept(other, hint: false);

        void TryAccept(Collider other, bool hint)
        {
            // Enter and Stay can both be queued in one physics step; the second
            // arrives after the first has switched the colliders off: skip it
            if (!other.enabled) return;
            var item = other.GetComponentInParent<SurvivalItem>();
            if (item == null) return;
            if (item.kind != ItemKind.Branch && item.kind != ItemKind.LeafBundle) return;
            var grab = item.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab != null && grab.isSelected) return;

            if (item.kind == ItemKind.Branch && branches < branchesNeeded)
            {
                AddBranchVisual(branches);
                branches++;
            }
            else if (item.kind == ItemKind.LeafBundle && leaves < leavesNeeded)
            {
                if (branches == 0)
                {
                    if (hint) ForageEvents.RaiseHint("shelter-branches-first");
                    return;
                }
                AddLeafPanel(leaves);
                leaves++;
            }
            else return;

            // Destroy lands at end of frame; drop the colliders now so neither a
            // queued event nor a second physics step can count this item again.
            foreach (var c in item.GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(item.gameObject);
            ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Crunch(), 0.7f);
            Haptics.Pulse(0.3f, 0.12f);
            ForageEvents.RaiseSignal("shelter-progress");

            if (IsComplete && !_celebrated)
            {
                _celebrated = true;
                GameManager.Instance.CompleteObjective("shelter");
                FactCard.Show("Shelter complete!",
                    "A lean-to: angled ribs against a ridge pole, covered with leaves for insulation and rain. " +
                    "It keeps you warm at night — and food stored inside is safe from scavengers.", good: true);
            }
        }

        // Roof geometry (shelter-local): ribs stand on the ground at z = RibBaseZ and lean
        // forward onto the ridge pole (height RidgeHeight, z = 0). Every rib and thatch
        // panel is derived from this one slope so they always agree with each other.
        const float RibBaseZ = -1.35f;
        const float RidgeHeight = 1.45f;
        static readonly Vector3 RoofDir = new Vector3(0f, RidgeHeight, -RibBaseZ);            // along the slope, up toward the ridge
        static float RoofLength => RoofDir.magnitude;
        static Vector3 RoofUp => RoofDir.normalized;
        /// <summary>Roof surface normal: up and out toward the open front (-z).</summary>
        static Vector3 RoofNormal => new Vector3(0f, RoofUp.z, -RoofUp.y);

        void AddBranchVisual(int index)
        {
            // angled rib leaning on the ridge
            var rib = new GameObject("Rib" + index);
            rib.transform.SetParent(transform, false);
            float x = Mathf.Lerp(-0.95f, 0.95f, index / (float)(branchesNeeded - 1));
            rib.transform.localPosition = new Vector3(x, 0, RibBaseZ);
            rib.transform.localRotation = Quaternion.FromToRotation(Vector3.up, RoofUp);
            rib.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(0.035f, 0.025f, RoofLength + 0.07f, 5, 2, 0.03f, 200 + index, 2f);
            rib.AddComponent<MeshRenderer>().sharedMaterial = _bark;
        }

        const float PanelWidth = 2.2f;
        Material[] _thatchMats;

        /// <summary>
        /// Thatch is solid green leaf colour, drawn on both sides so it also covers you from
        /// underneath. (The alpha-cutout leaf card used before is a single leaf drawn in a
        /// small part of the quad, so each panel came out as a thin vertical strip.)
        /// </summary>
        Material ThatchMaterial(int index)
        {
            if (_thatchMats == null) _thatchMats = new Material[leavesNeeded];
            if (_thatchMats[index] == null)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Thatch" + index };
                float v = (index % 3) * 0.035f;
                m.SetColor("_BaseColor", new Color(0.27f + v, 0.46f + v, 0.2f));
                m.SetFloat("_Smoothness", 0.1f);
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // double-sided
                _thatchMats[index] = m;
            }
            return _thatchMats[index];
        }

        void AddLeafPanel(int index)
        {
            // ProBuilder plane laid over the ribs as leaf thatch: the panels tile
            // side by side up the slope, each covering a third of it (slightly overlapping)
            float step = RoofLength / leavesNeeded;
            var pb = ShapeGenerator.GeneratePlane(PivotLocation.Center, 2.2f, step + 0.1f, 2, 1, Axis.Up);
            pb.gameObject.name = "Thatch" + index;
            pb.transform.SetParent(transform, false);
            Vector3 centre = new Vector3(0f, 0f, RibBaseZ) + RoofUp * (step * (index + 0.5f)) + RoofNormal * 0.045f;
            pb.transform.localPosition = centre;
            pb.transform.localRotation = Quaternion.FromToRotation(Vector3.up, RoofNormal);
            var mr = pb.GetComponent<MeshRenderer>();
            mr.sharedMaterial = ThatchMaterial(index);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            pb.ToMesh();
            pb.Refresh();

            // ProBuilder's width/length argument order is easy to get backwards (the panels
            // used to come out 0.75 wide and 2.2 long), so size the panel from its real
            // mesh bounds: 2.2 m across the roof, one third of the slope along it.
            var size = pb.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            if (size.x > 0.001f && size.z > 0.001f)
                pb.transform.localScale = new Vector3(PanelWidth / size.x, 1f, (step + 0.1f) / size.z);
        }

        void LateUpdate()
        {
            // completed shelter counts as warmth when you rest inside it
            if (!IsComplete) return;
            var gm = GameManager.Instance;
            if (gm == null || gm.vitals == null) return;
            // the rest spot is in the shelter's own space (it is built turned 155°), not a fixed world offset
            if (Vector3.Distance(gm.PlayerPosition, transform.TransformPoint(new Vector3(0f, 0.8f, -0.5f))) < 2.2f)
                gm.vitals.nearFire = true;
        }
    }
}

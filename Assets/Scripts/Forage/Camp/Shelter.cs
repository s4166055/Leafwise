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

        void OnTriggerEnter(Collider other)
        {
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
                if (branches == 0) { ForageEvents.RaiseHint("shelter-branches-first"); return; }
                AddLeafPanel(leaves);
                leaves++;
            }
            else return;

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

        void AddBranchVisual(int index)
        {
            // angled rib leaning on the ridge
            var rib = new GameObject("Rib" + index);
            rib.transform.SetParent(transform, false);
            float x = Mathf.Lerp(-0.95f, 0.95f, index / (float)(branchesNeeded - 1));
            rib.transform.localPosition = new Vector3(x, 0, -1.35f);
            rib.transform.localRotation = Quaternion.Euler(-47f, 0, 0);
            rib.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(0.035f, 0.025f, 2.05f, 5, 2, 0.03f, 200 + index, 2f);
            rib.AddComponent<MeshRenderer>().sharedMaterial = _bark;
        }

        void AddLeafPanel(int index)
        {
            // ProBuilder plane laid over the ribs as leaf thatch
            var pb = ShapeGenerator.GeneratePlane(PivotLocation.Center, 2.2f, 0.75f, 2, 1, Axis.Up);
            pb.gameObject.name = "Thatch" + index;
            pb.transform.SetParent(transform, false);
            float t = (index + 0.5f) / leavesNeeded;
            pb.transform.localPosition = new Vector3(0, Mathf.Lerp(0.35f, 1.25f, t), Mathf.Lerp(-1.05f, -0.35f, t));
            pb.transform.localRotation = Quaternion.Euler(43f, 0, 0);
            var mr = pb.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _leafMat;
            pb.ToMesh();
            pb.Refresh();
        }

        void LateUpdate()
        {
            // completed shelter counts as warmth when you rest inside it
            if (!IsComplete) return;
            var gm = GameManager.Instance;
            if (gm == null || gm.vitals == null) return;
            if (Vector3.Distance(gm.PlayerPosition, transform.position + new Vector3(0, 0.8f, -0.8f)) < 2.2f)
                gm.vitals.nearFire = true;
        }
    }
}

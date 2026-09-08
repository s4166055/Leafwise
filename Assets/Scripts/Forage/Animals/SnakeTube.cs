using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Renders a snake as ONE continuous tube skinned along its spine every
    /// frame: Catmull-Rom smoothed path through the spine transforms, tapered
    /// radius, cylindrical UVs so the scale texture wraps like real skin.
    /// </summary>
    public class SnakeTube : MonoBehaviour
    {
        public Transform[] spine;
        public float baseRadius = 0.055f;
        public float scale = 1f;
        public int sides = 8;
        public int samplesPerSpan = 3;

        Mesh _mesh;
        Vector3[] _verts;
        Vector3[] _normals;
        int _rings;

        public void Init(Transform[] spinePoints, float bodyScale, Material skin)
        {
            spine = spinePoints;
            scale = bodyScale;

            _rings = (spine.Length - 1) * samplesPerSpan + 1;
            int vcount = _rings * (sides + 1);
            _verts = new Vector3[vcount];
            _normals = new Vector3[vcount];

            var uvs = new Vector2[vcount];
            var tris = new int[(_rings - 1) * sides * 6];

            for (int r = 0; r < _rings; r++)
                for (int s = 0; s <= sides; s++)
                {
                    // v runs along the body so the banded/scale texture wraps around it
                    uvs[r * (sides + 1) + s] = new Vector2((float)s / sides, r * 0.22f);
                }

            int t = 0;
            for (int r = 0; r < _rings - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int i = r * (sides + 1) + s;
                    tris[t++] = i; tris[t++] = i + sides + 1; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + sides + 1; tris[t++] = i + sides + 2;
                }

            _mesh = new Mesh { name = "SnakeTube" };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.uv = uvs;
            _mesh.triangles = tris;

            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = skin;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Body radius along 0..1 (head end → tail tip): thick behind the head, fine tail.</summary>
        float RadiusAt(float t)
        {
            float profile = Mathf.Sin(Mathf.PI * Mathf.Pow(1f - t, 0.62f));
            return (0.35f + 0.65f * profile) * baseRadius * scale * (t > 0.985f ? 0.15f : 1f);
        }

        void LateUpdate()
        {
            if (spine == null || spine.Length < 4 || _mesh == null) return;

            int n = spine.Length;
            Vector3 P(int i) => spine[Mathf.Clamp(i, 0, n - 1)].position;

            int ring = 0;
            Vector3 prevCenter = P(0);
            for (int span = 0; span < n - 1; span++)
            {
                int steps = span == n - 2 ? samplesPerSpan + 1 : samplesPerSpan;
                for (int st = 0; st < steps; st++)
                {
                    float t = (float)st / samplesPerSpan;
                    Vector3 center = CatmullRom(P(span - 1), P(span), P(span + 1), P(span + 2), t);

                    // frame: tangent along path, right/up perpendicular
                    Vector3 tangent = (center - prevCenter);
                    if (tangent.sqrMagnitude < 1e-8f)
                        tangent = P(span + 1) - P(span);
                    tangent.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, tangent);
                    if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
                    right.Normalize();
                    Vector3 up = Vector3.Cross(tangent, right);

                    float pathT = (float)ring / (_rings - 1);
                    float radius = RadiusAt(pathT);
                    // slightly wider than tall = resting snake body
                    float rx = radius * 1.15f, ry = radius * 0.85f;

                    for (int s = 0; s <= sides; s++)
                    {
                        float a = s * Mathf.PI * 2f / sides;
                        Vector3 dir = right * (Mathf.Cos(a) * rx) + up * (Mathf.Sin(a) * ry);
                        int vi = ring * (sides + 1) + s;
                        _verts[vi] = transform.InverseTransformPoint(center + dir);
                        _normals[vi] = transform.InverseTransformDirection(dir.normalized);
                    }
                    prevCenter = center;
                    ring++;
                }
            }

            _mesh.vertices = _verts;
            _mesh.normals = _normals;
            _mesh.RecalculateBounds();
        }
    }
}

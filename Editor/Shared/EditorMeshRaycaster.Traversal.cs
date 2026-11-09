using UnityEngine;

namespace CableGeneratorV2Editor
{
    public static partial class EditorMeshRaycaster
    {
        static int[] s_traversalStack = new int[128];

        const float kDetEpsilon       = 1e-12f;
        const float kBaryEpsilon      = 1e-6f;
        const float kRayTMin          = 1e-6f;

        internal static bool RaycastBvh(
            MeshBvh bvh, Vector3 origin, Vector3 dir,
            ref float bestT, out int hitTri, out float hitU, out float hitV)
        {
            hitTri = -1;
            hitU   = 0f;
            hitV   = 0f;

            Vector3 invDir = new Vector3(
                1f / (Mathf.Abs(dir.x) < 1e-30f ? (dir.x < 0f ? -1e-30f : 1e-30f) : dir.x),
                1f / (Mathf.Abs(dir.y) < 1e-30f ? (dir.y < 0f ? -1e-30f : 1e-30f) : dir.y),
                1f / (Mathf.Abs(dir.z) < 1e-30f ? (dir.z < 0f ? -1e-30f : 1e-30f) : dir.z));

            bool hit = false;
            int  sp  = 0;
            s_traversalStack[sp++] = 0;

            while (sp > 0)
            {
                int nodeIndex = s_traversalStack[--sp];
                ref readonly var node = ref bvh.nodes[nodeIndex];

                if (!RayIntersectsAabb(origin, invDir, node.boundsMin, node.boundsMax, bestT))
                    continue;

                if (node.count > 0)
                {
                    for (int i = node.leftFirst; i < node.leftFirst + node.count; i++)
                    {
                        int tri = bvh.triOrder[i];
                        if (IntersectTriangle(bvh, origin, dir, tri, out float t, out float u, out float v)
                            && t < bestT)
                        {
                            bestT  = t;
                            hitTri = tri;
                            hitU   = u;
                            hitV   = v;
                            hit    = true;
                        }
                    }
                }
                else
                {
                    if (sp + 2 > s_traversalStack.Length)
                        System.Array.Resize(ref s_traversalStack, s_traversalStack.Length * 2);

                    s_traversalStack[sp++] = node.leftFirst;
                    s_traversalStack[sp++] = nodeIndex + 1;
                }
            }

            return hit;
        }

        static bool RayIntersectsAabb(Vector3 origin, Vector3 invDir, Vector3 bMin, Vector3 bMax, float tMax)
        {
            float t1 = (bMin.x - origin.x) * invDir.x;
            float t2 = (bMax.x - origin.x) * invDir.x;
            float tNear = Mathf.Min(t1, t2);
            float tFar  = Mathf.Max(t1, t2);

            t1 = (bMin.y - origin.y) * invDir.y;
            t2 = (bMax.y - origin.y) * invDir.y;
            tNear = Mathf.Max(tNear, Mathf.Min(t1, t2));
            tFar  = Mathf.Min(tFar,  Mathf.Max(t1, t2));

            t1 = (bMin.z - origin.z) * invDir.z;
            t2 = (bMax.z - origin.z) * invDir.z;
            tNear = Mathf.Max(tNear, Mathf.Min(t1, t2));
            tFar  = Mathf.Min(tFar,  Mathf.Max(t1, t2));

            return tNear <= tFar && tFar >= 0f && tNear <= tMax;
        }

        static bool IntersectTriangle(
            MeshBvh bvh, Vector3 origin, Vector3 dir, int tri,
            out float t, out float u, out float v)
        {
            t = 0f; u = 0f; v = 0f;

            Vector3 v0 = bvh.vertices[bvh.indices[tri * 3]];
            Vector3 v1 = bvh.vertices[bvh.indices[tri * 3 + 1]];
            Vector3 v2 = bvh.vertices[bvh.indices[tri * 3 + 2]];

            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;

            Vector3 p   = Vector3.Cross(dir, e2);
            float   det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < kDetEpsilon) return false;

            float invDet = 1f / det;
            Vector3 s = origin - v0;

            u = Vector3.Dot(s, p) * invDet;
            if (u < -kBaryEpsilon || u > 1f + kBaryEpsilon) return false;

            Vector3 q = Vector3.Cross(s, e1);
            v = Vector3.Dot(dir, q) * invDet;
            if (v < -kBaryEpsilon || u + v > 1f + kBaryEpsilon) return false;

            t = Vector3.Dot(e2, q) * invDet;
            return t > kRayTMin;
        }
    }
}

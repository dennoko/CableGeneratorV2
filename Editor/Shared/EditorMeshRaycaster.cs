using System.Collections.Generic;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public struct MeshRayHit
    {
        public Vector3    point;
        public Vector3    normal;
        public float      distance;
        public MeshFilter meshFilter;
        public int        triangleIndex;
    }

    /// <summary>
    /// MeshCollider を付与せずにシーン中の MeshFilter へ直接高速レイキャストするセッション（BVH + Physics フォールバック）。
    /// 2点選択配線・サーフェスなぞり描画・面スナップ投影がそれぞれ独立したセッションを持つため、
    /// 一方の開始・終了がもう一方の候補リストを破壊することはない。
    /// </summary>
    public sealed class MeshRaycastSession : System.IDisposable
    {
        readonly List<EditorMeshRaycaster.Candidate> candidates = new List<EditorMeshRaycaster.Candidate>();
        readonly Dictionary<int, EditorMeshRaycaster.MeshBvh> bvhCache = new Dictionary<int, EditorMeshRaycaster.MeshBvh>();
        readonly HashSet<int> skipped = new HashSet<int>();
        readonly LayerMask activeMask;
        readonly Transform excludeTransform;

        public MeshRaycastSession(LayerMask mask, GameObject excludeRoot)
        {
            activeMask = mask;
            excludeTransform = excludeRoot != null ? excludeRoot.transform : null;

            foreach (var mf in Object.FindObjectsOfType<MeshFilter>(false))
            {
                if (mf.sharedMesh == null) continue;
                if ((mask.value & (1 << mf.gameObject.layer)) == 0) continue;
                if (!mf.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.enabled) continue;

                if (mf.GetComponentInParent<CableGenerator>() != null) continue;
                if (excludeTransform != null && mf.transform.IsChildOf(excludeTransform)) continue;

                candidates.Add(new EditorMeshRaycaster.Candidate
                {
                    meshFilter   = mf,
                    mesh         = mf.sharedMesh,
                    worldToLocal = mf.transform.worldToLocalMatrix,
                    normalMatrix = mf.transform.worldToLocalMatrix.transpose,
                    worldBounds  = renderer.bounds,
                });
            }
        }

        public void Dispose()
        {
            candidates.Clear();
            bvhCache.Clear();
            skipped.Clear();
        }

        /// <summary>
        /// BVH レイキャストを実行し、同時に Physics.Raycast も併用して最も手前のヒットを返す。
        /// これにより、MeshCollider が無い背景メッシュにも、MeshFilter が無いコライダー（Terrain等）にも即座にヒットする。
        /// </summary>
        public bool Raycast(Ray worldRay, out MeshRayHit hit, float maxDistance = float.MaxValue)
        {
            hit = default;

            float bestT    = maxDistance;
            int   bestCand = -1;
            int   bestTri  = -1;
            float bestU    = 0f, bestV = 0f;
            EditorMeshRaycaster.MeshBvh bestBvh = null;

            for (int c = 0; c < candidates.Count; c++)
            {
                var cand = candidates[c];
                if (cand.meshFilter == null || cand.mesh == null) continue;

                if (!cand.worldBounds.IntersectRay(worldRay, out float boundsDist)) continue;
                if (boundsDist >= bestT) continue;

                var bvh = EditorMeshRaycaster.GetOrBuildBvh(cand.mesh, bvhCache, skipped);
                if (bvh == null) continue;

                Vector3 localOrigin = cand.worldToLocal.MultiplyPoint3x4(worldRay.origin);
                Vector3 localDir    = cand.worldToLocal.MultiplyVector(worldRay.direction);

                if (EditorMeshRaycaster.RaycastBvh(bvh, localOrigin, localDir, ref bestT, out int tri, out float u, out float v))
                {
                    bestCand = c;
                    bestTri  = tri;
                    bestU    = u;
                    bestV    = v;
                    bestBvh  = bvh;
                }
            }

            if (bestCand >= 0)
            {
                var best = candidates[bestCand];
                hit.point         = worldRay.origin + worldRay.direction * bestT;
                hit.distance      = bestT;
                hit.meshFilter    = best.meshFilter;
                hit.triangleIndex = bestTri;
                hit.normal        = EditorMeshRaycaster.ComputeWorldNormal(bestBvh, best, worldRay, bestTri, bestU, bestV);
            }

            // Physics.Raycast フォールバック（BVH より手前にコライダーがある場合、または MeshFilter なしオブジェクト対応）
            if (Physics.Raycast(worldRay, out RaycastHit physHit, bestT, activeMask.value, QueryTriggerInteraction.Ignore))
            {
                bool isExcluded = physHit.collider.GetComponentInParent<CableGenerator>() != null
                               || (excludeTransform != null && physHit.collider.transform.IsChildOf(excludeTransform));
                if (!isExcluded && physHit.distance < bestT)
                {
                    hit.point         = physHit.point;
                    hit.normal        = physHit.normal;
                    hit.distance      = physHit.distance;
                    hit.meshFilter    = physHit.collider.GetComponent<MeshFilter>();
                    hit.triangleIndex = physHit.triangleIndex;
                    return true;
                }
            }

            return bestCand >= 0;
        }
    }

    /// <summary>
    /// BVH 構築・交差判定の共通実装（状態を持たない）。
    /// </summary>
    public static class EditorMeshRaycaster
    {
        internal struct Candidate
        {
            public MeshFilter meshFilter;
            public Mesh       mesh;
            public Matrix4x4  worldToLocal;
            public Matrix4x4  normalMatrix;
            public Bounds     worldBounds;
        }

        internal struct BvhNode
        {
            public Vector3 boundsMin, boundsMax;
            public int     leftFirst;
            public int     count;
        }

        internal class MeshBvh
        {
            public Vector3[] vertices;
            public int[]     indices;
            public Vector3[] normals;
            public BvhNode[] nodes;
            public int       nodeCount;
            public int[]     triOrder;
        }

        static int[] s_traversalStack = new int[128];

        const int   kMaxTrianglesPerMesh = 4_000_000;
        const int   kLeafTriCount        = 4;
        const float kDetEpsilon          = 1e-12f;
        const float kBaryEpsilon         = 1e-6f;
        const float kRayTMin             = 1e-6f;
        const float kVectorEpsilonSqr    = 1e-12f;

        /// <summary>
        /// 単発のメッシュに対して BVH レイキャストを行う（テストおよび個別メッシュ判定用）。
        /// </summary>
        public static bool RaycastSingleMesh(Mesh mesh, Matrix4x4 localToWorld, Ray worldRay, out MeshRayHit hit)
        {
            hit = default;
            if (mesh == null) return false;

            var bvh = GetOrBuildBvh(mesh, new Dictionary<int, MeshBvh>(), new HashSet<int>());
            if (bvh == null) return false;

            Matrix4x4 worldToLocal = localToWorld.inverse;
            Vector3 localOrigin = worldToLocal.MultiplyPoint3x4(worldRay.origin);
            Vector3 localDir    = worldToLocal.MultiplyVector(worldRay.direction);

            float bestT = float.MaxValue;
            if (!RaycastBvh(bvh, localOrigin, localDir, ref bestT, out int tri, out float u, out float v))
                return false;

            var cand = new Candidate
            {
                mesh         = mesh,
                worldToLocal = worldToLocal,
                normalMatrix = worldToLocal.transpose,
            };

            hit.point         = worldRay.origin + worldRay.direction * bestT;
            hit.distance      = bestT;
            hit.triangleIndex = tri;
            hit.normal        = ComputeWorldNormal(bvh, cand, worldRay, tri, u, v);
            return true;
        }

        internal static Vector3 ComputeWorldNormal(
            MeshBvh bvh, in Candidate cand, in Ray worldRay, int tri, float u, float v)
        {
            int i0 = bvh.indices[tri * 3];
            int i1 = bvh.indices[tri * 3 + 1];
            int i2 = bvh.indices[tri * 3 + 2];

            Vector3 faceNormalLocal = Vector3.Cross(
                bvh.vertices[i1] - bvh.vertices[i0],
                bvh.vertices[i2] - bvh.vertices[i0]);

            Vector3 localNormal;
            if (bvh.normals != null)
            {
                float w = 1f - u - v;
                localNormal = w * bvh.normals[i0] + u * bvh.normals[i1] + v * bvh.normals[i2];
                if (localNormal.sqrMagnitude < kVectorEpsilonSqr)
                    localNormal = faceNormalLocal;
            }
            else
            {
                localNormal = faceNormalLocal;
            }

            Vector3 worldNormal = cand.normalMatrix.MultiplyVector(localNormal);
            if (worldNormal.sqrMagnitude < kVectorEpsilonSqr)
                worldNormal = Vector3.up;
            worldNormal.Normalize();

            if (Vector3.Dot(worldNormal, worldRay.direction) > 0f)
                worldNormal = -worldNormal;

            return worldNormal;
        }

        internal static MeshBvh GetOrBuildBvh(Mesh mesh, Dictionary<int, MeshBvh> cache, HashSet<int> skipped)
        {
            int id = mesh.GetInstanceID();
            if (skipped.Contains(id)) return null;

            if (cache.TryGetValue(id, out var cached))
            {
                if (cached.vertices.Length == mesh.vertexCount) return cached;
                cache.Remove(id);
            }

            MeshBvh bvh = null;
            try
            {
                bvh = BuildBvh(mesh);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CableGenerator V2] メッシュ '{mesh.name}' のBVH構築をスキップしました: {e.Message}");
            }

            if (bvh == null)
            {
                skipped.Add(id);
                return null;
            }

            cache[id] = bvh;
            return bvh;
        }

        static MeshBvh BuildBvh(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var indices  = mesh.triangles;
            int triCount = indices.Length / 3;
            if (triCount == 0 || triCount > kMaxTrianglesPerMesh) return null;

            var normals = mesh.normals;
            if (normals == null || normals.Length != vertices.Length)
                normals = null;

            var bvh = new MeshBvh
            {
                vertices  = vertices,
                indices   = indices,
                normals   = normals,
                nodes     = new BvhNode[triCount * 2],
                nodeCount = 0,
                triOrder  = new int[triCount],
            };

            var centroids = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                bvh.triOrder[t] = t;
                centroids[t] = (vertices[indices[t * 3]]
                              + vertices[indices[t * 3 + 1]]
                              + vertices[indices[t * 3 + 2]]) / 3f;
            }

            BuildNode(bvh, centroids, 0, triCount);
            return bvh;
        }

        static int BuildNode(MeshBvh bvh, Vector3[] centroids, int first, int count)
        {
            int nodeIndex = bvh.nodeCount++;

            Vector3 bMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 bMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            Vector3 cMin = bMin, cMax = bMax;

            for (int i = first; i < first + count; i++)
            {
                int tri = bvh.triOrder[i];
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = bvh.vertices[bvh.indices[tri * 3 + k]];
                    bMin = Vector3.Min(bMin, p);
                    bMax = Vector3.Max(bMax, p);
                }
                cMin = Vector3.Min(cMin, centroids[tri]);
                cMax = Vector3.Max(cMax, centroids[tri]);
            }

            if (count <= kLeafTriCount)
            {
                bvh.nodes[nodeIndex] = new BvhNode
                {
                    boundsMin = bMin, boundsMax = bMax,
                    leftFirst = first, count = count,
                };
                return nodeIndex;
            }

            Vector3 cSize = cMax - cMin;
            int axis = 0;
            if (cSize.y > cSize.x) axis = 1;
            if (cSize.z > cSize[axis]) axis = 2;
            float splitPos = 0.5f * (cMin[axis] + cMax[axis]);

            int mid = first;
            for (int i = first; i < first + count; i++)
            {
                if (centroids[bvh.triOrder[i]][axis] < splitPos)
                {
                    (bvh.triOrder[i], bvh.triOrder[mid]) = (bvh.triOrder[mid], bvh.triOrder[i]);
                    mid++;
                }
            }

            if (mid == first || mid == first + count)
                mid = first + count / 2;

            BuildNode(bvh, centroids, first, mid - first);
            int rightChild = BuildNode(bvh, centroids, mid, first + count - mid);

            bvh.nodes[nodeIndex] = new BvhNode
            {
                boundsMin = bMin, boundsMax = bMax,
                leftFirst = rightChild, count = 0,
            };
            return nodeIndex;
        }

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


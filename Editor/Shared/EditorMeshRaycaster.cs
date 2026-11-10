using System.Collections.Generic;
using UnityEngine;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// BVH 構築・交差判定の共通実装（状態を持たない）。
    /// </summary>
    public static partial class EditorMeshRaycaster
    {
        internal struct Candidate
        {
            public Renderer   renderer;
            public MeshFilter meshFilter;
            /// <summary>未ベイクの SkinnedMeshRenderer（初回ヒット判定時に現在ポーズでベイクし null に戻す）</summary>
            public SkinnedMeshRenderer pendingSkinned;
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

        const int   kMaxTrianglesPerMesh = 4_000_000;
        const int   kLeafTriCount        = 4;
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
    }
}

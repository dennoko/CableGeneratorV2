using System.Collections.Generic;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
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
}

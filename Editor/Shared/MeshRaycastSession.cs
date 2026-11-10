using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// MeshCollider を付与せずにシーン中のメッシュへ直接高速レイキャストするセッション（BVH + Physics フォールバック）。
    /// ・MeshFilter + MeshRenderer: 共有メッシュをそのまま BVH 化
    /// ・SkinnedMeshRenderer: 初めてレイがバウンズに入った時点で現在ポーズをベイクして BVH 化（セッション中は固定）
    /// 2点選択配線・サーフェスなぞり描画・面スナップ投影がそれぞれ独立したセッションを持つため、
    /// 一方の開始・終了がもう一方の候補リストを破壊することはない。
    /// </summary>
    public sealed class MeshRaycastSession : System.IDisposable
    {
        // SkinnedMeshRenderer.bounds はポーズによって実形状より小さいことがあるため、ベイク前の粗判定は広げて行う
        const float kSkinnedBoundsPadding = 1.5f;

        readonly List<EditorMeshRaycaster.Candidate> candidates = new List<EditorMeshRaycaster.Candidate>();
        readonly Dictionary<int, EditorMeshRaycaster.MeshBvh> bvhCache = new Dictionary<int, EditorMeshRaycaster.MeshBvh>();
        readonly HashSet<int> skipped = new HashSet<int>();
        readonly List<Mesh> bakedMeshes = new List<Mesh>();
        readonly LayerMask activeMask;
        readonly Transform excludeTransform;

        /// <summary>true なら面の裏側には当たらない（メッシュ・MeshCollider とも）。セッション中に変えてもよい。</summary>
        public bool IgnoreBackfaces { get; set; }

        public MeshRaycastSession(LayerMask mask, GameObject excludeRoot, bool ignoreBackfaces = false)
        {
            activeMask = mask;
            IgnoreBackfaces = ignoreBackfaces;
            excludeTransform = excludeRoot != null ? excludeRoot.transform : null;

            // Prefab モード中はそのステージ内、通常時はメインステージ（開いている全シーン）から探す。
            // StageHandle.FindComponentsOfType は非アクティブな GameObject 上のコンポーネントも返すため、
            // activeInHierarchy や SceneVisibilityManager の非表示・ピック無効状態を必ず確認する。
            var stage = StageUtility.GetCurrentStageHandle();

            foreach (var mf in stage.FindComponentsOfType<MeshFilter>())
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (!mf.TryGetComponent<MeshRenderer>(out var renderer) || !IsPickableRenderer(renderer)) continue;
                if (IsExcluded(mf.transform)) continue;

                candidates.Add(new EditorMeshRaycaster.Candidate
                {
                    renderer     = renderer,
                    meshFilter   = mf,
                    mesh         = mf.sharedMesh,
                    worldToLocal = mf.transform.worldToLocalMatrix,
                    normalMatrix = mf.transform.worldToLocalMatrix.transpose,
                    worldBounds  = renderer.bounds,
                });
            }

            foreach (var smr in stage.FindComponentsOfType<SkinnedMeshRenderer>())
            {
                if (smr == null || smr.sharedMesh == null || !IsPickableRenderer(smr)) continue;
                if (IsExcluded(smr.transform)) continue;

                Bounds b = smr.bounds;
                b.extents *= kSkinnedBoundsPadding;
                candidates.Add(new EditorMeshRaycaster.Candidate
                {
                    renderer       = smr,
                    pendingSkinned = smr,
                    worldBounds    = b,
                });
            }
        }

        static bool IsPickableRenderer(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff) return false;
            return IsPickableGameObject(renderer.gameObject);
        }

        static bool IsPickableGameObject(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return false;
            var vis = SceneVisibilityManager.instance;
            if (vis != null && (vis.IsHidden(go) || vis.IsPickingDisabled(go))) return false;
            return true;
        }

        bool IsExcluded(Transform t)
        {
            if ((activeMask.value & (1 << t.gameObject.layer)) == 0) return true;
            if (t.GetComponentInParent<CableGenerator>() != null) return true;
            return excludeTransform != null && t.IsChildOf(excludeTransform);
        }

        public void Dispose()
        {
            candidates.Clear();
            bvhCache.Clear();
            skipped.Clear();
            foreach (var m in bakedMeshes)
                if (m != null) Object.DestroyImmediate(m);
            bakedMeshes.Clear();
        }

        /// <summary>
        /// BVH レイキャストを実行し、同時に Physics.Raycast も併用して最も手前のヒットを返す。
        /// これにより、MeshCollider が無い背景メッシュ・スキンメッシュにも、MeshFilter が無いコライダー（Terrain等）にも即座にヒットする。
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
                if (cand.renderer == null) continue;

                if (!cand.worldBounds.IntersectRay(worldRay, out float boundsDist)) continue;
                if (boundsDist >= bestT) continue;

                if (cand.pendingSkinned != null)
                {
                    BakeSkinned(ref cand);
                    candidates[c] = cand;
                    if (cand.mesh == null) continue;
                    if (!cand.worldBounds.IntersectRay(worldRay, out boundsDist) || boundsDist >= bestT) continue;
                }
                if (cand.mesh == null) continue;

                var bvh = EditorMeshRaycaster.GetOrBuildBvh(cand.mesh, bvhCache, skipped);
                if (bvh == null) continue;

                Vector3 localOrigin = cand.worldToLocal.MultiplyPoint3x4(worldRay.origin);
                Vector3 localDir    = cand.worldToLocal.MultiplyVector(worldRay.direction);

                if (EditorMeshRaycaster.RaycastBvh(bvh, localOrigin, localDir, ref bestT, out int tri, out float u, out float v, IgnoreBackfaces))
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
                hit.renderer      = best.renderer;
                hit.meshFilter    = best.meshFilter;
                hit.triangleIndex = bestTri;
                hit.normal        = EditorMeshRaycaster.ComputeWorldNormal(bestBvh, best, worldRay, bestTri, bestU, bestV);
            }

            // Physics.Raycast フォールバック（BVH より手前にコライダーがある場合、または MeshFilter なしオブジェクト対応）
            // MeshCollider の裏面もメッシュと同じ扱いにする（プロジェクトの設定は呼んだ後に戻す）
            bool projectHitsBackfaces = Physics.queriesHitBackfaces;
            bool physHitFound;
            RaycastHit physHit;
            try
            {
                Physics.queriesHitBackfaces = !IgnoreBackfaces;
                physHitFound = Physics.Raycast(worldRay, out physHit, bestT, activeMask.value, QueryTriggerInteraction.Ignore);
            }
            finally
            {
                Physics.queriesHitBackfaces = projectHitsBackfaces;
            }
            if (physHitFound)
            {
                bool isExcluded = !IsPickableGameObject(physHit.collider.gameObject)
                               || physHit.collider.GetComponentInParent<CableGenerator>() != null
                               || (excludeTransform != null && physHit.collider.transform.IsChildOf(excludeTransform));
                if (!isExcluded && physHit.distance < bestT)
                {
                    hit.point         = physHit.point;
                    hit.normal        = physHit.normal;
                    hit.distance      = physHit.distance;
                    hit.renderer      = physHit.collider.GetComponent<Renderer>();
                    hit.meshFilter    = physHit.collider.GetComponent<MeshFilter>();
                    hit.triangleIndex = physHit.triangleIndex;
                    return true;
                }
            }

            return bestCand >= 0;
        }

        /// <summary>
        /// SkinnedMeshRenderer を現在ポーズでベイクする。
        /// ・ボーン＋バインドポーズを持つ通常のスキンメッシュ:
        ///   BakeMesh(useScale: false) の頂点はボーン行列のワールドスケールを含んだ状態で
        ///   Transform の位置・回転のみを除いた空間（TRS(position, rotation, Vector3.one)）に出力される。
        ///   BakeMesh(useScale: true) や localToWorldMatrix を使うと FBX 等の localScale (100倍等) が二重適用されてしまう。
        /// ・ボーンを持たない SkinnedMeshRenderer（ブレンドシェイプのみ等）:
        ///   BakeMesh(useScale: false) はスケール未適用のローカル頂点を返すため localToWorldMatrix でワールドへ戻す。
        /// </summary>
        void BakeSkinned(ref EditorMeshRaycaster.Candidate cand)
        {
            var smr = cand.pendingSkinned;
            cand.pendingSkinned = null;

            var baked = new Mesh { name = smr.name + " (CableGen Pick)", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                smr.BakeMesh(baked, false);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CableGenerator V2] スキンメッシュ '{smr.name}' のベイクをスキップしました: {e.Message}");
                Object.DestroyImmediate(baked);
                return;
            }

            if (baked.vertexCount == 0)
            {
                Object.DestroyImmediate(baked);
                return;
            }
            baked.RecalculateBounds();
            bakedMeshes.Add(baked);

            Transform t = smr.transform;
            Matrix4x4 meshToWorld = HasSkinningBones(smr)
                ? Matrix4x4.TRS(t.position, t.rotation, Vector3.one)
                : t.localToWorldMatrix;

            cand.mesh         = baked;
            cand.worldToLocal = meshToWorld.inverse;
            cand.normalMatrix = cand.worldToLocal.transpose;
            cand.worldBounds  = TransformBounds(meshToWorld, baked.bounds);
        }

        static bool HasSkinningBones(SkinnedMeshRenderer smr)
        {
            var sharedMesh = smr.sharedMesh;
            if (sharedMesh == null) return false;
            var bones = smr.bones;
            if (bones == null || bones.Length == 0) return false;
            var bindposes = sharedMesh.bindposes;
            if (bindposes == null || bindposes.Length == 0) return false;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null) return true;
            }
            return false;
        }

        static Bounds TransformBounds(Matrix4x4 m, Bounds local)
        {
            Vector3 c = local.center, e = local.extents;
            var result = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                result.Encapsulate(m.MultiplyPoint3x4(c + corner));
            }
            return result;
        }
    }
}

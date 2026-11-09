using UnityEngine;

namespace CableGeneratorV2Runtime
{
    public partial class CableGenerator
    {
        void BuildLodTiers()
        {
            if (!enableLod) return;
            if (!TryGetProfileSource(out var src))
            {
                ClearLodMeshes();
                return;
            }
            BuildLodTiers(src);
        }

        void BuildLodTiers(in ProfileSource src)
        {
            var f1 = FindLodFilter(1);
            var f2 = FindLodFilter(2);
            if (f1 != null) BuildTierMesh(AcquireOwnedMesh(f1, 1), 1, src, 0, null);
            if (f2 != null) BuildTierMesh(AcquireOwnedMesh(f2, 2), 2, src, 0, null);

            var lodGroup = GetComponent<LODGroup>();
            if (lodGroup != null && lodGroup.enabled)
                lodGroup.RecalculateBounds();
        }

        void ClearLodMeshes()
        {
            var m1 = FindOwnedMesh(FindLodFilter(1), 1);
            var m2 = FindOwnedMesh(FindLodFilter(2), 2);
            if (m1 != null) m1.Clear();
            if (m2 != null) m2.Clear();
        }

        // ================================================================
        //  Owned Mesh (MeshFilter の参照を正とする所有判定)
        // ================================================================

        string OwnedMeshName(int tier) => "CableMesh_LOD" + tier + "_" + GetInstanceID();

        Mesh FindOwnedMesh(MeshFilter mf, int tier)
        {
            if (mf == null) return null;
            Mesh m = mf.sharedMesh;
            if (m == null || !CableObjectUtility.IsTransient(m)) return null;
            return m.name == OwnedMeshName(tier) ? m : null;
        }

        Mesh AcquireOwnedMesh(MeshFilter mf, int tier)
        {
            Mesh m = FindOwnedMesh(mf, tier);
            if (m != null) return m;

            // 複製元と共有している／旧バージョンでシーンに埋め込まれたメッシュは使わず新規に確保
            m = new Mesh
            {
                name      = OwnedMeshName(tier),
                hideFlags = CableObjectUtility.TransientAssetFlags,
            };
            m.MarkDynamic();
            mf.sharedMesh = m;
            return m;
        }

        void DestroyOwnedMesh(MeshFilter mf, int tier)
        {
            Mesh m = FindOwnedMesh(mf, tier);
            if (m == null) return;
            mf.sharedMesh = null;
            CableObjectUtility.Destroy(m);
        }

        // ================================================================
        //  LOD Structure (一時オブジェクトとして構築し、シーンには保存しない)
        // ================================================================

        MeshFilter FindLodFilter(int tier)
        {
            Transform root = transform.Find(kLodRootName);
            if (root == null) return null;
            Transform child = root.Find("LOD" + tier);
            return child != null ? child.GetComponent<MeshFilter>() : null;
        }

        void SyncLodStructure()
        {
            MigrateLegacyLodHierarchy();

            var rootRenderer = GetComponent<MeshRenderer>();
            Transform lodRoot = transform.Find(kLodRootName);
            var lodGroup = GetComponent<LODGroup>();

            if (!enableLod || rootRenderer == null)
            {
                RemoveLodStructure(transform);
                return;
            }

            if (lodRoot == null)
                lodRoot = CreateTransientChild(transform, kLodRootName);

            Renderer r1 = EnsureTierRenderer(lodRoot, 1, rootRenderer);
            Renderer r2 = EnsureTierRenderer(lodRoot, 2, rootRenderer);

            if (lodGroup == null)
                lodGroup = gameObject.AddComponent<LODGroup>();
            lodGroup.enabled = true;

            GetLodScreenHeights(out float h1, out float h2, out float hc);
            lodGroup.SetLODs(new[]
            {
                new LOD(h1, new Renderer[] { rootRenderer }),
                new LOD(h2, new[] { r1 }),
                new LOD(hc, new[] { r2 }),
            });
        }

        static void RemoveLodStructure(Transform owner)
        {
            // ユーザーが独自に付けた LODGroup は触らない（LOD0 がこのオブジェクトの Renderer のものだけ対象）
            var lodGroup = owner.GetComponent<LODGroup>();
            if (lodGroup != null && IsGeneratedLodGroup(lodGroup, owner.GetComponent<MeshRenderer>()))
            {
                if (CableObjectUtility.CanDestroy(lodGroup))
                    CableObjectUtility.Destroy(lodGroup);
                else
                    lodGroup.enabled = false; // Prefab アセット由来で削除できない場合は無効化
            }

            Transform lodRoot = owner.Find(kLodRootName);
            if (lodRoot != null && CableObjectUtility.IsTransient(lodRoot.gameObject))
                CableObjectUtility.Destroy(lodRoot.gameObject);
        }

        static bool IsGeneratedLodGroup(LODGroup lodGroup, Renderer rootRenderer)
        {
            if (rootRenderer == null) return false;
            var lods = lodGroup.GetLODs();
            if (lods.Length == 0 || lods[0].renderers == null) return false;
            foreach (var r in lods[0].renderers)
                if (r == rootRenderer) return true;
            // 旧バージョンは LOD0 を子オブジェクトに持っていた
            return lodGroup.transform.Find(kLodRootName) != null;
        }

        /// <summary>
        /// 旧バージョンでシーンに保存された LOD 階層（LOD0 子 + ルート Renderer 無効）を一時階層へ移行する。
        /// </summary>
        void MigrateLegacyLodHierarchy()
        {
            Transform legacy = transform.Find(kLodRootName);
            if (legacy == null || CableObjectUtility.IsTransient(legacy.gameObject)) return;
            if (!CableObjectUtility.CanDestroy(legacy.gameObject)) return;

            CableObjectUtility.Destroy(legacy.gameObject);
            var rootRenderer = GetComponent<MeshRenderer>();
            if (rootRenderer != null) rootRenderer.enabled = true;
        }

        static Transform CreateTransientChild(Transform parent, string name)
        {
            var go = new GameObject(name) { hideFlags = CableObjectUtility.TransientObjectFlags };
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Renderer EnsureTierRenderer(Transform lodRoot, int tier, MeshRenderer source)
        {
            Transform child = lodRoot.Find("LOD" + tier);
            if (child == null) child = CreateTransientChild(lodRoot, "LOD" + tier);

            if (child.GetComponent<MeshFilter>() == null)
                child.gameObject.AddComponent<MeshFilter>();

            var mr = child.GetComponent<MeshRenderer>();
            if (mr == null) mr = child.gameObject.AddComponent<MeshRenderer>();

            mr.sharedMaterials   = source.sharedMaterials;
            mr.shadowCastingMode = source.shadowCastingMode;
            mr.receiveShadows    = source.receiveShadows;
            return mr;
        }
    }
}

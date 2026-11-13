using System.Collections.Generic;
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
            BuildOwnedTier(FindLodFilter(1), 1, src);
            BuildOwnedTier(FindLodFilter(2), 2, src);

            var lodGroup = GetComponent<LODGroup>();
            if (lodGroup != null && lodGroup.enabled)
                lodGroup.RecalculateBounds();
        }

        void BuildOwnedTier(MeshFilter mf, int tier, in ProfileSource src)
        {
            if (mf == null) return;
            Mesh mesh = AcquireOwnedMesh(mf, tier);
            BuildTierMesh(mesh, tier, src, 0, null);
            ConfirmSavedContent(mf, mesh);
        }

        void ClearOwnedTier(MeshFilter mf, int tier)
        {
            Mesh mesh = FindOwnedMesh(mf, tier);
            if (mesh == null) return;
            mesh.Clear();
            ConfirmSavedContent(mf, mesh);
        }

        void ClearLodMeshes()
        {
            ClearOwnedTier(FindLodFilter(1), 1);
            ClearOwnedTier(FindLodFilter(2), 2);
        }

        // ================================================================
        //  Owned Mesh
        //  ・メッシュは MeshFilter の参照を正とし、シーンに直接シリアライズされる（C# フィールドには持たない）。
        //  ・InstanceID はシーンの再読み込みで変わるため、所有者は名前ではなく実行時の台帳で判定する。
        //    台帳はドメインリロードで消えるが、「最初に使ったケーブルが所有者」になるだけで破綻しない。
        //  ・複製（Ctrl+D）直後は複製元と同じメッシュを参照している。複製元が台帳に載っていて今も
        //    そのメッシュを使っていれば、複製側は所有者になれず自分用のメッシュを新しく確保する。
        // ================================================================

        const string kOwnedMeshPrefix = "CableMesh_LOD";

        // key: メッシュの InstanceID
        static readonly Dictionary<int, CableGenerator> s_meshOwners = new Dictionary<int, CableGenerator>();

        static string OwnedMeshName(int tier) => kOwnedMeshPrefix + tier;

        MeshFilter FilterForTier(int tier) => tier == 0 ? GetComponent<MeshFilter>() : FindLodFilter(tier);

        /// <summary>
        /// このツールが生成したメッシュか。ユーザーが MeshFilter に割り当てたメッシュやアセットのメッシュを
        /// 誤って Clear / 上書きしないための判定（旧バージョンの名前は末尾に "_InstanceID" が付く）。
        /// </summary>
        static bool IsGeneratedMesh(Mesh m, int tier)
        {
            if (m == null) return false;
            string expected = OwnedMeshName(tier);
            string name = m.name;
            if (name != expected && !name.StartsWith(expected + "_", System.StringComparison.Ordinal)) return false;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(m)) return false;
#endif
            return true;
        }

        /// <param name="claim">
        /// true: 所有者のいないメッシュ（シーンから読み込まれた直後など）を自分のものとして台帳に登録する。
        /// false: すでに自分が所有者として登録済みのものだけを返す。
        /// </param>
        Mesh FindOwnedMesh(MeshFilter mf, int tier, bool claim = true)
        {
            if (mf == null) return null;
            Mesh m = mf.sharedMesh;
            if (!IsGeneratedMesh(m, tier)) return null;

            int id = m.GetInstanceID();
            s_meshOwners.TryGetValue(id, out CableGenerator owner);
            if (ReferenceEquals(owner, this)) return m;
            if (!claim) return null;

            if (owner != null)
            {
                // 複製元がまだ使っているメッシュは共有しない
                MeshFilter ownerFilter = owner.FilterForTier(tier);
                if (ownerFilter != null && ownerFilter != mf && ownerFilter.sharedMesh == m) return null;
            }

            s_meshOwners[id] = this;
            OnExistingMeshClaimed(mf, m, tier);
            return m;
        }

        void OnExistingMeshClaimed(MeshFilter mf, Mesh m, int tier)
        {
            m.MarkDynamic();

            bool changed = false;
            // 旧バージョンの「保存しない」フラグ付きメッシュを保存対象へ移行
            changed |= CableObjectUtility.SetHideFlags(m, CableObjectUtility.GeneratedAssetFlags);
            string name = OwnedMeshName(tier);
            if (m.name != name) { m.name = name; changed = true; }

#if UNITY_EDITOR
            // 保存済みの内容を控えておき、再生成の結果が変わっていたらシーンを未保存扱いにする
            if (!Application.isPlaying)
                s_savedContentHashes[m.GetInstanceID()] = ComputeContentHash(m);
#endif
            if (changed) CableObjectUtility.MarkSceneDataChanged(mf);
        }

        Mesh AcquireOwnedMesh(MeshFilter mf, int tier)
        {
            Mesh m = FindOwnedMesh(mf, tier);
            if (m != null) return m;

            // 未生成／複製元と共有している／ユーザーが割り当てた別メッシュ → 新規に確保
            m = new Mesh
            {
                name      = OwnedMeshName(tier),
                hideFlags = CableObjectUtility.GeneratedAssetFlags,
            };
            m.MarkDynamic();
            mf.sharedMesh = m;
            s_meshOwners[m.GetInstanceID()] = this;
            CableObjectUtility.MarkSceneDataChanged(mf);
            return m;
        }

        /// <summary>台帳から自分の登録を外す（メッシュ自体と MeshFilter の参照には触れない）。</summary>
        static void ReleaseMeshOwnership(Mesh m)
        {
            if (ReferenceEquals(m, null)) return;
            int id = m.GetInstanceID();
            s_meshOwners.Remove(id);
#if UNITY_EDITOR
            s_savedContentHashes.Remove(id);
#endif
        }

        // ================================================================
        //  Saved Content Check
        //  シーンを開いただけ（＝未変更）でも、プロファイルアセットの変更やツールの更新で再生成結果が
        //  保存済みメッシュと変わることがある。その場合だけシーンを未保存扱いにして、
        //  古いメッシュのままビルド・アップロードされるのを防ぐ。
        // ================================================================

        void ConfirmSavedContent(MeshFilter mf, Mesh m)
        {
#if UNITY_EDITOR
            if (m == null || s_savedContentHashes.Count == 0) return;
            int id = m.GetInstanceID();
            if (!s_savedContentHashes.TryGetValue(id, out int savedHash)) return;
            s_savedContentHashes.Remove(id);
            if (savedHash != ComputeContentHash(m))
                CableObjectUtility.MarkSceneDataChanged(mf);
#endif
        }

#if UNITY_EDITOR
        // key: メッシュの InstanceID / value: シーンから読み込まれた時点の内容ハッシュ
        static readonly Dictionary<int, int> s_savedContentHashes = new Dictionary<int, int>();
        static readonly List<Vector3> s_hashV3 = new List<Vector3>();
        static readonly List<Vector2> s_hashV2 = new List<Vector2>();

        static int ComputeContentHash(Mesh m)
        {
            unchecked
            {
                int h = m.vertexCount;
                if (!m.isReadable) return h;

                m.GetVertices(s_hashV3);
                for (int i = 0; i < s_hashV3.Count; i++) h = h * 31 + s_hashV3[i].GetHashCode();
                m.GetNormals(s_hashV3);
                for (int i = 0; i < s_hashV3.Count; i++) h = h * 31 + s_hashV3[i].GetHashCode();
                for (int channel = 0; channel < 2; channel++)
                {
                    m.GetUVs(channel, s_hashV2);
                    for (int i = 0; i < s_hashV2.Count; i++) h = h * 31 + s_hashV2[i].GetHashCode();
                }
                for (int s = 0; s < m.subMeshCount; s++)
                    h = h * 31 + (int)m.GetIndexCount(s);

                s_hashV3.Clear();
                s_hashV2.Clear();
                return h;
            }
        }
#endif

        // ================================================================
        //  LOD Structure (LOD1 / LOD2 を子階層として構築し、シーン・ビルドにそのまま保存する)
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

            // Undo を経由しない構造変更なので、変更があったときはシーンを未保存扱いにする
            bool changed = false;

            if (lodRoot == null)
            {
                lodRoot = CreateGeneratedChild(transform, kLodRootName);
                changed = true;
            }
            else
            {
                changed |= CableObjectUtility.SetHideFlags(lodRoot.gameObject, CableObjectUtility.GeneratedObjectFlags);
                if (!lodRoot.gameObject.activeSelf)
                {
                    lodRoot.gameObject.SetActive(true);
                    changed = true;
                }
            }

            Renderer r1 = EnsureTierRenderer(lodRoot, 1, rootRenderer, ref changed);
            Renderer r2 = EnsureTierRenderer(lodRoot, 2, rootRenderer, ref changed);

            if (lodGroup == null)
            {
                lodGroup = gameObject.AddComponent<LODGroup>();
                changed = true;
            }
            lodGroup.enabled = true;

            GetLodScreenHeights(out float h1, out float h2, out float hc);
            lodGroup.SetLODs(new[]
            {
                new LOD(h1, new Renderer[] { rootRenderer }),
                new LOD(h2, new[] { r1 }),
                new LOD(hc, new[] { r2 }),
            });

            if (changed) CableObjectUtility.MarkSceneDataChanged(lodGroup);
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
            if (lodRoot == null) return;
            if (CableObjectUtility.CanDestroy(lodRoot.gameObject))
                CableObjectUtility.Destroy(lodRoot.gameObject);
            else
                lodRoot.gameObject.SetActive(false); // Prefab アセット由来で削除できない場合は非表示（再有効化時に戻す）
        }

        /// <summary>
        /// コンポーネントだけが外されたときに残る LOD 階層を、通常のオブジェクトとして編集できるようにする。
        /// </summary>
        static void UnlockLodStructure(Transform owner)
        {
            Transform lodRoot = owner.Find(kLodRootName);
            if (lodRoot == null) return;
            CableObjectUtility.SetHideFlags(lodRoot.gameObject, HideFlags.None);
            for (int i = 0; i < lodRoot.childCount; i++)
                CableObjectUtility.SetHideFlags(lodRoot.GetChild(i).gameObject, HideFlags.None);
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
        /// 旧バージョンでシーンに保存された LOD 階層（LOD0 子 + ルート Renderer 無効）を現行の構成へ移行する。
        /// 現行の階層は LOD1 / LOD2 しか持たないので、LOD0 子の有無で見分ける。
        /// </summary>
        void MigrateLegacyLodHierarchy()
        {
            Transform legacy = transform.Find(kLodRootName);
            if (legacy == null || legacy.Find("LOD0") == null) return;
            if (!CableObjectUtility.CanDestroy(legacy.gameObject)) return;

            CableObjectUtility.Destroy(legacy.gameObject);
            var rootRenderer = GetComponent<MeshRenderer>();
            if (rootRenderer != null) rootRenderer.enabled = true;
            CableObjectUtility.MarkSceneDataChanged(this);
        }

        static Transform CreateGeneratedChild(Transform parent, string name)
        {
            var go = new GameObject(name) { hideFlags = CableObjectUtility.GeneratedObjectFlags };
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Renderer EnsureTierRenderer(Transform lodRoot, int tier, MeshRenderer source, ref bool changed)
        {
            Transform child = lodRoot.Find("LOD" + tier);
            if (child == null)
            {
                child = CreateGeneratedChild(lodRoot, "LOD" + tier);
                changed = true;
            }
            else
            {
                // 旧バージョンの「保存しない」フラグ付き階層を保存対象へ移行
                changed |= CableObjectUtility.SetHideFlags(child.gameObject, CableObjectUtility.GeneratedObjectFlags);
            }

            if (child.GetComponent<MeshFilter>() == null)
            {
                child.gameObject.AddComponent<MeshFilter>();
                changed = true;
            }

            var mr = child.GetComponent<MeshRenderer>();
            if (mr == null)
            {
                mr = child.gameObject.AddComponent<MeshRenderer>();
                changed = true;
            }

            mr.sharedMaterials   = source.sharedMaterials;
            mr.shadowCastingMode = source.shadowCastingMode;
            mr.receiveShadows    = source.receiveShadows;
            return mr;
        }
    }
}

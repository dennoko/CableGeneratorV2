using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// スプラインと断面プロファイルからケーブルメッシュを生成するコンポーネント。
    ///
    /// 【ライフサイクル設計】
    /// ・生成物（メッシュ・LOD 子階層）はすべて「シーンに保存しない一時オブジェクト」として扱い、
    ///   保存されるのは入力（スプライン・設定）のみ。生成物はいつでも入力から再生成できる。
    /// ・生成メッシュは MeshFilter の参照そのものを正とし、名前に所有者の InstanceID を埋めて所有判定する。
    ///   C# 側のフィールドにキャッシュしないため、ドメインリロードで参照を失っても再利用・再生成が破綻しない。
    /// ・GameObject / コンポーネントの生成・破棄（構造変更）は OnValidate / OnEnable / OnGUI 中に行わず、
    ///   EditorApplication.delayCall で 1 回にまとめて実行する。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [ExecuteAlways]
    public class CableGenerator : MonoBehaviour
    {
        [Header("スプラインデータ (Splinesパッケージ不要)")]
        [SerializeField]
        CableSpline spline = new CableSpline();

        [Header("断面プロファイル")]
        public CableProfile profile;

        [Tooltip("断面の太さ倍率（プロファイルアセットを変更せずに太さをスケール）")]
        [Range(0.1f, 5f)]
        public float radiusScale = 1f;

        [Header("メッシュ分割・最適化設定")]
        [Tooltip("ON: 直線部のポリゴンを自動削減する曲率適応型分割 / OFF: 従来の均等分割")]
        public bool useAdaptiveSampling = true;

        [Tooltip("曲率適応型分割の許容角度（小さいほどカーブが滑らか、大きいほど軽量）")]
        [Range(1.5f, 25f)]
        public float adaptiveAngleError = 6f;

        [Tooltip("均等分割モード時のスプライン方向分割数")]
        [Range(2, 256)]
        public int resolution = 32;

        [Tooltip("UVタイリングスケール")]
        public float uvTiling = 1f;

        [Header("コライダー自動接地 (Auto-Draping)")]
        [Tooltip("ONのとき、たわませたケーブルが床や障害物のコライダー表面に自動で乗っかる")]
        public bool enableSurfaceCollision = false;

        [Tooltip("接地判定対象のレイヤーマスク")]
        public LayerMask collisionLayerMask = ~0;

        [Header("軽量化 LOD 自動生成")]
        [Tooltip("ONのとき、LOD1・LOD2 メッシュと LODGroup を自動生成する（LOD0 はこのオブジェクト自身の MeshRenderer）")]
        public bool enableLod = false;

        [Range(0.05f, 0.8f)]
        public float lod1ScreenHeight = 0.25f;

        [Range(0.01f, 0.4f)]
        public float lod2ScreenHeight = 0.08f;

        [Range(0.001f, 0.1f)]
        public float lodCullScreenHeight = 0.015f;

        [Header("FBX 書き出し (ボーン＆ウェイト付き)")]
        [Tooltip("FBX 書き出し時に生成するボーン本数（始点〜終点を等弧長で分割）")]
        [Range(2, 32)]
        public int boneCount = 8;

        public const string kLodRootName = "_CableLODs";
        public const string kLegacyBonesRootName = "Bones_Root";
        public const int kMinBones = 2;
        public const int kMaxBones = 32;

        public CableSpline Spline => spline;
        public Mesh GeneratedMesh     => FindOwnedMesh(GetComponent<MeshFilter>(), 0);
        public Mesh GeneratedLod1Mesh => FindOwnedMesh(FindLodFilter(1), 1);
        public Mesh GeneratedLod2Mesh => FindOwnedMesh(FindLodFilter(2), 2);

        // ---- 作業用バッファ（生成処理中だけ使う一時メモリ。状態の保持には使わない）----
        static readonly List<CableSamplePoint> s_samples     = new List<CableSamplePoint>();
        static readonly List<Vector3>          s_verts       = new List<Vector3>();
        static readonly List<int>              s_tris        = new List<int>();
        static readonly List<Vector3>          s_normals     = new List<Vector3>();
        static readonly List<Vector2>          s_uvs         = new List<Vector2>();
        static readonly List<Vector2>          s_uv2s        = new List<Vector2>();

#if UNITY_EDITOR
        // 遅延実行の重複登録防止フラグ。失われても「余分に 1 回登録される」だけで整合性には影響しない。
        [System.NonSerialized] bool refreshQueued;
        [System.NonSerialized] bool lodPassQueued;
#endif

        // ================================================================
        //  Lifecycle
        // ================================================================

        void OnEnable()
        {
            if (spline == null)
                spline = new CableSpline();

            CableSpline.AnySplineChanged     += OnAnySplineChanged;
            CableProfile.AnyProfileChanged   += OnAnyProfileChanged;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // 構造変更を伴うので OnEnable 中には行わず次のエディタ更新でまとめて実行
                QueueRefresh();
                return;
            }
#endif
            Refresh();
        }

        void OnDisable()
        {
            CableSpline.AnySplineChanged   -= OnAnySplineChanged;
            CableProfile.AnyProfileChanged -= OnAnyProfileChanged;
        }

        void OnDestroy()
        {
            DestroyOwnedMesh(GetComponent<MeshFilter>(), 0);
            DestroyOwnedMesh(FindLodFilter(1), 1);
            DestroyOwnedMesh(FindLodFilter(2), 2);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // コンポーネントだけが外された場合は、残った一時 LOD 階層と LODGroup を片付ける
                // （GameObject ごと破棄される場合は tf が null になるので何もしない）
                Transform tf = transform;
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (tf == null || tf.GetComponent<CableGenerator>() != null) return;
                    RemoveLodStructure(tf);
                };
            }
#endif
        }

        void OnValidate()
        {
            if (spline == null)
                spline = new CableSpline();

#if UNITY_EDITOR
            if (!CanGenerate()) return;
            QueueRefresh();
#endif
        }

        void OnAnySplineChanged(CableSpline changedSpline, int knotIndex, CableSplineModification modification)
        {
            if (changedSpline != spline || !CanGenerate()) return;
            BuildMeshes();
        }

        void OnAnyProfileChanged(CableProfile changedProfile)
        {
            if (changedProfile != profile || this == null) return;
#if UNITY_EDITOR
            // プロファイルの OnValidate 中に構造変更しないよう遅延
            QueueRefresh();
#else
            Refresh();
#endif
        }

        /// <summary>
        /// 生成対象として扱えるか（Prefab アセットやインポート中のアセットには生成物を作らない）。
        /// </summary>
        bool CanGenerate()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(this)) return false;
#endif
            return true;
        }

        // ================================================================
        //  Public API
        // ================================================================

        /// <summary>
        /// 構造（LOD 階層）とメッシュを即座に再構築する。
        /// OnGUI 中など構造変更を避けたい場面では <see cref="RequestRefresh"/> を使う。
        /// </summary>
        public void RebuildMesh()
        {
            if (!CanGenerate()) return;
            Refresh();
        }

        /// <summary>
        /// 再構築を予約する（エディタでは次の更新でまとめて 1 回だけ実行。再生中は即時）。
        /// </summary>
        public void RequestRefresh()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (CanGenerate()) QueueRefresh();
                return;
            }
#endif
            RebuildMesh();
        }

        public void GetLodScreenHeights(out float lod1, out float lod2, out float cull)
        {
            lod1 = Mathf.Clamp(lod1ScreenHeight, 0.05f, 0.9f);
            lod2 = Mathf.Clamp(lod2ScreenHeight, 0.01f, lod1 - 0.01f);
            cull = Mathf.Clamp(lodCullScreenHeight, 0.001f, lod2 - 0.005f);
        }

        /// <summary>
        /// 書き出し用に、現在の形状を固定したメッシュと中心線サンプルを生成する。
        /// </summary>
        /// <param name="target">書き込み先メッシュ（呼び出し側が所有・破棄する）</param>
        /// <param name="lodTier">0 = フル, 1 = LOD1, 2 = LOD2</param>
        /// <param name="skinBoneCount">2 以上で、始点〜終点を等弧長で分割したボーンチェーン用ウェイトを生成</param>
        /// <param name="centerlineOut">メッシュ生成に使った中心線サンプル（ボーン配置用）。null 可</param>
        /// <param name="boneWeightsOut">頂点ごとのボーンウェイト。null 可</param>
        public bool BuildExportMesh(Mesh target, int lodTier, int skinBoneCount,
            List<CableSamplePoint> centerlineOut, List<BoneWeight> boneWeightsOut)
        {
            centerlineOut?.Clear();
            boneWeightsOut?.Clear();
            if (target == null || !TryGetProfileSource(out var src)) return false;

            int bones = skinBoneCount >= kMinBones ? Mathf.Min(skinBoneCount, kMaxBones) : 0;
            bool ok = BuildTierMesh(target, Mathf.Clamp(lodTier, 0, 2), src, bones, boneWeightsOut);
            if (ok && centerlineOut != null)
                centerlineOut.AddRange(s_samples);
            return ok;
        }

        // ================================================================
        //  Refresh / Build
        // ================================================================

#if UNITY_EDITOR
        void QueueRefresh()
        {
            if (refreshQueued) return;
            refreshQueued = true;
            UnityEditor.EditorApplication.delayCall += DeferredRefresh;
        }

        void DeferredRefresh()
        {
            refreshQueued = false;
            if (this == null || !isActiveAndEnabled || !CanGenerate()) return;
            Refresh();
        }

        void QueueLodPass()
        {
            if (lodPassQueued) return;
            lodPassQueued = true;
            UnityEditor.EditorApplication.delayCall += DeferredLodPass;
        }

        void DeferredLodPass()
        {
            lodPassQueued = false;
            if (this == null || !isActiveAndEnabled || !CanGenerate()) return;

            // ハンドル／スライダーのドラッグ中は遠景用 LOD の更新を見送り、離した時点で 1 回だけ作る
            if (GUIUtility.hotControl != 0)
            {
                QueueLodPass();
                return;
            }
            BuildLodTiers();
        }
#endif

        void Refresh()
        {
            SyncLodStructure();
            BuildMeshes();

            var attachments = GetComponentsInChildren<CableKnotAttachment>(true);
            for (int i = 0; i < attachments.Length; i++)
                attachments[i].UpdateAttachment();
        }

        void BuildMeshes()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null) return;

            Mesh mesh0 = AcquireOwnedMesh(mf, 0);
            if (!TryGetProfileSource(out var src))
            {
                mesh0.Clear();
                ClearLodMeshes();
                return;
            }

            BuildTierMesh(mesh0, 0, src, 0, null);

            if (!enableLod) return;

#if UNITY_EDITOR
            if (!Application.isPlaying && GUIUtility.hotControl != 0)
            {
                QueueLodPass();
                return;
            }
#endif
            BuildLodTiers(src);
        }

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

        // ================================================================
        //  Profile Source (1 回の再構築につき 1 回だけ取得して各 LOD で共有)
        // ================================================================

        readonly struct ProfileSource
        {
            public readonly Vector2[] verts;
            public readonly Vector2[] normals;
            public readonly float[]   us;
            public readonly float[]   perimeters;
            public readonly int       vertsPerLoop;
            public readonly float     maxRadius;
            public readonly float     twistTurnsPerMeter;

            public ProfileSource(Vector2[] verts, Vector2[] normals, float[] us, float[] perimeters,
                int vertsPerLoop, float maxRadius, float twist)
            {
                this.verts = verts; this.normals = normals; this.us = us; this.perimeters = perimeters;
                this.vertsPerLoop = vertsPerLoop; this.maxRadius = maxRadius; twistTurnsPerMeter = twist;
            }
        }

        bool TryGetProfileSource(out ProfileSource src)
        {
            src = default;
            if (spline == null || profile == null || spline.Count < 2) return false;

            Vector2[] verts = profile.GetVertices();
            Vector2[] norms = profile.GetNormals();
            float[]   us    = profile.GetUCoords();
            int       vpl   = Mathf.Max(1, profile.GetVerticesPerLoop());
            float[]   perim = profile.GetPerimeters();

            // カスタムプロファイルの配列長不整合で IndexOutOfRange にならないよう検証
            if (verts == null || norms == null || us == null || perim == null) return false;
            if (verts.Length < 2 || norms.Length != verts.Length || us.Length != verts.Length) return false;
            if (vpl < 2 || verts.Length % vpl != 0) return false;

            float maxR2 = 0f;
            for (int i = 0; i < verts.Length; i++)
                maxR2 = Mathf.Max(maxR2, verts[i].sqrMagnitude);

            float twist = (profile is ITwistedProfile twisted) ? twisted.GetTwistTurnsPerMeter() : 0f;
            src = new ProfileSource(verts, norms, us, perim, vpl, Mathf.Sqrt(maxR2), twist);
            return true;
        }

        // ================================================================
        //  Core Mesh Extrusion (Tier 0 = Full, Tier 1 = LOD1, Tier 2 = LOD2)
        // ================================================================

        bool BuildTierMesh(Mesh targetMesh, int lodTier, in ProfileSource src, int skinBoneCount, List<BoneWeight> weightsOut)
        {
            targetMesh.Clear();
            s_verts.Clear();
            s_tris.Clear();
            s_normals.Clear();
            s_uvs.Clear();
            s_uv2s.Clear();

            float scale = Mathf.Max(0.01f, radiusScale);
            DecimateProfile(src.verts, src.normals, src.us, src.vertsPerLoop, src.perimeters, lodTier, scale,
                out Vector2[] profileVerts, out Vector2[] profileNormals, out float[] profileUs,
                out int vertsPerLoop, out float[] perimeters);

            int profileVertCount = profileVerts.Length;
            if (profileVertCount == 0 || vertsPerLoop < 2) return false;

            float twistTurnsPerMeter = src.twistTurnsPerMeter;
            float splineLength = SampleCenterline(lodTier, twistTurnsPerMeter, src.maxRadius * scale, s_samples);

            int sampleCount = s_samples.Count;
            if (sampleCount < 2 || splineLength < 1e-5f) return false;

            int loopCount = profileVertCount / vertsPerLoop;
            var uv1Layout = CalculateUV1Layout(perimeters, loopCount, splineLength);
            int loopDiv = Mathf.Max(1, vertsPerLoop - 1);

            bool buildWeights = weightsOut != null && skinBoneCount >= kMinBones;

            for (int i = 0; i < sampleCount; i++)
            {
                var sample = s_samples[i];
                Vector3 localPos   = sample.localPos;
                Vector3 localRight = sample.localRight;
                Vector3 localUp    = sample.localUp;

                float dist     = sample.cumulativeLength;
                float normDist = sample.normalizedDistance;
                float v        = dist * uvTiling;

                float twistAngle = twistTurnsPerMeter * (Mathf.PI * 2f) * dist;
                float twistCos = Mathf.Cos(twistAngle);
                float twistSin = Mathf.Sin(twistAngle);

                BoneWeight ringWeight = buildWeights ? EvaluateAnalyticalBoneWeight(normDist, skinBoneCount) : default;

                for (int j = 0; j < profileVertCount; j++)
                {
                    float px = profileVerts[j].x * twistCos - profileVerts[j].y * twistSin;
                    float py = profileVerts[j].x * twistSin + profileVerts[j].y * twistCos;
                    s_verts.Add(localPos + localRight * px + localUp * py);

                    float nx = profileNormals[j].x * twistCos - profileNormals[j].y * twistSin;
                    float ny = profileNormals[j].x * twistSin + profileNormals[j].y * twistCos;
                    s_normals.Add((localRight * nx + localUp * ny).normalized);

                    s_uvs.Add(new Vector2(profileUs[j], v));

                    int loopIdx = j / vertsPerLoop;
                    int localJ  = j % vertsPerLoop;
                    float localU = (localJ == vertsPerLoop - 1)
                        ? (float)(localJ - 1) / loopDiv + 0.5f / loopDiv
                        : (float)localJ / loopDiv;
                    s_uv2s.Add(CalculateUV1(uv1Layout, loopIdx, localU, normDist));

                    if (buildWeights)
                        weightsOut.Add(ringWeight);
                }
            }

            int ringSegments = sampleCount - 1;
            for (int i = 0; i < ringSegments; i++)
            {
                int ringStart     = i * profileVertCount;
                int nextRingStart = (i + 1) * profileVertCount;

                for (int j = 0; j < profileVertCount - 1; j++)
                {
                    if ((j + 1) % vertsPerLoop == 0)
                        continue;

                    int a = ringStart + j;
                    int b = ringStart + j + 1;
                    int c = nextRingStart + j;
                    int d = nextRingStart + j + 1;

                    s_tris.Add(a); s_tris.Add(b); s_tris.Add(c);
                    s_tris.Add(b); s_tris.Add(d); s_tris.Add(c);
                }
            }

            // 太い束線プロファイル × 長いケーブルでは 65535 頂点を超えるため 32bit インデックスに切り替える
            targetMesh.indexFormat = s_verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            targetMesh.SetVertices(s_verts);
            targetMesh.SetNormals(s_normals);
            targetMesh.SetUVs(0, s_uvs);
            targetMesh.SetUVs(1, s_uv2s);
            targetMesh.SetTriangles(s_tris, 0, true);
            return true;
        }

        float SampleCenterline(int lodTier, float twistTurnsPerMeter, float clearance, List<CableSamplePoint> outSamples)
        {
            // ツイストがある場合は直線区間でもねじれが表現されるよう最大ステップ長を設定
            float maxStepLength = 0f;
            if (Mathf.Abs(twistTurnsPerMeter) > 0.01f)
            {
                float stepsPerTurn = lodTier == 0 ? 12f : (lodTier == 1 ? 7f : 4f);
                maxStepLength = 1f / (Mathf.Abs(twistTurnsPerMeter) * stepsPerTurn);
            }

            float tierAngleError = lodTier == 0 ? adaptiveAngleError
                                 : lodTier == 1 ? adaptiveAngleError * 2.2f
                                 :                adaptiveAngleError * 4.5f;

            int tierUniformRes = lodTier == 0 ? resolution
                               : lodTier == 1 ? Mathf.Max(2, resolution / 2)
                               :                Mathf.Max(2, resolution / 4);

            return CableCurveSampler.SampleSpline(
                spline,
                transform,
                useAdaptiveSampling,
                tierUniformRes,
                tierAngleError,
                maxStepLength,
                enableSurfaceCollision,
                clearance,
                collisionLayerMask,
                outSamples);
        }

        // ================================================================
        //  Procedural Profile Decimation for LOD1 / LOD2
        // ================================================================

        public static void GetDecimatedProfileData(
            CableProfile srcProfile,
            int lodTier,
            float scale,
            out Vector2[] outVerts,
            out Vector2[] outNormals,
            out float[] outUs,
            out int outVertsPerLoop,
            out float[] outPerimeters)
        {
            DecimateProfile(
                srcProfile.GetVertices(), srcProfile.GetNormals(), srcProfile.GetUCoords(),
                Mathf.Max(1, srcProfile.GetVerticesPerLoop()), srcProfile.GetPerimeters(),
                lodTier, scale,
                out outVerts, out outNormals, out outUs, out outVertsPerLoop, out outPerimeters);
        }

        static void DecimateProfile(
            Vector2[] rawVerts, Vector2[] rawNorms, float[] rawUs, int rawVpl, float[] rawPerims,
            int lodTier, float scale,
            out Vector2[] outVerts, out Vector2[] outNormals, out float[] outUs,
            out int outVertsPerLoop, out float[] outPerimeters)
        {
            int loopCount = rawVerts.Length / rawVpl;
            int rawSegmentsPerLoop = rawVpl - 1;

            // LOD0 または頂点数が少ない断面は間引きせずスケールのみ適用
            if (lodTier <= 0 || rawSegmentsPerLoop <= 4)
            {
                outVerts = new Vector2[rawVerts.Length];
                for (int i = 0; i < rawVerts.Length; i++)
                    outVerts[i] = rawVerts[i] * scale;
                outNormals = rawNorms;
                outUs = rawUs;
                outVertsPerLoop = rawVpl;
                outPerimeters = new float[rawPerims.Length];
                for (int i = 0; i < rawPerims.Length; i++)
                    outPerimeters[i] = rawPerims[i] * scale;
                return;
            }

            // LOD1: 円周分割数を約 50% (最低4角)、LOD2: 約 30% (最低3角) に手続き間引き
            int targetSegments = lodTier == 1
                ? Mathf.Max(4, Mathf.RoundToInt(rawSegmentsPerLoop * 0.5f))
                : Mathf.Max(3, Mathf.RoundToInt(rawSegmentsPerLoop * 0.3f));

            targetSegments = Mathf.Min(targetSegments, rawSegmentsPerLoop);
            outVertsPerLoop = targetSegments + 1;

            int totalOut = loopCount * outVertsPerLoop;
            outVerts = new Vector2[totalOut];
            outNormals = new Vector2[totalOut];
            outUs = new float[totalOut];
            outPerimeters = new float[loopCount];

            for (int loop = 0; loop < loopCount; loop++)
            {
                int srcBase = loop * rawVpl;
                int dstBase = loop * outVertsPerLoop;

                for (int k = 0; k < targetSegments; k++)
                {
                    int srcIdx = Mathf.Clamp(Mathf.RoundToInt((float)k / targetSegments * rawSegmentsPerLoop), 0, rawSegmentsPerLoop - 1);
                    outVerts[dstBase + k]   = rawVerts[srcBase + srcIdx] * scale;
                    outNormals[dstBase + k] = rawNorms[srcBase + srcIdx];
                    outUs[dstBase + k]      = rawUs[srcBase + srcIdx];
                }

                // 閉じ頂点（末尾）
                outVerts[dstBase + targetSegments]   = rawVerts[srcBase + rawSegmentsPerLoop] * scale;
                outNormals[dstBase + targetSegments] = rawNorms[srcBase + rawSegmentsPerLoop];
                outUs[dstBase + targetSegments]      = rawUs[srcBase + rawSegmentsPerLoop];

                float p = 0f;
                for (int k = 1; k < outVertsPerLoop; k++)
                    p += Vector2.Distance(outVerts[dstBase + k], outVerts[dstBase + k - 1]);
                outPerimeters[loop] = p;
            }
        }

        // ================================================================
        //  Analytical Smooth Skinning Weights (FBX 書き出し用)
        // ================================================================

        public static BoneWeight EvaluateAnalyticalBoneWeight(float normalizedDistance, int numBones)
        {
            if (numBones <= 1)
                return new BoneWeight { boneIndex0 = 0, weight0 = 1f };

            float b = Mathf.Clamp01(normalizedDistance) * (numBones - 1);
            int idx0 = Mathf.Clamp(Mathf.FloorToInt(b), 0, numBones - 1);
            int idx1 = Mathf.Min(idx0 + 1, numBones - 1);

            if (idx0 == idx1)
                return new BoneWeight { boneIndex0 = idx0, weight0 = 1f };

            float f = Mathf.Clamp01(b - idx0);
            // Hermite SmoothStep 補間により関節の折れ曲がりを滑らかにする
            float w1 = f * f * (3f - 2f * f);
            float w0 = 1f - w1;

            return new BoneWeight
            {
                boneIndex0 = idx0,
                weight0    = w0,
                boneIndex1 = idx1,
                weight1    = w1,
            };
        }

        // ================================================================
        //  UV1 Shelf Packing (V1 完全継承)
        // ================================================================

        struct StripInfo
        {
            public int loopIndex;
            public int splitIndex;
            public int splitCount;
            public float realWidth;
            public float realHeight;
            public float packU;
            public float packV;
            public float packW;
            public float packH;
        }

        struct UV1Layout
        {
            public StripInfo[] strips;
            public int[] loopStripStart;
            public int[] loopSplitCount;
        }

        const float Padding = 0.01f;

        static UV1Layout CalculateUV1Layout(float[] perimeters, int loopCount, float splineLength)
        {
            var layout = new UV1Layout
            {
                loopStripStart = new int[loopCount],
                loopSplitCount = new int[loopCount]
            };

            var stripList = new List<StripInfo>();

            for (int loop = 0; loop < loopCount; loop++)
            {
                float perimeter = (loop < perimeters.Length) ? perimeters[loop] : 0.01f;
                if (perimeter < 0.0001f) perimeter = 0.0001f;

                int splits = 1;
                layout.loopStripStart[loop] = stripList.Count;
                layout.loopSplitCount[loop] = splits;

                float stripHeight = splineLength / splits;

                for (int s = 0; s < splits; s++)
                {
                    stripList.Add(new StripInfo
                    {
                        loopIndex = loop,
                        splitIndex = s,
                        splitCount = splits,
                        realWidth = perimeter,
                        realHeight = stripHeight,
                    });
                }
            }

            var strips = stripList.ToArray();
            if (strips.Length == 0)
            {
                layout.strips = strips;
                return layout;
            }

            var sortedIndices = new int[strips.Length];
            for (int i = 0; i < sortedIndices.Length; i++) sortedIndices[i] = i;
            System.Array.Sort(sortedIndices, (a, b) =>
                strips[b].realHeight.CompareTo(strips[a].realHeight));

            float totalArea = 0f;
            for (int i = 0; i < strips.Length; i++)
                totalArea += strips[i].realWidth * strips[i].realHeight;

            float scaleFactor = totalArea > 1e-8f ? 1f / Mathf.Sqrt(totalArea) : 1f;
            float bestScale = scaleFactor;

            for (int attempt = 0; attempt < 16; attempt++)
            {
                if (TryShelfPack(strips, sortedIndices, bestScale, Padding, out _, out _))
                    break;
                bestScale *= 0.85f;
            }

            TryShelfPack(strips, sortedIndices, bestScale, Padding, out float usedW, out float usedH);

            float fillScale = 1f;
            if (usedW > 0.0001f && usedH > 0.0001f)
                fillScale = Mathf.Min((1f - Padding) / usedW, (1f - Padding) / usedH);

            for (int i = 0; i < strips.Length; i++)
            {
                var s = strips[i];
                s.packU = s.packU * fillScale + Padding * 0.5f;
                s.packV = s.packV * fillScale + Padding * 0.5f;
                s.packW *= fillScale;
                s.packH *= fillScale;
                strips[i] = s;
            }

            layout.strips = strips;
            return layout;
        }

        static bool TryShelfPack(StripInfo[] strips, int[] sortedIndices, float scale, float pad,
            out float usedWidth, out float usedHeight)
        {
            float cursorU = pad;
            float cursorV = pad;
            float shelfHeight = 0f;
            float maxU = 0f;

            for (int si = 0; si < sortedIndices.Length; si++)
            {
                int idx = sortedIndices[si];
                float w = strips[idx].realWidth * scale;
                float h = strips[idx].realHeight * scale;

                if (cursorU + w + pad > 1f)
                {
                    cursorU = pad;
                    cursorV += shelfHeight + pad;
                    shelfHeight = 0f;
                }

                var s = strips[idx];
                s.packU = cursorU;
                s.packV = cursorV;
                s.packW = w;
                s.packH = h;
                strips[idx] = s;

                cursorU += w + pad;
                if (h > shelfHeight) shelfHeight = h;
                if (cursorU > maxU) maxU = cursorU;
            }

            float maxV = cursorV + shelfHeight + pad;
            usedWidth = maxU;
            usedHeight = maxV;

            return maxV <= 1f;
        }

        static Vector2 CalculateUV1(UV1Layout layout, int loopIdx, float localU, float t)
        {
            if (layout.strips == null || layout.strips.Length == 0)
                return new Vector2(localU, t);

            int splitCount = layout.loopSplitCount[loopIdx];
            int stripStart = layout.loopStripStart[loopIdx];

            int splitIdx = Mathf.FloorToInt(t * splitCount);
            if (splitIdx >= splitCount) splitIdx = splitCount - 1;

            int stripIdx = stripStart + splitIdx;
            var strip = layout.strips[stripIdx];
            float splitT = t * splitCount - splitIdx;

            return new Vector2(
                strip.packU + localU * strip.packW,
                strip.packV + splitT * strip.packH
            );
        }
    }
}

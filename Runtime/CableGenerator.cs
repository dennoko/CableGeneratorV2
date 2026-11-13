using System.Collections.Generic;
using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// スプラインと断面プロファイルからケーブルメッシュを生成するコンポーネント。
    ///
    /// 【ライフサイクル設計】
    /// ・生成物（メッシュ・LOD 子階層）は入力（スプライン・設定）と一緒にシーンへ直接シリアライズされる。
    ///   そのためベイクしなくてもワールドのビルドに含まれ、このスクリプトが動かない環境でも表示される。
    ///   生成物はいつでも入力から再生成でき、シーンを開いたときにも作り直して保存済みの内容と照合する。
    /// ・生成メッシュは MeshFilter の参照そのものを正とし、所有者は実行時の台帳で判定する（複製時の共有防止）。
    ///   C# 側のフィールドにキャッシュしないため、ドメインリロードで台帳を失っても再利用・再生成が破綻しない。
    /// ・コンポーネントが外されても生成物は消さない（その時点の形状のまま残る）。
    /// ・GameObject / コンポーネントの生成・破棄（構造変更）は OnValidate / OnEnable / OnGUI 中に行わず、
    ///   EditorApplication.delayCall で 1 回にまとめて実行する。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [ExecuteAlways]
    public partial class CableGenerator : MonoBehaviour
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
                ConfirmSavedContent(mf, mesh0);
                ClearLodMeshes();
                return;
            }

            BuildTierMesh(mesh0, 0, src, 0, null);
            ConfirmSavedContent(mf, mesh0);

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
    }
}

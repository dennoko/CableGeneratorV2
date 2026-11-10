using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    [CustomEditor(typeof(CableGenerator))]
    public partial class CableGeneratorInspector : Editor
    {
        // ---- Serialized Properties ----
        SerializedProperty profileProp;
        SerializedProperty radiusScaleProp;
        SerializedProperty useAdaptiveSamplingProp;
        SerializedProperty adaptiveAngleErrorProp;
        SerializedProperty resolutionProp;
        SerializedProperty uvTilingProp;
        SerializedProperty enableSurfaceCollisionProp;
        SerializedProperty collisionLayerMaskProp;
        SerializedProperty enableLodProp;
        SerializedProperty lod1ScreenHeightProp;
        SerializedProperty lod2ScreenHeightProp;
        SerializedProperty lodCullScreenHeightProp;
        SerializedProperty boneCountProp;

        const string kPrefKeyBakeFolder = "CableGeneratorV2_BakeFolderPath";
        const string kPrefKeyGlobalCoords = "CableGeneratorV2_UseGlobalKnotCoords";
        string bakeFolderPath = "";

        static bool UseGlobalKnotCoords
        {
            get => EditorPrefs.GetBool(kPrefKeyGlobalCoords, true);
            set => EditorPrefs.SetBool(kPrefKeyGlobalCoords, value);
        }

        // ---- Picking Mode ----
        internal static CableGenerator s_pickingTarget = null;
        static int            s_pickCount     = 0;
        static Vector3[]      s_pickedPoints  = new Vector3[2];
        static Vector3[]      s_pickedNormals = new Vector3[2];
        static float          s_tangentScale  = 1f;

        // ---- Knot Selection & Projection Settings ----
        public static int       s_snapKnotIndex        = 0;
        public static readonly HashSet<int> s_selectedKnotIndices = new HashSet<int>();
        public static Vector3   s_snapDirection        = Vector3.down;
        public static bool      s_snapDirectionIsLocal = false;
        public static float     s_snapMaxDistance      = 10f;
        public static float     s_snapSurfaceOffset    = 0.003f;
        public static LayerMask s_snapLayerMask        = ~0;
        public static string    s_snapLastResult       = string.Empty;
        static int              s_addedKnotModeIndex   = 0;
        static int              s_initialDivisionCount = 4;
        internal static string           s_knotInitLastResult   = string.Empty;

        // ---- FBX Export Settings ----
        static readonly string[] kFbxLodLabels = { "LOD0 (フル)", "LOD1 (約50%)", "LOD2 (約30%)" };
        static int  s_fbxLodTier      = 0;
        static bool s_fbxIncludeBones = true;

        // ---- Cable Sag Settings ----
        static CableGenerator s_sagTarget;
        static float     s_sagDropDistance  = 0.5f;
        static float     s_sagHandleLength  = 0.5f;
        static string    s_sagLastResult    = string.Empty;
        static bool      s_hasSagKnots      = false;
        static bool      s_sagUseMirrored   = false;
        static int       s_sagOriginalCount = 0;
        static bool      s_sagWasClosed     = false;
        static Vector3[] s_sagBasePositions = null;

        // ---- Section Fold States ----
        static bool s_foldKnotList        = true;
        static bool s_foldKnotDetail      = false;
        static bool s_foldLodAndBones     = true;
        static bool s_foldSplineSetup     = false;
        static bool s_foldSurfaceDraw     = false;
        static bool s_foldCableSag        = false;
        static bool s_foldKnotProjection  = false;
        static bool s_foldKnotSubdivision = false;
        static bool s_foldAttachments     = false;
        static bool s_foldExport          = false;

        const float kVectorEpsilon          = 0.000001f;
        const float kVectorEpsilonSqr       = kVectorEpsilon * kVectorEpsilon;
        const float kMidpointTangentDivisor = 6f;
        const float kLinearTangentDivisor   = 3f;

        static readonly string[] kTangentModeLabels = { "自動", "スムーズ", "コーナー" };

        static CableTangentMode AddedKnotTangentMode() => s_addedKnotModeIndex switch
        {
            1 => CableTangentMode.Mirrored,
            2 => CableTangentMode.Broken,
            _ => CableTangentMode.AutoSmooth,
        };

        // Rate-slider state
        float  s_rateSliderValue    = 0f;
        string s_activeRateSliderId = null;
        double s_lastTime           = 0;

        bool DrawRateSlider(string label, string id, ref float lengthValue)
        {
            bool changed = false;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, CableGeneratorTheme.CaptionStyle, GUILayout.Width(130));

            float val = s_activeRateSliderId == id ? s_rateSliderValue : 0f;

            EditorGUI.BeginChangeCheck();
            val = EditorGUILayout.Slider(GUIContent.none, val, -1f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                if (s_activeRateSliderId != id)
                {
                    s_activeRateSliderId = id;
                    s_lastTime = EditorApplication.timeSinceStartup;
                }
                s_rateSliderValue = val;
            }

            if (s_activeRateSliderId == id)
            {
                if (GUIUtility.hotControl == 0)
                {
                    s_activeRateSliderId = null;
                    s_rateSliderValue    = 0f;
                }
                else
                {
                    double time = EditorApplication.timeSinceStartup;
                    float  dt   = (float)(time - s_lastTime);
                    if (dt > 0.1f) dt = 0.016f;

                    if (Mathf.Abs(s_rateSliderValue) > 0.001f)
                    {
                        lengthValue *= Mathf.Pow(12f, s_rateSliderValue * dt);
                        if (lengthValue < 0.01f) lengthValue = 0.01f;
                        changed = true;
                    }
                    s_lastTime = time;
                    Repaint();
                }
            }

            GUILayout.Label($"{lengthValue:F2} m", CableGeneratorTheme.CaptionStyle, GUILayout.Width(45));
            EditorGUILayout.EndHorizontal();

            return changed;
        }

        // ================================================================
        //  Lifecycle
        // ================================================================

        void OnEnable()
        {
            profileProp                = serializedObject.FindProperty("profile");
            radiusScaleProp            = serializedObject.FindProperty("radiusScale");
            useAdaptiveSamplingProp    = serializedObject.FindProperty("useAdaptiveSampling");
            adaptiveAngleErrorProp     = serializedObject.FindProperty("adaptiveAngleError");
            resolutionProp             = serializedObject.FindProperty("resolution");
            uvTilingProp               = serializedObject.FindProperty("uvTiling");
            enableSurfaceCollisionProp = serializedObject.FindProperty("enableSurfaceCollision");
            collisionLayerMaskProp     = serializedObject.FindProperty("collisionLayerMask");
            enableLodProp              = serializedObject.FindProperty("enableLod");
            lod1ScreenHeightProp       = serializedObject.FindProperty("lod1ScreenHeight");
            lod2ScreenHeightProp       = serializedObject.FindProperty("lod2ScreenHeight");
            lodCullScreenHeightProp    = serializedObject.FindProperty("lodCullScreenHeight");
            boneCountProp              = serializedObject.FindProperty("boneCount");

            bakeFolderPath = EditorPrefs.GetString(kPrefKeyBakeFolder, "");

            // シーンのツールバー (S1) に操作対象を知らせる
            var current = target as CableGenerator;
            if (current != null && !EditorUtility.IsPersistent(current))
            {
                CableSceneToolState.SetTarget(current);
                if (current.Spline != null) ValidateKnotSelection(current, current.Spline.Count);
            }

            // プロファイル未設定ならデフォルトを割り当てる。
            // アセット生成を伴うので Editor.OnEnable 中には行わず遅延し、Prefab アセットには適用しない。
            var gen = target as CableGenerator;
            if (gen != null && gen.profile == null && !EditorUtility.IsPersistent(gen))
            {
                EditorApplication.delayCall += () =>
                {
                    if (gen == null || gen.profile != null) return;
                    var defaultProfile = CableGeneratorCreationMenu.FindOrCreateDefaultProfile();
                    if (defaultProfile == null) return;
                    gen.profile = defaultProfile;
                    EditorUtility.SetDirty(gen);
                    gen.RequestRefresh();
                };
            }
        }

        void OnDisable()
        {
            if (s_pickingTarget != null && s_pickingTarget == target as CableGenerator)
                CancelPickingMode();
            if (s_drawTarget != null && s_drawTarget == target as CableGenerator)
                CancelDrawMode();
            CableSceneToolState.ClearTarget(target as CableGenerator);
        }

        // ================================================================
        //  Inspector GUI
        // ================================================================

        public override void OnInspectorGUI()
        {
            CableGeneratorTheme.Initialize();

            serializedObject.Update();
            var generator = (CableGenerator)target;
            var spline    = generator.Spline;

            EditorGUILayout.BeginVertical(CableGeneratorTheme.InspectorRootStyle);

            // ---- 1. ステータス＆クイック操作バー ----
            DrawStatsAndQuickActionsCard(generator, spline);

            // ---- 2. 断面プロファイル＆メッシュ分割設定 ----
            DrawSection("断面プロファイル & メッシュ最適化", () => DrawProfileAndMeshSection(generator));

            // ---- 3. 軽量化 LOD ----
            DrawFoldableSection("軽量化 LOD", ref s_foldLodAndBones, () => DrawLodSection(generator));

            // ---- 4. 制御点リスト ----
            DrawFoldableSection($"制御点リスト ({spline.Count} 点)", ref s_foldKnotList, () => DrawKnotListContents(generator, spline));

            // ---- 5. 選択ノットの詳細 ----
            DrawFoldableSection($"選択ノット [{Mathf.Clamp(s_snapKnotIndex, 0, Mathf.Max(0, spline.Count - 1))}] の詳細設定", ref s_foldKnotDetail, () => DrawSelectedKnotDetailContents(generator, spline));

            // ---- 6. 2点選択でスプライン配線（コライダー付与不要） ----
            DrawFoldableSection("2点選択でスプライン配線 (コライダー不要)", ref s_foldSplineSetup, () => DrawSplineSetupSection(generator));

            // ---- 7. サーフェスに描いて配線 ----
            DrawFoldableSection("サーフェスに描いて配線", ref s_foldSurfaceDraw, () => DrawSurfaceDrawSection(generator));

            // ---- 8. ケーブルたわみ設定 ----
            DrawFoldableSection("ケーブルたわみ設定", ref s_foldCableSag, () => DrawCableSagSection(generator, spline));

            // ---- 9. ノットを面にスナップ配置 ----
            DrawFoldableSection("ノットを面にスナップ配置", ref s_foldKnotProjection, () => DrawKnotProjectionSection(generator));

            // ---- 10. ノットの細分化・等分 ----
            DrawFoldableSection("ノットの細分化・等分", ref s_foldKnotSubdivision, () => DrawKnotSubdivisionSection(generator));

            // ---- 11. アタッチメント ----
            if (spline != null && spline.Count > 0)
            {
                DrawFoldableSection("アタッチメント (制御点へのオブジェクト追従)", ref s_foldAttachments, () => DrawAttachmentsSection(generator, spline));
            }

            // ---- 12. エクスポート (Bake / FBX) ----
            DrawFoldableSection("エクスポート (Bake / FBX)", ref s_foldExport, () => DrawExportSection(generator));

            GUILayout.EndVertical();
        }
    }
}


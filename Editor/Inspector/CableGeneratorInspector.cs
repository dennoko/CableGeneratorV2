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
        static string           s_knotInitLastResult   = string.Empty;

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

            // プロファイル未設定ならデフォルトを割り当てる。
            // アセット生成を伴うので Editor.OnEnable 中には行わず遅延し、Prefab アセットには適用しない。
            var gen = target as CableGenerator;
            if (gen != null && gen.profile == null && !EditorUtility.IsPersistent(gen))
            {
                EditorApplication.delayCall += () =>
                {
                    if (gen == null || gen.profile != null) return;
                    var defaultProfile = FindOrCreateDefaultProfile();
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
            DrawSection("断面プロファイル & メッシュ最適化", () =>
            {
                EditorGUI.BeginChangeCheck();

                EditorGUILayout.PropertyField(profileProp,     new GUIContent("断面プロファイル"));
                EditorGUILayout.PropertyField(radiusScaleProp, new GUIContent("太さ倍率"));
                EditorGUILayout.PropertyField(uvTilingProp,    new GUIContent("UVタイリング"));

                GUILayout.Space(4);

                EditorGUILayout.PropertyField(useAdaptiveSamplingProp,
                    new GUIContent("曲率適応型分割 (軽量)", "ONのとき直線部の頂点を節約し、カーブ部のみ密に分割します"));

                if (useAdaptiveSamplingProp.boolValue)
                {
                    EditorGUILayout.PropertyField(adaptiveAngleErrorProp, new GUIContent("カーブ許容角 (°)"));
                }
                else
                {
                    EditorGUILayout.PropertyField(resolutionProp, new GUIContent("均等分割数"));
                }

                GUILayout.Space(4);
                EditorGUILayout.PropertyField(enableSurfaceCollisionProp, new GUIContent("コライダー自動接地 (床判定)"));
                if (enableSurfaceCollisionProp.boolValue)
                {
                    EditorGUILayout.PropertyField(collisionLayerMaskProp, new GUIContent("接地 LayerMask"));
                }

                // ApplyModifiedProperties が OnValidate を呼び、再構築は次の更新で 1 回だけ行われる
                if (EditorGUI.EndChangeCheck())
                    serializedObject.ApplyModifiedProperties();

                GUILayout.Space(6);
                if (GUILayout.Button(" メッシュを再生成 ", CableGeneratorTheme.ActionButtonStyle))
                    generator.RequestRefresh();
            });

            // ---- 3. 軽量化 LOD ----
            DrawFoldableSection("軽量化 LOD", ref s_foldLodAndBones, () =>
            {
                EditorGUI.BeginChangeCheck();

                EditorGUILayout.PropertyField(enableLodProp, new GUIContent("⚡ 自動 LOD 生成 (LODGroup)"));
                if (enableLodProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(lod1ScreenHeightProp,    new GUIContent("LOD1 切替画面比"));
                    EditorGUILayout.PropertyField(lod2ScreenHeightProp,    new GUIContent("LOD2 切替画面比"));
                    EditorGUILayout.PropertyField(lodCullScreenHeightProp, new GUIContent("カリング画面比"));

                    GUILayout.Label($"ポリゴン数: LOD0 = {TriangleCount(generator.GeneratedMesh)}△  /  " +
                                    $"LOD1 = {TriangleCount(generator.GeneratedLod1Mesh)}△  /  " +
                                    $"LOD2 = {TriangleCount(generator.GeneratedLod2Mesh)}△", CableGeneratorTheme.CaptionStyle);
                    EditorGUI.indentLevel--;
                }

                if (EditorGUI.EndChangeCheck())
                    serializedObject.ApplyModifiedProperties();

                DrawLegacyBonesNotice(generator);
            });

            // ---- 4. 制御点リスト ----
            DrawFoldableSection($"制御点リスト ({spline.Count} 点)", ref s_foldKnotList, () =>
            {
                DrawKnotListContents(generator, spline);
            });

            // ---- 5. 選択ノットの詳細 ----
            DrawFoldableSection($"選択ノット [{Mathf.Clamp(s_snapKnotIndex, 0, Mathf.Max(0, spline.Count - 1))}] の詳細設定", ref s_foldKnotDetail, () =>
            {
                DrawSelectedKnotDetailContents(generator, spline);
            });

            // ---- 6. 2点選択でスプライン配線（コライダー付与不要） ----
            DrawFoldableSection("2点選択でスプライン配線 (コライダー不要)", ref s_foldSplineSetup, () =>
            {
                EditorGUI.BeginChangeCheck();
                bool sliding1 = DrawRateSlider("ハンドルの長さ", "Len_TangentScale", ref s_tangentScale);
                if (EditorGUI.EndChangeCheck() || sliding1)
                    ApplyTangentScaleToSpline(generator, s_tangentScale);

                GUILayout.Space(4);

                bool isMyTarget = s_pickingTarget == generator;
                if (!isMyTarget)
                {
                    EditorGUI.BeginDisabledGroup(s_pickingTarget != null || s_drawTarget != null);
                    if (GUILayout.Button("2点選択でSplineを設定 (メッシュ直接判定)", CableGeneratorTheme.SecondaryButtonStyle))
                        StartPickingMode(generator);
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    string hint = s_pickCount == 0
                        ? "シーンで 1点目 をクリックしてください"
                        : "シーンで 2点目 をクリックしてください";
                    EditorGUILayout.HelpBox(hint, MessageType.Info);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("やり直し", CableGeneratorTheme.SecondaryButtonStyle))
                        s_pickCount = 0;
                    if (GUILayout.Button("キャンセル", CableGeneratorTheme.SecondaryButtonStyle))
                        CancelPickingMode();
                    EditorGUILayout.EndHorizontal();
                }
            });

            // ---- 7. サーフェスに描いて配線 ----
            DrawFoldableSection("サーフェスに描いて配線", ref s_foldSurfaceDraw, () =>
                DrawSurfaceDrawSection(generator));

            // ---- 8. ケーブルたわみ設定 ----
            DrawFoldableSection("ケーブルたわみ設定", ref s_foldCableSag, () =>
            {
                // 直接区間 sag の一括スライダー
                float firstSag = spline.Count >= 2 ? spline.GetSegmentSag(0) : 0f;
                EditorGUI.BeginChangeCheck();
                float newDirectSag = EditorGUILayout.Slider("直接たわみ量 (Scene ▼連動)", firstSag, 0f, 3f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(generator, "Adjust Direct Segment Sag");
                    for (int i = 0; i < spline.SegmentCount; i++)
                        spline.SetSegmentSag(i, newDirectSag, false);
                    spline.NotifyChanged(-1, CableSplineModification.KnotModified);
                    EditorUtility.SetDirty(generator);
                    SceneView.RepaintAll();
                }

                GUILayout.Space(6);

                if (GUILayout.Button("たわみノットを挿入 (V1互換・中点ノット追加)", CableGeneratorTheme.SecondaryButtonStyle))
                    InsertSagKnots(generator);

                if (!string.IsNullOrEmpty(s_sagLastResult))
                    GUILayout.Label(s_sagLastResult, CableGeneratorTheme.CaptionStyle);

                if (s_hasSagKnots)
                {
                    GUILayout.Space(4);
                    EditorGUI.BeginChangeCheck();
                    s_sagDropDistance = EditorGUILayout.Slider("ノット降下距離", s_sagDropDistance, 0f, 5f);
                    if (EditorGUI.EndChangeCheck())
                        UpdateSagKnots(generator, s_sagDropDistance, s_sagHandleLength);

                    EditorGUI.BeginChangeCheck();
                    s_sagUseMirrored = EditorGUILayout.Toggle("Mirrored ハンドル", s_sagUseMirrored);
                    if (EditorGUI.EndChangeCheck())
                        UpdateSagKnots(generator, s_sagDropDistance, s_sagHandleLength);

                    EditorGUI.BeginDisabledGroup(!s_sagUseMirrored);
                    EditorGUI.BeginChangeCheck();
                    bool sliding2 = DrawRateSlider("ハンドルの長さ", "Len_SagHandle", ref s_sagHandleLength);
                    if (EditorGUI.EndChangeCheck() || sliding2)
                        UpdateSagKnots(generator, s_sagDropDistance, s_sagHandleLength);
                    EditorGUI.EndDisabledGroup();
                }
            });

            // ---- 9. ノットを面にスナップ配置 ----
            DrawFoldableSection("ノットを面にスナップ配置", ref s_foldKnotProjection, () =>
            {
                EditorGUI.BeginChangeCheck();
                s_snapKnotIndex = Mathf.Max(0, EditorGUILayout.IntField("対象ノット Index", s_snapKnotIndex));
                if (EditorGUI.EndChangeCheck())
                    s_selectedKnotIndices.Clear();

                if (s_selectedKnotIndices.Count > 0)
                {
                    var sorted = new List<int>(s_selectedKnotIndices);
                    sorted.Sort();
                    GUILayout.Label($"複数選択中: {string.Join(", ", sorted)}", CableGeneratorTheme.CaptionStyle);
                }
                else
                {
                    GUILayout.Label("Shiftキー+クリックで複数選択", CableGeneratorTheme.CaptionStyle);
                }

                s_snapDirection        = EditorGUILayout.Vector3Field("投影方向", s_snapDirection);
                s_snapDirectionIsLocal = EditorGUILayout.Toggle("方向をローカル扱い", s_snapDirectionIsLocal);
                s_snapMaxDistance      = Mathf.Max(0f, EditorGUILayout.FloatField("最大距離", s_snapMaxDistance));
                s_snapSurfaceOffset    = Mathf.Max(0f, EditorGUILayout.FloatField("面オフセット", s_snapSurfaceOffset));
                s_snapLayerMask        = DrawLayerMaskField("LayerMask", s_snapLayerMask);

                GUILayout.Space(6);

                string snapButtonLabel = s_selectedKnotIndices.Count > 0
                    ? $"選択した {s_selectedKnotIndices.Count} ノットを面へ投影"
                    : "指定ノットを面へ投影";
                if (GUILayout.Button(snapButtonLabel, CableGeneratorTheme.SecondaryButtonStyle))
                {
                    bool ok = SnapKnotInDirection(generator);
                    if (!ok && string.IsNullOrEmpty(s_snapLastResult))
                        s_snapLastResult = "投影に失敗しました。";
                }

                if (!string.IsNullOrEmpty(s_snapLastResult))
                    GUILayout.Label(s_snapLastResult, CableGeneratorTheme.CaptionStyle);
            });

            // ---- 10. ノットの細分化・等分 ----
            DrawFoldableSection("ノットの細分化・等分", ref s_foldKnotSubdivision, () =>
            {
                s_addedKnotModeIndex   = EditorGUILayout.Popup("追加ノットモード", s_addedKnotModeIndex, kTangentModeLabels);
                s_initialDivisionCount = Mathf.Max(1, EditorGUILayout.IntField("始点-終点 分割数", s_initialDivisionCount));

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("始点-終点を等分して再配置", CableGeneratorTheme.SecondaryButtonStyle))
                    RedistributeKnotsBetweenEndpoints(generator, s_initialDivisionCount, AddedKnotTangentMode());
                if (GUILayout.Button("全区間を細分化して追加", CableGeneratorTheme.SecondaryButtonStyle))
                    SubdivideSplineKnots(generator, AddedKnotTangentMode());
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(s_knotInitLastResult))
                    GUILayout.Label(s_knotInitLastResult, CableGeneratorTheme.CaptionStyle);
            });

            // ---- 11. アタッチメント ----
            if (spline != null && spline.Count > 0)
            {
                DrawFoldableSection("アタッチメント (制御点へのオブジェクト追従)", ref s_foldAttachments, () =>
                {
                    int knotCount = spline.Count;
                    for (int i = 0; i < knotCount; i++)
                    {
                        if (GUILayout.Button($"ノット {i} にアタッチメントを追加", CableGeneratorTheme.SecondaryButtonStyle))
                            AddAttachment(generator, i);
                        if (i < knotCount - 1) GUILayout.Space(2);
                    }
                });
            }

            // ---- 12. エクスポート (Bake / FBX) ----
            DrawFoldableSection("エクスポート (Bake / FBX)", ref s_foldExport, () =>
            {
                var currentMesh = generator.GeneratedMesh;
                bool hasMesh = currentMesh != null && currentMesh.vertexCount > 0;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("保存先フォルダ");
                EditorGUI.BeginChangeCheck();
                bakeFolderPath = EditorGUILayout.TextField(bakeFolderPath);
                if (GUILayout.Button("...", CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(28)))
                {
                    string selected = EditorUtility.OpenFolderPanel("保存先フォルダを選択", "Assets", "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        string dataPath = Application.dataPath.Replace("\\", "/");
                        selected = selected.Replace("\\", "/");
                        if (selected.StartsWith(dataPath))
                        {
                            bakeFolderPath = "Assets" + selected.Substring(dataPath.Length);
                            GUI.changed = true;
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("エラー", "Assetsフォルダ内を選択してください。", "OK");
                        }
                    }
                }
                if (EditorGUI.EndChangeCheck())
                    EditorPrefs.SetString(kPrefKeyBakeFolder, bakeFolderPath);
                EditorGUILayout.EndHorizontal();

                if (string.IsNullOrEmpty(bakeFolderPath))
                    GUILayout.Label($"未設定の場合: {CableMeshExporter.DefaultOutputFolder}", CableGeneratorTheme.CaptionStyle);

                GUILayout.Space(6);

                EditorGUI.BeginDisabledGroup(!hasMesh);
                if (GUILayout.Button("メッシュを保存 (.asset) & ベイクオブジェクト生成", CableGeneratorTheme.ActionButtonStyle))
                {
                    BakeCableToSceneAndAssets(generator, bakeFolderPath);
                    GUIUtility.ExitGUI();
                }
                EditorGUI.EndDisabledGroup();

                // ---- FBX (形状固定 + ボーン + ウェイト) ----
                GUILayout.Space(10);
                GUILayout.Label("FBX 書き出し（現在の形状を固定）", CableGeneratorTheme.SecondaryTextStyle);

                s_fbxLodTier = EditorGUILayout.Popup("書き出す LOD", s_fbxLodTier, kFbxLodLabels);
                s_fbxIncludeBones = EditorGUILayout.Toggle(
                    new GUIContent("🦴 ボーン＆ウェイトを付ける", "始点〜終点を等弧長で分割したボーンチェーンとスムーズウェイトを含めます（PhysBone 素体）"),
                    s_fbxIncludeBones);

                if (s_fbxIncludeBones)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(boneCountProp, new GUIContent("ボーン本数"));
                    if (EditorGUI.EndChangeCheck())
                        serializedObject.ApplyModifiedProperties();
                    GUILayout.Label("Bone_0 が始点です。PhysBone は Bone_0 に付けてください。", CableGeneratorTheme.CaptionStyle);
                }

                GUILayout.Space(4);
                EditorGUI.BeginDisabledGroup(!hasMesh);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("FBX を書き出してシーンに配置", CableGeneratorTheme.ActionButtonStyle))
                {
                    ExportCableFbx(generator, bakeFolderPath, s_fbxLodTier, s_fbxIncludeBones, true);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("FBX のみ書き出し", CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(120)))
                {
                    ExportCableFbx(generator, bakeFolderPath, s_fbxLodTier, s_fbxIncludeBones, false);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.EndDisabledGroup();
            });

            GUILayout.EndVertical();
        }

        // ================================================================
        //  Stats Bar & Quick Actions
        // ================================================================

        void DrawStatsAndQuickActionsCard(CableGenerator generator, CableSpline spline)
        {
            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"制御点: {spline.Count}", CableGeneratorTheme.SecondaryTextStyle, GUILayout.Width(68));

            float length = spline.CalculateLength(generator.transform.localToWorldMatrix,
                generator.transform.InverseTransformVector(Vector3.down));
            GUILayout.Label($"全長: {length:F2} m", CableGeneratorTheme.CaptionStyle, GUILayout.Width(84));

            GUILayout.Label($"{TriangleCount(generator.GeneratedMesh)} △", CableGeneratorTheme.CaptionStyle);

            GUILayout.FlexibleSpace();

            bool wasClosed = spline.Closed;
            EditorGUI.BeginChangeCheck();
            bool isClosed = EditorGUILayout.ToggleLeft("ループ", wasClosed, CableGeneratorTheme.SecondaryTextStyle, GUILayout.Width(54));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(generator, "スプラインの開閉を切り替え");
                spline.Closed = isClosed; // 変更通知でメッシュは再構築される
                EditorUtility.SetDirty(generator);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("↕ 反転", "スプラインの始点と終点を入れ替えます"), CableGeneratorTheme.SecondaryButtonStyle))
                FlipSplineDirection(generator);

            if (GUILayout.Button(new GUIContent("⟺ 均等配置", "全制御点を曲線弧長に沿って等間隔に再配置します"), CableGeneratorTheme.SecondaryButtonStyle))
                RedistributeKnotsEvenly(generator);
            EditorGUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        // ================================================================
        //  Knot List & Selected Knot Detail (V1 SplineContainerEditor 統合)
        // ================================================================

        void DrawKnotListContents(CableGenerator generator, CableSpline spline)
        {
            int count       = spline.Count;
            int deleteIndex = -1;

            for (int i = 0; i < count; i++)
            {
                bool isSelected = i == s_snapKnotIndex;
                bool isFirst    = i == 0;
                bool isLast     = i == count - 1 && !spline.Closed;

                var rowStyle = isSelected
                    ? CableGeneratorTheme.KnotRowSelectedStyle
                    : CableGeneratorTheme.KnotRowStyle;

                GUILayout.BeginHorizontal(rowStyle);

                string labelText = isFirst ? $"[{i}] スタート"
                                 : isLast  ? $"[{i}] エンド"
                                 :           $"[{i}]";

                if (GUILayout.Button(labelText, CableGeneratorTheme.KnotLabelButtonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22)))
                {
                    s_snapKnotIndex = i;
                    s_selectedKnotIndices.Clear();
                    SceneView.RepaintAll();
                    Repaint();
                }

                CableTangentMode currentMode = spline.GetTangentMode(i);
                EditorGUI.BeginChangeCheck();
                int newSimpleIdx = EditorGUILayout.Popup(ToSimpleIndex(currentMode), kTangentModeLabels, GUILayout.Width(72));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(generator, "接線モードを変更");
                    spline.SetTangentMode(i, FromSimpleIndex(newSimpleIdx));
                    EditorUtility.SetDirty(generator);
                }

                EditorGUI.BeginDisabledGroup(count <= 2);
                if (GUILayout.Button("×", CableGeneratorTheme.KnotDeleteButtonStyle, GUILayout.Width(22), GUILayout.Height(22)))
                    deleteIndex = i;
                EditorGUI.EndDisabledGroup();

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);

            if (GUILayout.Button("＋ 制御点を追加", CableGeneratorTheme.ActionButtonStyle))
                AddKnotAfterSelected(generator);

            if (deleteIndex >= 0 && count > 2)
            {
                Undo.RecordObject(generator, "制御点を削除");
                spline.RemoveAt(deleteIndex);
                if (s_snapKnotIndex >= deleteIndex)
                    s_snapKnotIndex--;
                s_snapKnotIndex = Mathf.Clamp(s_snapKnotIndex, 0, Mathf.Max(0, spline.Count - 1));
                s_selectedKnotIndices.Clear();
                EditorUtility.SetDirty(generator);
                SceneView.RepaintAll();
            }
        }

        void DrawSelectedKnotDetailContents(CableGenerator generator, CableSpline spline)
        {
            if (spline.Count == 0) return;
            // 他のケーブルから引き継いだ範囲外の選択を正規化（◀▶ が効かなくなるのを防ぐ）
            s_snapKnotIndex = Mathf.Clamp(s_snapKnotIndex, 0, spline.Count - 1);
            int index = s_snapKnotIndex;

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            bool useGlobal = GUILayout.Toggle(UseGlobalKnotCoords,
                UseGlobalKnotCoords ? "Global" : "Local",
                CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck()) UseGlobalKnotCoords = useGlobal;

            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(index <= 0);
            if (GUILayout.Button("◀", CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(26)))
            {
                s_snapKnotIndex--;
                s_selectedKnotIndices.Clear();
                SceneView.RepaintAll();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(index >= spline.Count - 1);
            if (GUILayout.Button("▶", CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(26)))
            {
                s_snapKnotIndex++;
                s_selectedKnotIndices.Clear();
                SceneView.RepaintAll();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            var knot = spline[index];

            // ワールド座標フィールド
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("位置", CableGeneratorTheme.CaptionStyle, GUILayout.Width(32));
            Vector3 worldPos = generator.transform.TransformPoint(knot.position);
            EditorGUI.BeginChangeCheck();
            Vector3 newWorldPos = EditorGUILayout.Vector3Field(GUIContent.none, worldPos);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(generator, "制御点を移動");
                knot.position = generator.transform.InverseTransformPoint(newWorldPos);
                spline.SetKnot(index, knot);
                EditorUtility.SetDirty(generator);
            }
            EditorGUILayout.EndHorizontal();

            // 区間たわみフィールド
            if (index < spline.SegmentCount)
            {
                EditorGUI.BeginChangeCheck();
                float newSag = EditorGUILayout.Slider("この区間のたわみ (m)", knot.sag, 0f, 3f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(generator, "区間たわみを変更");
                    spline.SetSegmentSag(index, newSag);
                    EditorUtility.SetDirty(generator);
                }
            }

            GUILayout.Space(4);

            // 回転 (15° ステップボタン付き)
            Vector3 euler = UseGlobalKnotCoords
                ? (generator.transform.rotation * knot.rotation).eulerAngles
                : knot.rotation.eulerAngles;

            EditorGUI.BeginChangeCheck();
            DrawRotationXYZ("回転 (ねじれ補正)", ref euler);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(generator, "ノット回転を変更");
                knot = spline[index];
                knot.rotation = UseGlobalKnotCoords
                    ? Quaternion.Inverse(generator.transform.rotation) * Quaternion.Euler(euler)
                    : Quaternion.Euler(euler);
                spline.SetKnot(index, knot);
                EditorUtility.SetDirty(generator);
            }

            if (knot.mode == CableTangentMode.AutoSmooth)
            {
                EditorGUILayout.HelpBox("自動（AutoSmooth）モード中は接線ハンドルが自動調整されます。手動調整するには「スムーズ」または「コーナー」に切り替えてください。", MessageType.None);
            }
            else if (knot.mode == CableTangentMode.Mirrored || knot.mode == CableTangentMode.Continuous)
            {
                float currentLen = Mathf.Max(0.01f, knot.tangentOut.magnitude);
                EditorGUI.BeginChangeCheck();
                bool sliding = DrawRateSlider("ハンドル長さ（同期）", "Len_Smooth", ref currentLen);
                if (EditorGUI.EndChangeCheck() || sliding)
                {
                    Undo.RecordObject(generator, "接線長さを変更");
                    knot = spline[index];
                    knot.tangentOut = (knot.tangentOut.sqrMagnitude > 1e-6f ? knot.tangentOut.normalized : Vector3.forward) * currentLen;
                    knot.tangentIn  = (knot.tangentIn.sqrMagnitude  > 1e-6f ? knot.tangentIn.normalized  : Vector3.back)    * currentLen;
                    spline.SetKnot(index, knot);
                    EditorUtility.SetDirty(generator);
                }
            }
            else if (knot.mode == CableTangentMode.Broken)
            {
                float tOutLen = Mathf.Max(0.01f, knot.tangentOut.magnitude);
                float tInLen  = Mathf.Max(0.01f, knot.tangentIn.magnitude);

                EditorGUI.BeginChangeCheck();
                bool sOut = DrawRateSlider("タンジェントOut 長さ", "Len_Out", ref tOutLen);
                if (EditorGUI.EndChangeCheck() || sOut)
                {
                    Undo.RecordObject(generator, "タンジェントOut長さを変更");
                    knot = spline[index];
                    knot.tangentOut = (knot.tangentOut.sqrMagnitude > 1e-6f ? knot.tangentOut.normalized : Vector3.forward) * tOutLen;
                    spline.SetKnot(index, knot);
                    EditorUtility.SetDirty(generator);
                }

                EditorGUI.BeginChangeCheck();
                bool sIn = DrawRateSlider("タンジェントIn 長さ", "Len_In", ref tInLen);
                if (EditorGUI.EndChangeCheck() || sIn)
                {
                    Undo.RecordObject(generator, "タンジェントIn長さを変更");
                    knot = spline[index];
                    knot.tangentIn = (knot.tangentIn.sqrMagnitude > 1e-6f ? knot.tangentIn.normalized : Vector3.back) * tInLen;
                    spline.SetKnot(index, knot);
                    EditorUtility.SetDirty(generator);
                }
            }
        }

        static void DrawAxisRotationRow(string label, ref float eulerAxis)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label(label, CableGeneratorTheme.CaptionStyle, GUILayout.Width(16));

            float displayAngle = (float)System.Math.Round(eulerAxis, 2);

            EditorGUI.BeginChangeCheck();
            float newAngle = EditorGUILayout.FloatField(GUIContent.none, displayAngle, GUILayout.Width(50));
            if (EditorGUI.EndChangeCheck()) eulerAxis = newAngle;

            if (GUILayout.Button("0", CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(22))) { eulerAxis  = 0f;  GUI.changed = true; }
            if (GUILayout.Button("-", CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(22))) { eulerAxis -= 15f; GUI.changed = true; }
            if (GUILayout.Button("+", CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(22))) { eulerAxis += 15f; GUI.changed = true; }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        static void DrawRotationXYZ(string header, ref Vector3 eulerAngles)
        {
            GUILayout.Label(header, CableGeneratorTheme.SecondaryTextStyle);
            DrawAxisRotationRow("X", ref eulerAngles.x);
            DrawAxisRotationRow("Y", ref eulerAngles.y);
            DrawAxisRotationRow("Z", ref eulerAngles.z);
        }

        // ================================================================
        //  UI Helpers
        // ================================================================

        /// <summary>三角形数（mesh.triangles は配列を確保するため使わない）</summary>
        static int TriangleCount(Mesh mesh)
        {
            if (mesh == null || mesh.subMeshCount == 0) return 0;
            return (int)(mesh.GetIndexCount(0) / 3);
        }

        public static CableKnotAttachment AddAttachment(CableGenerator generator, int knotIndex)
        {
            var child = new GameObject($"Attachment_Knot{knotIndex}");
            Undo.RegisterCreatedObjectUndo(child, "Add Knot Attachment");
            child.transform.SetParent(generator.transform, false);
            child.layer = generator.gameObject.layer;
            var attachment = child.AddComponent<CableKnotAttachment>();
            attachment.knotIndex = knotIndex;
            attachment.UpdateAttachment();
            Selection.activeGameObject = child;
            return attachment;
        }

        /// <summary>
        /// 旧バージョンのライブボーン機能が残した Bones_Root（スキニングされていない）を案内・削除する。
        /// </summary>
        static void DrawLegacyBonesNotice(CableGenerator generator)
        {
            Transform legacy = generator.transform.Find(CableGenerator.kLegacyBonesRootName);
            if (legacy == null) return;

            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "旧バージョンのボーン階層 (Bones_Root) が残っています。ボーン付きケーブルは「エクスポート → FBX を書き出してシーンに配置」で作成する方式に変わりました。",
                MessageType.Info);
            if (GUILayout.Button("旧 Bones_Root を削除", CableGeneratorTheme.SecondaryButtonStyle))
            {
                if (CableObjectUtility.CanDestroy(legacy.gameObject))
                {
                    Undo.DestroyObjectImmediate(legacy.gameObject);
                    var smr = generator.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null && CableObjectUtility.CanDestroy(smr)) Undo.DestroyObjectImmediate(smr);
                    var mr = generator.GetComponent<MeshRenderer>();
                    if (mr != null && !mr.enabled)
                    {
                        Undo.RecordObject(mr, "Enable Cable Renderer");
                        mr.enabled = true;
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("削除できません", "Prefab の一部のため削除できません。Prefab モードで削除してください。", "OK");
                }
                GUIUtility.ExitGUI();
            }
        }

        void DrawSection(string title, System.Action content)
        {
            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);
            GUILayout.Label(title, CableGeneratorTheme.SectionHeaderStyle);

            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, CableGeneratorTheme.Outline);
            EditorGUILayout.Space(4);

            content?.Invoke();
            GUILayout.EndVertical();
        }

        void DrawFoldableSection(string title, ref bool foldout, System.Action content)
        {
            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);

            string label = (foldout ? "▼  " : "▶  ") + title;
            if (GUILayout.Button(label, CableGeneratorTheme.SectionHeaderStyle))
            {
                foldout     = !foldout;
                GUI.changed = true;
            }

            if (foldout)
            {
                var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(rect, CableGeneratorTheme.Outline);
                EditorGUILayout.Space(4);
                content?.Invoke();
            }

            GUILayout.EndVertical();
        }
    }
}


using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  Inspector Section Draw Methods
        // ================================================================

        void DrawProfileAndMeshSection(CableGenerator generator)
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

            if (EditorGUI.EndChangeCheck())
                serializedObject.ApplyModifiedProperties();

            GUILayout.Space(6);
            if (GUILayout.Button(" メッシュを再生成 ", CableGeneratorTheme.ActionButtonStyle))
                generator.RequestRefresh();
        }

        void DrawLodSection(CableGenerator generator)
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
        }

        void DrawSplineSetupSection(CableGenerator generator)
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
        }

        void DrawCableSagSection(CableGenerator generator, CableSpline spline)
        {
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
        }

        void DrawKnotProjectionSection(CableGenerator generator)
        {
            EditorGUI.BeginChangeCheck();
            s_snapKnotIndex = Mathf.Max(0, EditorGUILayout.IntField("対象ノット Index", s_snapKnotIndex));
            if (EditorGUI.EndChangeCheck())
                SelectSingleKnot(s_snapKnotIndex);

            if (s_selectedKnotIndices.Count > 1)
            {
                var sorted = new List<int>(s_selectedKnotIndices);
                sorted.Sort();
                GUILayout.Label($"複数選択中: {string.Join(", ", sorted)}", CableGeneratorTheme.CaptionStyle);
            }
            else
            {
                GUILayout.Label("Shift / Ctrl + クリック、またはドラッグの矩形で複数選択", CableGeneratorTheme.CaptionStyle);
            }

            s_snapDirection        = EditorGUILayout.Vector3Field("投影方向", s_snapDirection);
            s_snapDirectionIsLocal = EditorGUILayout.Toggle("方向をローカル扱い", s_snapDirectionIsLocal);
            s_snapMaxDistance      = Mathf.Max(0f, EditorGUILayout.FloatField("最大距離", s_snapMaxDistance));
            s_snapSurfaceOffset    = Mathf.Max(0f, EditorGUILayout.FloatField("面オフセット", s_snapSurfaceOffset));
            s_snapLayerMask        = DrawLayerMaskField("LayerMask", s_snapLayerMask);

            GUILayout.Space(6);

            string snapButtonLabel = s_selectedKnotIndices.Count > 1
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
        }

        void DrawKnotSubdivisionSection(CableGenerator generator)
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
        }

        void DrawAttachmentsSection(CableGenerator generator, CableSpline spline)
        {
            int knotCount = spline.Count;
            for (int i = 0; i < knotCount; i++)
            {
                if (GUILayout.Button($"ノット {i} にアタッチメントを追加", CableGeneratorTheme.SecondaryButtonStyle))
                    AddAttachment(generator, i);
                if (i < knotCount - 1) GUILayout.Space(2);
            }
        }

        void DrawExportSection(CableGenerator generator)
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
        }
    }
}


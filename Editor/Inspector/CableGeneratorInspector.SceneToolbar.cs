using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  Scene View Top Capsule Overlay Toolbar
        // ================================================================

        void DrawSceneOverlayToolbar(CableGenerator gen)
        {
            Handles.BeginGUI();

            var sceneView = SceneView.currentDrawingSceneView;
            float viewWidth = sceneView != null
                ? sceneView.position.width
                : Screen.width / EditorGUIUtility.pixelsPerPoint;

            float barWidth = Mathf.Min(viewWidth - 24f, 760f);
            float barX = (viewWidth - barWidth) * 0.5f;

            GUILayout.BeginArea(new Rect(barX, 8f, barWidth, 36f), CableGeneratorTheme.OverlayBarStyle);
            GUILayout.BeginHorizontal();

            // 1. 2点配線ボタン
            bool isPicking = s_pickingTarget == gen;
            if (GUILayout.Button(isPicking ? "🎯 2点配線中 (Esc解除)" : "🎯 2点配線",
                isPicking ? CableGeneratorTheme.ActiveToggleStyle : CableGeneratorTheme.SecondaryButtonStyle))
            {
                if (isPicking) CancelPickingMode();
                else           StartPickingMode(gen);
            }

            // 2. なぞり描画ボタン
            bool isDrawing = s_drawTarget == gen;
            if (GUILayout.Button(isDrawing ? "✏️ 描画中 (Esc解除)" : "✏️ なぞり描画",
                isDrawing ? CableGeneratorTheme.ActiveToggleStyle : CableGeneratorTheme.SecondaryButtonStyle))
            {
                if (isDrawing) CancelDrawMode();
                else           StartDrawMode(gen);
            }

            // 3. 面投影ショートカット
            if (GUILayout.Button(new GUIContent("🧲 面投影", "選択ノットを下方向のメッシュ表面へスナップ投影します"),
                CableGeneratorTheme.SecondaryButtonStyle))
            {
                SnapKnotInDirection(gen);
                Repaint();
            }

            // 4. 床判定 (Auto-Draping) トグル
            bool colOn = gen.enableSurfaceCollision;
            if (GUILayout.Button(colOn ? "🧱 床判定:ON" : "🧱 床判定:OFF",
                colOn ? CableGeneratorTheme.ActiveToggleStyle : CableGeneratorTheme.SecondaryButtonStyle))
            {
                Undo.RecordObject(gen, "Toggle Cable Surface Collision");
                gen.enableSurfaceCollision = !colOn;
                EditorUtility.SetDirty(gen);
                gen.RequestRefresh(); // 構造変更を伴い得るので OnGUI 外で実行
                Repaint();
            }

            // 5. ワンクリック LOD トグル
            bool lodOn = gen.enableLod;
            if (GUILayout.Button(lodOn ? "⚡ LOD:ON" : "⚡ LOD:OFF",
                lodOn ? CableGeneratorTheme.ActiveToggleStyle : CableGeneratorTheme.SecondaryButtonStyle))
            {
                Undo.RecordObject(gen, "Toggle Cable LOD");
                gen.enableLod = !lodOn;
                EditorUtility.SetDirty(gen);
                gen.RequestRefresh();
                Repaint();
            }

            // 6. メッシュ保存 (Bake)
            Mesh mesh0 = gen.GeneratedMesh;
            bool hasMesh = mesh0 != null && mesh0.vertexCount > 0;
            EditorGUI.BeginDisabledGroup(!hasMesh);
            if (GUILayout.Button("💾 保存(Bake)", CableGeneratorTheme.SecondaryButtonStyle))
            {
                BakeCableToSceneAndAssets(gen, bakeFolderPath);
                GUIUtility.ExitGUI();
            }

            // 7. ボーン付き FBX 書き出し（形状固定）
            if (GUILayout.Button(new GUIContent($"🦴 FBX({gen.boneCount}本)", "現在の形状を固定し、ボーン＆ウェイト付き FBX を書き出してシーンに配置します"),
                CableGeneratorTheme.SecondaryButtonStyle))
            {
                ExportCableFbx(gen, bakeFolderPath, s_fbxLodTier, true, true);
                GUIUtility.ExitGUI();
            }
            EditorGUI.EndDisabledGroup();

            // 8. ポリゴン数バッジ
            int triCount = TriangleCount(mesh0);
            Mesh lod2 = lodOn ? gen.GeneratedLod2Mesh : null;
            if (lod2 != null)
            {
                GUILayout.Label($"{triCount}→{TriangleCount(lod2)}△", CableGeneratorTheme.CaptionStyle, GUILayout.Width(68));
            }
            else
            {
                GUILayout.Label($"{triCount}△", CableGeneratorTheme.CaptionStyle, GUILayout.Width(48));
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            Handles.EndGUI();
        }
    }
}


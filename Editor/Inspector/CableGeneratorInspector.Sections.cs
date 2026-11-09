using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  UI Section Drawing Helpers
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
    }
}

using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
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
    }
}
